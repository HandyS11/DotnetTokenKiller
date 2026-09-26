using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class DevinIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-devin-test-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private HomePaths Home => new(HomeDir);
    private string RulePath => Path.Combine(ProjectDir, ".devin", "rules", "dtk.md");
    private string ProjectHooksPath => Path.Combine(ProjectDir, ".devin", "hooks.v1.json");
    private string LegacyRulePath => Path.Combine(ProjectDir, ".windsurf", "rules", "dtk.md");
    private string GlobalConfigPath => Path.Combine(Home.DevinConfigDir, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private DevinIntegrator CreateSut() => new(Home);

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public void ProviderName_IsDevin() => CreateSut().ProviderName.Should().Be("devin");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesRuleAndRootLevelHook()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(RulePath, ProjectHooksPath);
        result.Notes.Should().Equal(DevinIntegrator.CascadeNote, DevinIntegrator.RestrictedModeNote);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(ProjectHooksPath))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("PreToolUse");
        var group = root["PreToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        group["matcher"]!.GetValue<string>().Should().Be("exec");
        group["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook devin");
        group["hooks"]![0]!["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesConfigHooksAndGlobalRulesSection()
    {
        await WriteAsync(GlobalConfigPath, """{"model":"swe-1"}""");

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(Home.WindsurfGlobalRulesPath);
        result.UpdatedFiles.Should().Equal(GlobalConfigPath);
        var config = JsonNode.Parse(await File.ReadAllTextAsync(GlobalConfigPath))!;
        config["model"]!.GetValue<string>().Should().Be("swe-1");
        config["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("dtk hook devin");
        (await File.ReadAllTextAsync(Home.WindsurfGlobalRulesPath)).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public async Task IntegrateAsync_ReleasedWindsurfRule_IsRemovedAndItsDirectoriesPruned()
    {
        await new DevinIntegrator(Home).IntegrateAsync(ProjectDir, false, default);
        await WriteAsync(LegacyRulePath, await File.ReadAllTextAsync(RulePath));
        File.Delete(RulePath);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.RemovedFiles.Should().Equal(LegacyRulePath);
        Directory.Exists(Path.Combine(ProjectDir, ".windsurf")).Should().BeFalse();
    }

    [Fact]
    public async Task IntegrateAsync_EditedWindsurfRule_IsKeptWithANoteNotSkipped()
    {
        await WriteAsync(LegacyRulePath, "# my own rules\n");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        File.Exists(LegacyRulePath).Should().BeTrue();
        result.SkippedFiles.Should().NotContain(LegacyRulePath);
        result.Notes.Should().Contain(DevinIntegrator.LegacyRuleKeptNote(LegacyRulePath));
    }

    [Fact]
    public async Task IntegrateAsync_MalformedHooksFile_ThrowsAndLeavesTheOwnedLegacyRuleInPlace()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        await WriteAsync(LegacyRulePath, await File.ReadAllTextAsync(RulePath));
        await File.WriteAllTextAsync(ProjectHooksPath, "not json");

        var act = () => CreateSut().IntegrateAsync(ProjectDir, false, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(LegacyRulePath).Should().BeTrue("the hook write failed before the legacy rule was ever retired");
    }

    [Fact]
    public async Task IntegrateAsync_DtkClaudeHookInProjectSettings_AddsTheImportNote()
    {
        await WriteAsync(Path.Combine(ProjectDir, ".claude", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"dtk hook claude"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().Contain(ImportedClaudeHook.Note("Devin"));
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesRuleHookAndReleasedWindsurfRule()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        await WriteAsync(LegacyRulePath, await File.ReadAllTextAsync(RulePath));

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().BeEquivalentTo(RulePath, ProjectHooksPath, LegacyRulePath);
        Directory.Exists(Path.Combine(ProjectDir, ".devin")).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_EditedWindsurfRule_IsKeptWithTheUninstallWordingNotTheInstallWording()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        await WriteAsync(LegacyRulePath, "# my own rules\n");

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        File.Exists(LegacyRulePath).Should().BeTrue();
        result.Notes.Should().Contain(DevinIntegrator.LegacyRuleKeptOnUninstallNote(LegacyRulePath));
        result.Notes.Should().NotContain(DevinIntegrator.LegacyRuleKeptNote(LegacyRulePath));
    }

    [Fact]
    public async Task UninstallAsync_Global_RemovesSectionAndHookButKeepsOtherConfig()
    {
        await WriteAsync(GlobalConfigPath, """{"model":"swe-1"}""");
        await CreateSut().IntegrateGlobalAsync(false, default);

        await CreateSut().UninstallAsync(ProjectDir, HookScope.Global, new Dictionary<string, string>(), default);

        (await File.ReadAllTextAsync(GlobalConfigPath)).Should().NotContain("hooks").And.Contain("swe-1");
        File.Exists(Home.WindsurfGlobalRulesPath).Should().BeFalse();
    }
}
