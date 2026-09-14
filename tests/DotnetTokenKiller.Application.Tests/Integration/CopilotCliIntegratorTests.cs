using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CopilotCliIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-copilotcli-test-{Guid.NewGuid()}");
    private readonly string _isolatedHome;
    private readonly CopilotCliIntegrator _sut;

    public CopilotCliIntegratorTests()
    {
        _isolatedHome = Path.Combine(_tempDir, "isolated-home");
        _sut = new CopilotCliIntegrator(new HomePaths(_isolatedHome));
    }

    private string HookScriptPath => Path.Combine(_tempDir, ".github", "hooks", "dotnet-to-dtk.py");
    private string HookJsonPath => Path.Combine(_tempDir, ".github", "hooks", "dtk-dotnet.json");
    private string InstructionsPath => Path.Combine(_tempDir, ".github", "copilot-instructions.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void ProviderName_ReturnsCopilotCli()
    {
        _sut.ProviderName.Should().Be("copilot-cli");
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesRegistrationAndInstructions()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(2);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(HookScriptPath).Should().BeFalse();
        File.Exists(HookJsonPath).Should().BeTrue();
        File.Exists(InstructionsPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_HookJson_HasCopilotPreToolUseShape()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(HookJsonPath)) as JsonObject;

        root.Should().NotBeNull();
        root["version"]!.GetValue<int>().Should().Be(1);
        var preToolUse = root["hooks"]!["preToolUse"]!.AsArray();
        preToolUse.Should().ContainSingle();
        var entry = preToolUse[0]!;
        entry["type"]!.GetValue<string>().Should().Be("command");
        entry["matcher"]!.GetValue<string>().Should().Be("bash");
        entry["bash"]!.GetValue<string>().Should().Be("dtk hook copilot-cli; exit 0");
        entry["powershell"]!.GetValue<string>().Should().Be("dtk hook copilot-cli; exit 0");
        entry["timeoutSec"]!.GetValue<int>().Should().Be(10);
        entry.AsObject().ContainsKey("cwd").Should().BeFalse();
    }

    [Fact]
    public async Task IntegrateAsync_Instructions_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsInstructionsAndReportsRestUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        // The registration JSON is deterministic, so a repeat run finds it byte-identical and
        // reports it unchanged rather than skipped — dtk must never print false "use --force"
        // advice for a file --force would not change. Only the section-based instructions file has
        // no such comparison and still reports skipped.
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle();
        result.UnchangedFiles.Should().Equal(HookJsonPath);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_UpdatesInstructionsAndReportsRestUnchanged()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        // The section-based instructions file has no identical-content check and is always
        // rewritten under --force. The registration JSON has nothing to write over identical
        // content, so it reports unchanged rather than updated.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().ContainSingle();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().Equal(HookJsonPath);
    }

    [Fact]
    public async Task IntegrateAsync_ExistingInstructionsWithoutMarker_NoForce_SkipsFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        const string original = "# Copilot rules\n\nBe nice.";
        await File.WriteAllTextAsync(InstructionsPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(InstructionsPath);
        (await File.ReadAllTextAsync(InstructionsPath)).Should().Be(original);
    }

    private string GlobalHookScriptPath => Path.Combine(_isolatedHome, ".copilot", "hooks", "dotnet-to-dtk.py");
    private string GlobalHookJsonPath => Path.Combine(_isolatedHome, ".copilot", "hooks", "dtk-dotnet.json");

    [Fact]
    public async Task IntegrateGlobalAsync_FreshHome_CreatesHookArtifactsOnly()
    {
        var result = await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        File.Exists(GlobalHookScriptPath).Should().BeFalse();
        File.Exists(GlobalHookJsonPath).Should().BeTrue();
        result.CreatedFiles.Should().Equal(GlobalHookJsonPath);
        result.Notes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HookJson_IsTheSameRegistration()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        (await File.ReadAllTextAsync(GlobalHookJsonPath)).Should().Be(await File.ReadAllTextAsync(HookJsonPath));
    }

    [Fact]
    public async Task IntegrateGlobalAsync_DoesNotWriteRepoInstructions()
    {
        await _sut.IntegrateGlobalAsync(false, CancellationToken.None);

        File.Exists(InstructionsPath).Should().BeFalse();
        Directory.Exists(Path.Combine(_isolatedHome, ".github")).Should().BeFalse();
        Directory.GetFiles(_isolatedHome, "copilot-instructions.md", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_PythonEraRegistration_IsReplacedWithoutForceAndTheScriptRemoved()
    {
        LegacyHookFixtures.WriteStampedScript(HookScriptPath);
        await File.WriteAllTextAsync(HookJsonPath, """
            {"version":1,"hooks":{"preToolUse":[{"type":"command","matcher":"bash","bash":"python3 dotnet-to-dtk.py","cwd":".github/hooks","timeoutSec":10}]}}
            """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Contain("dtk hook copilot-cli; exit 0").And.NotContain("python3");
        result.UpdatedFiles.Should().Contain(HookJsonPath);
        result.RemovedFiles.Should().Equal(HookScriptPath);
        Directory.Exists(Path.GetDirectoryName(HookJsonPath)).Should().BeTrue("the registration still lives there");
    }

    [Fact]
    public async Task IntegrateAsync_ForeignHookJson_IsSkippedWithoutForce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HookJsonPath)!);
        const string foreign = """{"version":1,"hooks":{"preToolUse":[{"type":"command","bash":"./my-own-check.sh"}]}}""";
        await File.WriteAllTextAsync(HookJsonPath, foreign);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Be(foreign);
        result.SkippedFiles.Should().Contain(HookJsonPath);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"hooks":{"preToolUse":[]}}""")]
    [InlineData("""{"version":1,"hooks":{"preToolUse":["dtk hook copilot-cli; exit 0"]}}""")]
    [InlineData("""{"version":1,"hooks":{"preToolUse":[{"type":"command","timeoutSec":10}]}}""")]
    public async Task IntegrateAsync_HookJsonWithoutADtkCommandEntry_IsSkippedWithoutForce(string existing)
    {
        // Only a file whose every entry runs a dtk command is provably dtk's; an empty list, a non-object
        // entry, or an entry with no command at all could be anyone's.
        Directory.CreateDirectory(Path.GetDirectoryName(HookJsonPath)!);
        await File.WriteAllTextAsync(HookJsonPath, existing);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Be(existing);
        result.SkippedFiles.Should().Contain(HookJsonPath);
    }

    [Fact]
    public async Task IntegrateAsync_HookJsonLocked_IsSkippedWithoutThrowing()
    {
        // An exclusive lock held from within this process, rather than chmod, which root ignores.
        LegacyHookFixtures.WriteStampedScript(HookScriptPath);
        const string legacy = """{"version":1,"hooks":{"preToolUse":[{"type":"command","bash":"python3 dotnet-to-dtk.py"}]}}""";
        await File.WriteAllTextAsync(HookJsonPath, legacy);

        await using (new FileStream(HookJsonPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

            result.SkippedFiles.Should().Contain(HookJsonPath);
            result.RemovedFiles.Should().BeEmpty();
        }

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Be(legacy);
        File.Exists(HookScriptPath).Should().BeTrue("a registration dtk could not read may still run the script");
    }

    [Fact]
    public async Task IntegrateAsync_SkippedRegistrationStillRunningThePythonHook_KeepsTheScript()
    {
        // Copilot CLI denies every tool call when a hook exits non-zero, so deleting the script a skipped
        // registration still runs would turn a kept-as-is install into a broken one.
        LegacyHookFixtures.WriteStampedScript(HookScriptPath);
        const string mixed = """{"version":1,"hooks":{"preToolUse":[{"type":"command","bash":"python3 dotnet-to-dtk.py","cwd":".github/hooks"},{"type":"command","bash":"./my-own-check.sh"}]}}""";
        await File.WriteAllTextAsync(HookJsonPath, mixed);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Be(mixed);
        result.SkippedFiles.Should().Contain(HookJsonPath);
        File.Exists(HookScriptPath).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().Contain(note => note.Contains(HookJsonPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntegrateAsync_AnotherHookFileStillRunsThePythonHook_KeepsTheScript()
    {
        // Copilot CLI loads every JSON file in the hooks directory, not only dtk-dotnet.json.
        LegacyHookFixtures.WriteStampedScript(HookScriptPath);
        const string legacy = """{"version":1,"hooks":{"preToolUse":[{"type":"command","bash":"python3 dotnet-to-dtk.py","cwd":".github/hooks"}]}}""";
        await File.WriteAllTextAsync(HookJsonPath, legacy);
        var other = Path.Combine(_tempDir, ".github", "hooks", "team.json");
        await File.WriteAllTextAsync(other, legacy);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Contain("dtk hook copilot-cli");
        File.Exists(HookScriptPath).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().Contain(note => note.Contains(other, StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntegrateAsync_OrphanPythonScriptWithNoRegistration_IsKeptWithANote()
    {
        LegacyHookFixtures.WriteStampedScript(HookScriptPath);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        File.Exists(HookJsonPath).Should().BeTrue();
        File.Exists(HookScriptPath).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().ContainSingle(note => note.Contains(HookScriptPath, StringComparison.Ordinal))
            .Which.Should().Contain("left in place");
    }

    [Theory]
    [InlineData("""{"version":1,"hooks":{},"hooks":{"preToolUse":[{"type":"command","bash":"python3 dotnet-to-dtk.py"}]}}""")]
    [InlineData("""{"version":1,"hooks":{"preToolUse":[{"type":"command","bash":"python3 dotnet-to-dtk.py","bash":"python3 dotnet-to-dtk.py"}]}}""")]
    public async Task IntegrateAsync_HookJsonWithDuplicateKeys_IsSkippedWithoutForce(string duplicated)
    {
        // JsonNode.Parse accepts a repeated key and throws ArgumentException only when the object is indexed.
        Directory.CreateDirectory(Path.GetDirectoryName(HookJsonPath)!);
        await File.WriteAllTextAsync(HookJsonPath, duplicated);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Be(duplicated);
        result.SkippedFiles.Should().Contain(HookJsonPath);
    }
}
