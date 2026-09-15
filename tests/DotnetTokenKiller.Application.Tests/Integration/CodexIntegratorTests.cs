using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CodexIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-codex-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string HooksPath => Path.Combine(ProjectDir, ".codex", "hooks.json");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private CodexIntegrator CreateSut()
    {
        var home = new HomePaths(HomeDir, name => _environment.GetValueOrDefault(name));
        return new CodexIntegrator(new RtkHookCoexistence(home.ClaudeDir, RtkConfigPath), home);
    }

    [Fact]
    public void ProviderName_IsCodex() => CreateSut().ProviderName.Should().Be("codex");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndHookWithBothNotes()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().Equal(CodexIntegrator.ApprovalNote, CodexIntegrator.ProjectTrustNote);
        (await File.ReadAllTextAsync(AgentsPath)).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public async Task IntegrateAsync_HooksJson_HoldsOnlyTheDtkPreToolUseHookForBash()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(HooksPath))!.AsObject();
        root.Count.Should().Be(1, "Codex rejects unknown top-level keys in hooks.json");
        root.ContainsKey("hooks").Should().BeTrue();
        var group = root["hooks"]!["PreToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        group["matcher"]!.GetValue<string>().Should().Be("Bash");
        var handler = group["hooks"]!.AsArray().Should().ContainSingle().Subject!;
        handler["type"]!.GetValue<string>().Should().Be("command");
        handler["command"]!.GetValue<string>().Should().Be("dtk hook codex");
        handler["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchangedWithoutRepeatingTheNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().BeEmpty("an unchanged hook keeps whatever approval the user already gave");
    }

    [Fact]
    public async Task IntegrateAsync_HooksJsonWithAForeignHook_KeepsItBesideDtks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath,
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"audit.sh"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(HooksPath);
        var json = await File.ReadAllTextAsync(HooksPath);
        json.Should().Contain("audit.sh").And.Contain("dtk hook codex");
    }

    [Fact]
    public async Task IntegrateAsync_MalformedHooksJson_Throws()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath, "{ not json");

        var act = () => CreateSut().IntegrateAsync(ProjectDir, false, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderCodexHomeAndTheSharedSkillsDirectory()
    {
        var codexHome = Path.Combine(_tempDir, "codex-home");
        _environment["CODEX_HOME"] = codexHome;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(codexHome, "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(codexHome, "hooks.json"));
        result.Notes.Should().Equal(CodexIntegrator.ApprovalNote);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WithoutCodexHome_WritesUnderDotCodex()
    {
        await CreateSut().IntegrateGlobalAsync(false, default);

        File.Exists(Path.Combine(HomeDir, ".codex", "hooks.json")).Should().BeTrue();
        File.Exists(Path.Combine(HomeDir, ".codex", "AGENTS.md")).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_RtkHookInCodexHooks_ExcludesDotnetInRtkConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath,
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook codex"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task IntegrateGlobalAsync_StaleDotCodexHooksMentioningRtk_UnderCustomCodexHome_DoesNotReconcileRtk()
    {
        _environment["CODEX_HOME"] = Path.Combine(_tempDir, "codex-home");
        var staleHooksPath = Path.Combine(HomeDir, ".codex", "hooks.json");
        Directory.CreateDirectory(Path.GetDirectoryName(staleHooksPath)!);
        await File.WriteAllTextAsync(staleHooksPath,
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook codex"}]}]}}""");

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        File.Exists(RtkConfigPath).Should().BeFalse("~/.codex/hooks.json is not what Codex reads when CODEX_HOME points elsewhere");
        result.CreatedFiles.Should().NotContain(RtkConfigPath);
    }
}
