using DotnetTokenKiller.Application.Integration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class JetBrainsAiIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-jetbrains-test-{Guid.NewGuid()}");
    private readonly JetBrainsAiIntegrator _sut = new();

    private string GuidelinesPath => Path.Combine(_tempDir, ".junie", "guidelines.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesGuidelinesFile()
    {
        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.CreatedFiles.Should().ContainSingle().Which.Should().Be(GuidelinesPath);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        File.Exists(GuidelinesPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsFile()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.SkippedFiles.Should().ContainSingle();
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesFile()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, force: true, CancellationToken.None);

        result.UpdatedFiles.Should().ContainSingle();
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_GuidelinesFile_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(GuidelinesPath);

        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingGuidelinesWithoutMarker_AppendsSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GuidelinesPath)!);
        await File.WriteAllTextAsync(GuidelinesPath, "# My Project Guidelines\n\nDo stuff.");

        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(GuidelinesPath);
        var content = await File.ReadAllTextAsync(GuidelinesPath);
        content.Should().Contain("# My Project Guidelines");
        content.Should().Contain("<!-- dtk -->");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingGuidelinesWithMarker_WithForce_ReplacesOnlyDtkSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GuidelinesPath)!);
        await File.WriteAllTextAsync(GuidelinesPath,
            "# Guide\n\n<!-- dtk -->\nOLD\n<!-- /dtk -->\n\n## Other");

        await _sut.IntegrateAsync(_tempDir, force: true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(GuidelinesPath);
        content.Should().Contain("# Guide");
        content.Should().Contain("## Other");
        content.Should().NotContain("OLD");
        content.Should().Contain("DotnetTokenKiller");
    }

    [Fact]
    public void ProviderName_ReturnsJetbrains()
    {
        _sut.ProviderName.Should().Be("jetbrains");
    }
}
