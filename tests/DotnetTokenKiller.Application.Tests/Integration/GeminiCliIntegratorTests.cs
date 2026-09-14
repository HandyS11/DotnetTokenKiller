using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class GeminiCliIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-gemini-test-{Guid.NewGuid()}");
    private readonly string _isolatedHome;
    private readonly GeminiCliIntegrator _sut;

    public GeminiCliIntegratorTests()
    {
        _isolatedHome = Path.Combine(_tempDir, "isolated-home");
        _sut = new GeminiCliIntegrator(new HomePaths(_isolatedHome));
    }

    private string GeminiMdPath => Path.Combine(_tempDir, "GEMINI.md");
    private string HookPath => Path.Combine(_tempDir, ".gemini", "hooks", "dotnet-to-dtk.py");
    private string SettingsPath => Path.Combine(_tempDir, ".gemini", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesGeminiMdAndSettings()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(2);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(GeminiMdPath).Should().BeTrue();
        File.Exists(HookPath).Should().BeFalse();
        File.Exists(SettingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsGeminiMdOnly()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        // GEMINI.md is section-based and reports skipped when already present without --force.
        // settings.json's merge is idempotent — the hook entry is already registered, so dtk can
        // prove there is nothing to write there — so it reports unchanged, not skipped.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle();
        result.UnchangedFiles.Should().Equal(SettingsPath);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesGeminiMdOnly()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        // GEMINI.md is overwritten because section-based writes always replace under --force.
        // settings.json's merge is idempotent and the hook entry is already present, so it is
        // unchanged, not skipped — force-independent by nature, so --force changes nothing there.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().ContainSingle();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().Equal(SettingsPath);
    }

    [Fact]
    public async Task IntegrateAsync_GeminiMd_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(GeminiMdPath);

        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJson_RegistersTheFailOpenDtkHook()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(SettingsPath);
        var root = JsonNode.Parse(json) as JsonObject;

        var beforeTool = root!["hooks"]!["BeforeTool"]!.AsArray();
        beforeTool.Should().ContainSingle();
        beforeTool[0]!["matcher"]!.GetValue<string>().Should().Be("run_shell_command");
        beforeTool[0]!["hooks"]!.AsArray().Should().ContainSingle();
        beforeTool[0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook gemini; exit 0");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettings_PreservesOtherSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, """{"theme": "dark"}""");

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(SettingsPath);
        var root = JsonNode.Parse(json) as JsonObject;

        root!["theme"]!.GetValue<string>().Should().Be("dark");
        root["hooks"].Should().NotBeNull();
    }

    [Fact]
    public async Task IntegrateAsync_SettingsAlreadyHasHook_ReportsUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UnchangedFiles.Should().Contain(SettingsPath);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettingsWithoutHooks_AddsHookSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, """{"theme": "dark"}""");

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(SettingsPath);
        var json = await File.ReadAllTextAsync(SettingsPath);
        json.Should().Contain("BeforeTool");
        json.Should().Contain("dtk hook gemini; exit 0");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsWithDifferentHookInBeforeTool_AddsOurHook()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, """
                                                   {
                                                     "hooks": {
                                                       "BeforeTool": [
                                                         {
                                                           "matcher": "write_file",
                                                           "hooks": [{ "type": "command", "command": "some-other-hook.sh" }]
                                                         }
                                                       ]
                                                     }
                                                   }
                                                   """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(SettingsPath);
        var json = await File.ReadAllTextAsync(SettingsPath);
        json.Should().Contain("dtk hook gemini; exit 0");
        json.Should().Contain("some-other-hook.sh");
    }

    [Fact]
    public async Task IntegrateAsync_InvalidJsonSettings_ThrowsInvalidOperationException()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, "NOT VALID JSON {{{");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to parse JSON*");
    }

    [Fact]
    public async Task IntegrateAsync_NonObjectJsonRoot_ThrowsInvalidOperationException()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, "[1, 2, 3]");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must contain a JSON object at the root*");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingGeminiMdWithoutMarker_NoForce_SkipsFile()
    {
        Directory.CreateDirectory(_tempDir);
        const string original = "# My Project\n\nDo stuff.";
        await File.WriteAllTextAsync(GeminiMdPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(GeminiMdPath);
        var content = await File.ReadAllTextAsync(GeminiMdPath);
        content.Should().Be(original);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingGeminiMdWithoutMarker_WithForce_AppendsDtkSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(GeminiMdPath, "# My Project\n\nDo stuff.");

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(GeminiMdPath);

        var content = await File.ReadAllTextAsync(GeminiMdPath);
        content.Should().Contain("# My Project");
        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("DotnetTokenKiller");
    }

    [Fact]
    public async Task IntegrateAsync_ExistingGeminiMdWithMarker_WithForce_ReplacesOnlyDtkSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(GeminiMdPath,
            "# My Project\n\n<!-- dtk -->\nOLD CONTENT\n<!-- /dtk -->\n\n## Other");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(GeminiMdPath);
        content.Should().Contain("# My Project");
        content.Should().Contain("## Other");
        content.Should().NotContain("OLD CONTENT");
        content.Should().Contain("DotnetTokenKiller");
    }

    [Fact]
    public async Task IntegrateAsync_WhitespaceOnlyGeminiMd_WithForce_CreatesDtkSection()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(GeminiMdPath, "   \n  \n  ");

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().Contain(GeminiMdPath);
        var content = await File.ReadAllTextAsync(GeminiMdPath);
        content.Should().Contain("<!-- dtk -->");
        content.Should().NotStartWith(Environment.NewLine);
    }

    [Fact]
    public void ProviderName_ReturnsGemini()
    {
        _sut.ProviderName.Should().Be("gemini");
    }

    [Fact]
    public async Task IntegrateAsync_GeminiMd_DoesNotMentionPython()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(GeminiMdPath);
        content.ToLowerInvariant().Should().NotContain("python", "the hook no longer needs an interpreter");
    }

    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesArtifactsUnderHomeGeminiDir()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        result.CreatedFiles.Should().NotBeEmpty();
        File.Exists(Path.Combine(_isolatedHome, ".gemini", "settings.json")).Should().BeTrue();
        File.Exists(Path.Combine(_isolatedHome, ".gemini", "hooks", "dotnet-to-dtk.py")).Should().BeFalse();
        File.Exists(Path.Combine(_isolatedHome, ".gemini", "GEMINI.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_ExistingSettingsWithWholePathQuotedHookVariant_UpgradesToSingleEntry()
    {
        // Same fix as ClaudeCodeIntegrator's equivalent test: MergeJsonSettingsAsync is shared, so a
        // hand-edited whole-path-quoted variant of Gemini's hook command must be recognized as the
        // same hook and upgraded in place rather than appended as a duplicate.
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath,
            """
            {
              "hooks": {
                "BeforeTool": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "python3 \"$GEMINI_PROJECT_DIR/.gemini/hooks/dotnet-to-dtk.py\""
                      }
                    ]
                  }
                ]
              }
            }

            """);

        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(SettingsPath);
        var root = JsonNode.Parse(json) as JsonObject;
        var beforeTool = root!["hooks"]!["BeforeTool"]!.AsArray();

        beforeTool.Should().ContainSingle();
        beforeTool[0]!["hooks"]!.AsArray().Should().ContainSingle();
        beforeTool[0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook gemini; exit 0");
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RegistersTheSameDtkHookInHomeSettings()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(_isolatedHome, ".gemini", "settings.json"));
        var root = JsonNode.Parse(json) as JsonObject;

        var command = root!["hooks"]!["BeforeTool"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        command.Should().Be("dtk hook gemini; exit 0");
    }

    [Fact]
    public async Task IntegrateAsync_PythonInstall_MigratesTheRegistrationAndRemovesTheScript()
    {
        var script = Path.Combine(_tempDir, ".gemini", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        var settings = Path.Combine(_tempDir, ".gemini", "settings.json");
        await File.WriteAllTextAsync(settings, """
            {"hooks":{"BeforeTool":[{"matcher":"run_shell_command","hooks":[{"type":"command","command":"python3 \"$GEMINI_PROJECT_DIR\"/.gemini/hooks/dotnet-to-dtk.py"}]}]}}
            """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(settings)).Should().Contain("\"command\": \"dtk hook gemini; exit 0\"").And.NotContain("python3");
        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeFalse();
        result.RemovedFiles.Should().Equal(script);
        result.UpdatedFiles.Should().Contain(settings);
    }

    [Fact]
    public async Task IntegrateAsync_OrphanPythonScriptWithNoRegistration_IsKeptWithANote()
    {
        LegacyHookFixtures.WriteStampedScript(HookPath);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        File.Exists(HookPath).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().ContainSingle(note => note.Contains(HookPath, StringComparison.Ordinal))
            .Which.Should().Contain("left in place");
    }

    [Fact]
    public async Task IntegrateAsync_PythonHookStillRegisteredUnderAnotherEvent_KeepsTheScript()
    {
        // The merge migrates BeforeTool only; a second registration elsewhere in the file still runs the script.
        LegacyHookFixtures.WriteStampedScript(HookPath);
        await File.WriteAllTextAsync(SettingsPath, """
            {"hooks":{
              "BeforeTool":[{"matcher":"run_shell_command","hooks":[{"type":"command","command":"python3 .gemini/hooks/dotnet-to-dtk.py"}]}],
              "AfterTool":[{"matcher":"run_shell_command","hooks":[{"type":"command","command":"python3 .gemini/hooks/dotnet-to-dtk.py"}]}]}}
            """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        File.Exists(HookPath).Should().BeTrue("Gemini CLI denies the tool call when a hook exits 2");
        result.Notes.Should().Contain(note => note.Contains(SettingsPath, StringComparison.Ordinal));
    }
}
