using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class ClaudeCodeIntegratorTests : IDisposable
{
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
    public async Task IntegrateAsync_FreshDirectory_CreatesSkillAndSettings()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(2);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py")).Should().BeFalse();
        File.Exists(Path.Combine(_tempDir, ".claude", "settings.json")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_ReportsAllFilesUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        // With generated-artifact stamping, SKILL.md is already byte-identical to the stamped
        // current template, so it reports unchanged rather than skipped. settings.json's merge is
        // also idempotent — the hook entry is already registered, so dtk can prove there is nothing
        // to write there either — so it reports unchanged too, not skipped: nothing here would
        // change if --force were added.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_ReportsAllFilesUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        // With generated-artifact stamping, a --force re-run over identical content writes nothing:
        // the skill is already current, so it reports unchanged rather than updated.
        // settings.json's merge is idempotent and the hook entry is already present, so it is also
        // unchanged, not skipped — force-independent by nature, so --force changes nothing here.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task IntegrateAsync_SkillFile_ContainsDtkContent()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md"));

        content.Should().Contain("dotnet-token-killer");
        content.Should().Contain("dtk dotnet build");
        content.Should().NotContain("dtk dtk");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJson_RegistersTheDtkHook()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        var preToolUse = root!["hooks"]!["PreToolUse"]!.AsArray();
        preToolUse.Should().ContainSingle();
        preToolUse[0]!["matcher"]!.GetValue<string>().Should().Be("Bash");
        preToolUse[0]!["hooks"]!.AsArray().Should().ContainSingle();
        preToolUse[0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook claude");
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
    public async Task IntegrateAsync_SettingsJsonAlreadyHasHook_ReportsUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        // First run created settings.json with the hook.
        // Second run should detect the hook is already registered and report it unchanged: there
        // is nothing to write, and --force would not change that.
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        result.UnchangedFiles.Should().Contain(settingsPath);
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
        json.Should().Contain("dtk hook claude");
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
    public async Task IntegrateAsync_SkillFile_DoesNotMentionPython()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(
            Path.Combine(_tempDir, ".claude", "skills", "dotnet-token-killer", "SKILL.md"));

        content.ToLowerInvariant().Should().NotContain("python", "the hook no longer needs an interpreter");
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
        json.Should().Contain("dtk hook claude");
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
    public async Task IntegrateAsync_RtkHookWithAnExistingRtkConfig_ReportsTheConfigAsUpdatedNotCreated()
    {
        // An rtk user who already has a config gets it edited in place. Reporting that under
        // "created" would tell them a file appeared when in fact one of theirs was rewritten.
        var userClaudeDir = Path.Combine(_tempDir, "rtk-home", ".claude");
        var rtkConfigPath = Path.Combine(_tempDir, "rtk-config", "rtk", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(rtkConfigPath)!);
        await File.WriteAllTextAsync(rtkConfigPath, "[hooks]\nexclude_commands = [\"git\"]\n");
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "rtk hook claude" } ] } ] } }
            """);
        var sut = new ClaudeCodeIntegrator(new RtkHookCoexistence(userClaudeDir, rtkConfigPath), new HomePaths(_isolatedHome));

        var result = await sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(rtkConfigPath);
        result.CreatedFiles.Should().NotContain(rtkConfigPath);
        var toml = await File.ReadAllTextAsync(rtkConfigPath);
        toml.Should().Contain("\"git\"").And.Contain("\"dotnet\"");
    }

    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesSkillAndSettingsUnderHomeClaudeDir()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(2);
        File.Exists(Path.Combine(_isolatedHome, ".claude", "skills", "dotnet-token-killer", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".claude", "hooks", "dotnet-to-dtk.py")).Should().BeFalse();
        File.Exists(Path.Combine(_isolatedHome, ".claude", "settings.json")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RegistersTheSameDtkHookInHomeSettings()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".claude", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        var command = root!["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        command.Should().Be("dtk hook claude");
    }

    [Fact]
    public async Task IntegrateAsync_ReposCommittedSettingsWithWholePathQuotedHookVariant_UpgradesToSingleEntry()
    {
        // Exactly this repo's own committed .claude/settings.json, whose hook command was
        // hand-edited in #120 to quote the whole path instead of just the env-var segment. This is
        // the real-world file that exposed the duplicate-entry, \uXXXX-escaping and
        // missing-final-newline bugs when 'dtk integrate claude' merged into it.
        var settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath,
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "Bash",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "python3 \"$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py\""
                      }
                    ]
                  }
                ]
              }
            }

            """);

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(settingsPath);
        var root = JsonNode.Parse(json) as JsonObject;
        var preToolUse = root!["hooks"]!["PreToolUse"]!.AsArray();

        preToolUse.Should().ContainSingle();
        preToolUse[0]!["hooks"]!.AsArray().Should().ContainSingle();
        preToolUse[0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook claude");
        json.Should().EndWith("\n").And.NotEndWith("\n\n");
    }

    [Fact]
    public async Task IntegrateAsync_PythonInstall_MigratesTheRegistrationAndRemovesTheScript()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        var settings = Path.Combine(_tempDir, ".claude", "settings.json");
        await File.WriteAllTextAsync(settings, """
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py"}]}]}}
            """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(settings)).Should().Contain("\"command\": \"dtk hook claude\"").And.NotContain("python3");
        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeFalse();
        result.RemovedFiles.Should().Equal(script);
        result.UpdatedFiles.Should().Contain(settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntegrateAsync_EditedPythonScript_IsKeptWithANoteUnlessForced(bool force)
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteEditedScript(script);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"), LegacyClaudeSettings);

        var result = await _sut.IntegrateAsync(_tempDir, force, CancellationToken.None);

        File.Exists(script).Should().Be(!force);
        if (force)
        {
            result.RemovedFiles.Should().Equal(script);
        }
        else
        {
            result.Notes.Should().Contain(note => note.Contains(script, StringComparison.Ordinal));
        }
    }

    /// <summary>A Claude Code settings file whose only hook runs the Python script.</summary>
    private const string LegacyClaudeSettings = """
        {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py"}]}]}}
        """;

    [Fact]
    public async Task IntegrateAsync_PythonRegistrationOnlyInSettingsLocal_KeepsTheScriptAndNamesThatFile()
    {
        // dtk merges only settings.json. Deleting the script settings.local.json still runs would make that
        // hook's python3 exit 2, which Claude Code treats as blocking every Bash call.
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        var local = Path.Combine(_tempDir, ".claude", "settings.local.json");
        await File.WriteAllTextAsync(local, LegacyClaudeSettings);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        File.Exists(script).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().ContainSingle(note => note.Contains(script, StringComparison.Ordinal))
            .Which.Should().Contain(local);
        (await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"))).Should().Contain("dtk hook claude");
        (await File.ReadAllTextAsync(local)).Should().Be(LegacyClaudeSettings, "dtk does not edit settings.local.json");
    }

    [Fact]
    public async Task IntegrateAsync_PythonRegistrationInBothSettingsFiles_MigratesSettingsJsonAndKeepsTheScript()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        var settings = Path.Combine(_tempDir, ".claude", "settings.json");
        var local = Path.Combine(_tempDir, ".claude", "settings.local.json");
        await File.WriteAllTextAsync(settings, LegacyClaudeSettings);
        await File.WriteAllTextAsync(local, LegacyClaudeSettings);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(settings)).Should().Contain("dtk hook claude").And.NotContain("python3");
        File.Exists(script).Should().BeTrue();
        result.Notes.Should().Contain(note => note.Contains(local, StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntegrateGlobalAsync_PythonRegistrationOnlyInHomeSettingsLocal_KeepsTheScript()
    {
        var script = Path.Combine(_isolatedHome, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        await File.WriteAllTextAsync(Path.Combine(_isolatedHome, ".claude", "settings.local.json"), LegacyClaudeSettings);

        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        File.Exists(script).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_OrphanPythonScriptWithNoRegistration_IsKeptWithANote()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        File.Exists(script).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().ContainSingle(note => note.Contains(script, StringComparison.Ordinal))
            .Which.Should().Contain("left in place");
    }

    [Fact]
    public async Task IntegrateAsync_MalformedSettings_ThrowsAndKeepsThePythonScript()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"), "{ not json");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(script).Should().BeTrue("the working Python hook must survive a settings file dtk could not update");
    }

    [Fact]
    public void ImplementsIGlobalIntegrator()
    {
        _sut.Should().BeAssignableTo<DotnetTokenKiller.Domain.Integration.IGlobalIntegrator>();
    }
}
