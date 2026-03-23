using DotnetTokenKiller.Application.Integration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CursorIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-cursor-test-{Guid.NewGuid()}");
    private readonly CursorIntegrator _sut = new();

    private string RulePath => Path.Combine(_tempDir, ".cursor", "rules", "dtk.mdc");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesRuleFile()
    {
        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.CreatedFiles.Should().ContainSingle().Which.Should().Be(RulePath);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        File.Exists(RulePath).Should().BeTrue();
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
    public async Task IntegrateAsync_RuleFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(RulePath);

        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
        content.Should().Contain("alwaysApply:");
    }

    [Fact]
    public void ProviderName_ReturnsCursor()
    {
        _sut.ProviderName.Should().Be("cursor");
    }
}
