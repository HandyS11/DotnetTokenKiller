using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class AiderIntegratorTests : IDisposable
{
    private readonly AiderIntegrator _sut = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-aider-test-{Guid.NewGuid()}");

    private string InstructionsPath => Path.Combine(_tempDir, ".aider-dtk-instructions.md");
    private string ConfPath => Path.Combine(_tempDir, ".aider.conf.yml");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesBothFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(2);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(InstructionsPath).Should().BeTrue();
        File.Exists(ConfPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsBothFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().HaveCount(2);
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesBothFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().HaveCount(2);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_InstructionsFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_ConfFile_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(ConfPath);

        content.Should().Contain("# dtk");
        content.Should().Contain("# /dtk");
        content.Should().Contain(".aider-dtk-instructions.md");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithoutMarker_AppendsSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "auto-commits: false\n");

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(ConfPath);
        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().Contain("auto-commits: false");
        content.Should().Contain("# dtk");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithMarker_WithForce_ReplacesSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# dtk\nold-content\n# /dtk\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().NotContain("old-content");
        content.Should().Contain(".aider-dtk-instructions.md");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingConfWithMarkerButNoEndMarker_WithForce_TruncatesAtMarker()
    {
        // Covers ReplaceDtkSection when end < 0 (endMarker not found) — lines 98-99 of IntegratorHelpers.cs
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# dtk\nold-content-no-end-marker\n");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(ConfPath);
        content.Should().NotContain("old-content-no-end-marker");
        content.Should().Contain(".aider-dtk-instructions.md");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingReadKeyFlowStyle_MergesInsteadOfShadowing()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingReadKeyBlockStyle_MergesInsteadOfShadowing()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read:\n  - CONVENTIONS.md\n");

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
    }

    [Fact]
    public async Task IntegrateAsync_CommentLineMentioningReadKey_IsNotTreatedAsReadKey()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "# see the read: key below\nread: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        text.Should().Contain("CONVENTIONS.md").And.Contain(".aider-dtk-instructions.md");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingReadKey_SecondRunWithForce_DoesNotDuplicateEntry()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(ConfPath, "read: [CONVENTIONS.md]\n");

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);
        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var text = await File.ReadAllTextAsync(ConfPath);
        CountTopLevelKeys(text, "read").Should().Be(1);
        CountOccurrences(text, ".aider-dtk-instructions.md").Should().Be(1);
    }

    [Fact]
    public void ProviderName_ReturnsAider()
    {
        _sut.ProviderName.Should().Be("aider");
    }

    private static int CountTopLevelKeys(string yaml, string key)
    {
        var prefix = key + ":";
        return yaml.Split('\n').Count(line => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
