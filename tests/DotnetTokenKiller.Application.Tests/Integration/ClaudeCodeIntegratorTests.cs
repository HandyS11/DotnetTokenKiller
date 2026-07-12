using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class ClaudeCodeIntegratorTests : IDisposable
{
    private const string RepoMarkerFileName = "DotnetTokenKiller.slnx";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-claude-test-{Guid.NewGuid()}");
    private readonly ClaudeCodeIntegrator _sut;
    private readonly string _isolatedHome;

    public ClaudeCodeIntegratorTests()
    {
        _isolatedHome = Path.Combine(_tempDir, "isolated-home");
        var userClaudeDir = Path.Combine(_isolatedHome, ".claude");
        var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
        _sut = new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath), new HomePaths(_isolatedHome));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesAllThreeFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

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
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesSkillAndHookFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        // SKILL.md and the Python hook are overwritten; settings.json is skipped because
        // MergeSettingsJsonAsync is idempotent and the hook entry is already present.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().HaveCount(2);
        result.SkippedFiles.Should().ContainSingle();
    }

    [Fact]
    public async Task IntegrateAsync_SkillFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md"));

        content.Should().Contain("dotnet-token-killer");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_HookScript_ContainsPythonRewriteLogic()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py"));

        content.Should().Contain("def rewrite");
        content.Should().Contain("def main");
    }

    [Fact]
    public async Task IntegrateAsync_WritesHookEmittingUpdatedInputSchemaAsync()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var script = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py"));

        script.Should().Contain("hookSpecificOutput");
        script.Should().Contain("updatedInput");
        script.Should().NotContain("\"decision\"");
        script.Should().Contain("format");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJson_ContainsHookEntry()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        root.Should().NotBeNull();
        root["hooks"]!["PreToolUse"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettingsJson_PreservesOtherSettings()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """{"theme": "dark"}""");

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(settingsPath);
        var root = JsonNode.Parse(json) as JsonObject;

        root!["theme"]!.GetValue<string>().Should().Be("dark");
        root["hooks"].Should().NotBeNull();
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJsonAlreadyHasHook_SkipsFile()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        // First run created settings.json with the hook.
        // Second run should detect the hook and skip.
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        result.SkippedFiles.Should().Contain(settingsPath);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettingsWithoutHooks_AddsHookSection()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """{"theme": "dark"}""");

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(settingsPath);
        var json = await File.ReadAllTextAsync(settingsPath);
        json.Should().Contain("PreToolUse");
        json.Should().Contain("dotnet-to-dtk.py");
    }

    [Fact]
    public void ProviderName_ReturnsClaud()
    {
        _sut.ProviderName.Should().Be("claude");
    }

    [Fact]
    public async Task IntegrateAsync_InvalidJsonSettings_ThrowsInvalidOperationException()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, "NOT VALID JSON {{{");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to parse JSON*");
    }

    [Fact]
    public async Task IntegrateAsync_NonObjectJsonRoot_ThrowsInvalidOperationException()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, "[1, 2, 3]");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must contain a JSON object at the root*");
    }

    [Fact]
    public async Task IntegrateAsync_HooksIsNotJsonObject_ThrowsInvalidOperationException()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """{"hooks": [1, 2, 3]}""");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'hooks' property of unexpected type*expected a JSON object*");
    }

    [Fact]
    public async Task IntegrateAsync_HookEventKeyIsNotJsonArray_ThrowsInvalidOperationException()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """{"hooks": {"PreToolUse": "not-an-array"}}""");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'hooks.PreToolUse' property of unexpected type*expected a JSON array*");
    }

    [Fact]
    public async Task IntegrateAsync_HookScript_MatchesCommittedRepoHook()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var shippedHook = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py"));

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var committedHook = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py"));

        string.Equals(shippedHook, committedHook, StringComparison.Ordinal).Should().BeTrue(
            "HookScriptTemplates.ClaudeHook must stay byte-for-byte in sync with the repo's own " +
            ".claude/hooks/dotnet-to-dtk.py; update whichever one drifted.");
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, RepoMarkerFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing '{RepoMarkerFileName}') " +
            $"walking up from '{startDirectory}'.");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJson_RegistersHookViaClaudeProjectDirEnvVar()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        var command = root!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        command.Should().Be("""python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""");
    }

    [Fact]
    public async Task IntegrateAsync_SkillFile_DocumentsWindowsPythonCaveat()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md"));

        content.Should().Contain("python3");
        content.Should().Contain("Windows");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsWithDifferentHookInPreToolUse_AddsOurHook()
    {
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """
                                                   {
                                                     "hooks": {
                                                       "PreToolUse": [
                                                         {
                                                           "matcher": "Bash",
                                                           "hooks": [{ "type": "command", "command": "some-other-hook.sh" }]
                                                         }
                                                       ]
                                                     }
                                                   }
                                                   """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(settingsPath);
        var json = await File.ReadAllTextAsync(settingsPath);
        json.Should().Contain("dotnet-to-dtk.py");
        json.Should().Contain("some-other-hook.sh");
    }

    [Fact]
    public async Task IntegrateAsync_RtkHookInProjectSettings_ExcludesDotnetAndAddsNote()
    {
        var userClaudeDir = Path.Combine(_tempDir, "rtk-home", ".claude");
        var rtkConfigPath = Path.Combine(_tempDir, "rtk-config", "rtk", "config.toml");
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "rtk hook claude" } ] } ] } }
            """);
        var sut = new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath), new HomePaths(_isolatedHome));

        var result = await sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.Notes.Should().ContainSingle(n => n.Contains("dotnet"));
        result.CreatedFiles.Should().Contain(rtkConfigPath);
        (await File.ReadAllTextAsync(rtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesAllThreeFilesUnderHomeClaudeDir()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(3);
        File.Exists(Path.Combine(_isolatedHome, ".claude", "skills", "dotnet-token-killer", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".claude", "hooks", "dotnet-to-dtk.py")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".claude", "settings.json")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RegistersHomeRootedHookCommand()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        var command = root!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        command.Should().Be("""python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py""");
    }

    [Fact]
    public void ImplementsIGlobalIntegrator()
    {
        _sut.Should().BeAssignableTo<DotnetTokenKiller.Domain.Integration.IGlobalIntegrator>();
    }
}
