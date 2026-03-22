using DotnetTokenKiller.Application.Integration;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class ClaudeCodeIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-claude-test-{Guid.NewGuid()}");
    private readonly ClaudeCodeIntegrator _sut = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesAllThreeFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(3);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, ".claude", "settings.json")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsAllFiles()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesSkillAndHookFiles()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, force: true, CancellationToken.None);

        // SKILL.md and the Python hook are overwritten; settings.json is skipped because
        // MergeSettingsJsonAsync is idempotent and the hook entry is already present.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().HaveCount(2);
        result.SkippedFiles.Should().ContainSingle();
    }

    [Fact]
    public async Task IntegrateAsync_SkillFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md"));

        content.Should().Contain("dotnet-token-killer");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_HookScript_ContainsPythonRewriteLogic()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py"));

        content.Should().Contain("def rewrite");
        content.Should().Contain("def main");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJson_ContainsHookEntry()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        root.Should().NotBeNull();
        root!["hooks"]!["PreToolUse"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettingsJson_PreservesOtherSettings()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """{"theme": "dark"}""");

        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(settingsPath);
        var root = JsonNode.Parse(json) as JsonObject;

        root!["theme"]!.GetValue<string>().Should().Be("dark");
        root["hooks"].Should().NotBeNull();
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJsonAlreadyHasHook_SkipsFile()
    {
        await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        // First run created settings.json with the hook.
        // Second run should detect the hook and skip.
        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        result.SkippedFiles.Should().Contain(settingsPath);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettingsWithoutHooks_AddsHookSection()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """{"theme": "dark"}""");

        var result = await _sut.IntegrateAsync(_tempDir, force: false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(settingsPath);
        var json = await File.ReadAllTextAsync(settingsPath);
        json.Should().Contain("PreToolUse");
        json.Should().Contain("dotnet-to-dtk.py");
    }
}
