using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class AntigravityIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-antigravity-test-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string HooksPath => Path.Combine(ProjectDir, ".agents", "hooks.json");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");
    private HomePaths Home => new(HomeDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private AntigravityIntegrator CreateSut() => new(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home);

    [Fact]
    public void ProviderName_IsAntigravity() => CreateSut().ProviderName.Should().Be("antigravity");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndHookGroup()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().Equal(AntigravityIntegrator.WorkspaceTrustNote);

        var group = JsonNode.Parse(await File.ReadAllTextAsync(HooksPath))!["dtk"]!["PreToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        group["matcher"]!.GetValue<string>().Should().Be("run_command");
        var handler = group["hooks"]!.AsArray().Should().ContainSingle().Subject!;
        handler["command"]!.GetValue<string>().Should().Be("dtk hook antigravity || exit 0");
        handler["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchanged()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, HooksPath);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_SharesGeminisSectionAndWritesTheGlobalHookAndSkill()
    {
        await new GeminiCliIntegrator(Home).IntegrateGlobalAsync(false, default);
        var geminiMd = Path.Combine(HomeDir, ".gemini", "GEMINI.md");
        var before = await File.ReadAllTextAsync(geminiMd);

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        (await File.ReadAllTextAsync(geminiMd)).Should().Be(before);
        result.UnchangedFiles.Should().Contain(geminiMd);
        result.CreatedFiles.Should().Equal(
            SharedInstructionArtifacts.SkillPath(Home.AntigravitySkillsDir),
            Path.Combine(HomeDir, ".gemini", "config", "hooks.json"));
        result.Notes.Should().BeEmpty("workspace trust applies to project hooks only");
    }

    [Fact]
    public async Task IntegrateAsync_RtkAntigravityPlugin_ExcludesDotnetInRtkConfig()
    {
        var rtkHooks = Path.Combine(ProjectDir, ".agents", "plugins", "rtk", "hooks.json");
        Directory.CreateDirectory(Path.GetDirectoryName(rtkHooks)!);
        await File.WriteAllTextAsync(rtkHooks,
            """{"rtk-rewrite":{"PreToolUse":[{"matcher":"run_command","hooks":[{"type":"command","command":"rtk hook antigravity"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
    }
}
