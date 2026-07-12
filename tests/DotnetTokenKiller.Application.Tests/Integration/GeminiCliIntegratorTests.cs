using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class GeminiCliIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-gemini-test-{Guid.NewGuid()}");
    private readonly GeminiCliIntegrator _sut = new();

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
    public async Task IntegrateAsync_FreshDirectory_CreatesAllThreeFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(3);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(GeminiMdPath).Should().BeTrue();
        File.Exists(HookPath).Should().BeTrue();
        File.Exists(SettingsPath).Should().BeTrue();
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
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesGeminiMdAndHook()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        // GEMINI.md and hook are overwritten; settings.json is skipped because
        // MergeSettingsJsonAsync is idempotent and the hook entry is already present.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().HaveCount(2);
        result.SkippedFiles.Should().ContainSingle();
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
    public async Task IntegrateAsync_HookScript_ContainsPythonRewriteLogic()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(HookPath);

        content.Should().Contain("def rewrite");
        content.Should().Contain("def main");
        content.Should().Contain("hookSpecificOutput");
    }

    [Fact]
    public async Task IntegrateAsync_HookScript_KeepsGeminiSchemaDistinctFromClaude()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(HookPath);

        content.Contains("\"tool_input\"", StringComparison.Ordinal).Should().BeTrue(
            "Gemini must keep emitting its own hookSpecificOutput.tool_input payload field.");
        content.Contains("\"decision\"", StringComparison.Ordinal).Should().BeTrue(
            "Gemini must keep emitting its own top-level decision field.");
        content.Contains("\"updatedInput\"", StringComparison.Ordinal).Should().BeFalse(
            "Gemini must keep its own output schema; updatedInput belongs to Claude's schema only.");
    }

    [Fact]
    public async Task IntegrateAsync_SettingsJson_ContainsBeforeToolHook()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(SettingsPath);
        var root = JsonNode.Parse(json) as JsonObject;

        root.Should().NotBeNull();
        root["hooks"]!["BeforeTool"]!.AsArray().Should().NotBeEmpty();
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
    public async Task IntegrateAsync_SettingsAlreadyHasHook_SkipsFile()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(SettingsPath);
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
        json.Should().Contain("dotnet-to-dtk.py");
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
        json.Should().Contain("dotnet-to-dtk.py");
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
    public async Task IntegrateAsync_SettingsJson_RegistersHookViaGeminiProjectDirEnvVar()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var json = await File.ReadAllTextAsync(SettingsPath);
        var root = JsonNode.Parse(json) as JsonObject;

        var command = root!["hooks"]!["BeforeTool"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        command.Should().Be("""python3 "$GEMINI_PROJECT_DIR"/.gemini/hooks/dotnet-to-dtk.py""");
    }

    [Fact]
    public async Task IntegrateAsync_GeminiMd_DocumentsWindowsPythonCaveat()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(GeminiMdPath);
        content.Should().Contain("python3");
        content.Should().Contain("Windows");
    }
}
