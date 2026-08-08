using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class WindsurfIntegratorTests : IDisposable
{
    private readonly WindsurfIntegrator _sut = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-windsurf-test-{Guid.NewGuid()}");

    private string RulePath => Path.Combine(_tempDir, ".windsurf", "rules", "dtk.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesRuleFile()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().ContainSingle().Which.Should().Be(RulePath);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        File.Exists(RulePath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_ReportsFileUnchanged()
    {
        // The rule file's content is deterministic, so a repeat run finds it byte-identical and
        // reports it unchanged rather than skipped: WriteFileAsync must never print false
        // "use --force" advice for a file --force would not change.
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UnchangedFiles.Should().ContainSingle();
        result.SkippedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_StillReportsFileUnchanged()
    {
        // Identical content has nothing to write over, so even --force reports unchanged rather
        // than updated.
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UnchangedFiles.Should().ContainSingle();
        result.UpdatedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_RuleFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(RulePath);

        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public void ProviderName_ReturnsWindsurf()
    {
        _sut.ProviderName.Should().Be("windsurf");
    }
}
