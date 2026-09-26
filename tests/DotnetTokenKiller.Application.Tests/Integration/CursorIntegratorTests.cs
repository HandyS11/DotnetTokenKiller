using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CursorIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-cursor-test-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string RulePath => Path.Combine(ProjectDir, ".cursor", "rules", "dtk.mdc");
    private string ProjectHooksPath => Path.Combine(ProjectDir, ".cursor", "hooks.json");
    private string GlobalHooksPath => Path.Combine(HomeDir, ".cursor", "hooks.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private CursorIntegrator CreateSut()
    {
        var home = new HomePaths(HomeDir);
        return new CursorIntegrator(new RtkHookCoexistence(home.ClaudeDir, RtkConfigPath), home);
    }

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public void ProviderName_IsCursor() => CreateSut().ProviderName.Should().Be("cursor");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesRuleAndHookWithNotes()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(RulePath, ProjectHooksPath);
        result.Notes.Should().Equal(CursorIntegrator.AutoApprovalNote, CursorIntegrator.TrustNote, CursorIntegrator.KnownGapsNote);
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(ProjectHooksPath))!["hooks"]!["preToolUse"]![0]!;
        entry["command"]!.GetValue<string>().Should().Be("dtk hook cursor");
        (await File.ReadAllTextAsync(RulePath)).Should().Contain("alwaysApply: false");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(RulePath, ProjectHooksPath);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesOnlyTheHomeHookAndTheRuleNote()
    {
        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(GlobalHooksPath);
        result.Notes.Should().Equal(CursorIntegrator.AutoApprovalNote, CursorIntegrator.KnownGapsNote, CursorIntegrator.GlobalRuleNote);
        Directory.Exists(ProjectDir).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal void DescribeHooks_PointsAtTheScopesHooksJson(bool global)
    {
        var scope = global ? HookScope.Global : HookScope.Project;

        var hook = CreateSut().DescribeHooks(ProjectDir, scope).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(global ? GlobalHooksPath : ProjectHooksPath);
        hook.Command.Should().Be("dtk hook cursor");
        hook.PayloadKind.Should().Be(HookPayloadKind.Cursor);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_RtkCursorHook_ExcludesDotnetInRtksConfig()
    {
        await WriteAsync(GlobalHooksPath, """{"version":1,"hooks":{"preToolUse":[{"command":"rtk hook cursor"}]}}""");

        await CreateSut().IntegrateGlobalAsync(false, default);

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task IntegrateAsync_LegacyRtkCursorScript_ExcludesDotnetInRtksConfig()
    {
        await WriteAsync(Path.Combine(HomeDir, ".cursor", "hooks", "rtk-rewrite.sh"),
            "#!/usr/bin/env bash\n# all rewrite logic lives in `rtk rewrite`\n");

        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task IntegrateAsync_DtkClaudeHookInUserSettings_AddsTheImportNote()
    {
        await WriteAsync(Path.Combine(HomeDir, ".claude", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"dtk hook claude"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().Contain(ImportedClaudeHook.Note("Cursor"));
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesRuleAndHookAndPrunes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().BeEquivalentTo(RulePath, ProjectHooksPath);
        Directory.Exists(Path.Combine(ProjectDir, ".cursor")).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_Global_KeepsOtherHooks()
    {
        await WriteAsync(GlobalHooksPath, """{"version":1,"hooks":{"preToolUse":[{"command":"audit"}]}}""");
        await CreateSut().IntegrateGlobalAsync(false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Global, new Dictionary<string, string>(), default);

        result.UpdatedFiles.Should().Equal(GlobalHooksPath);
        (await File.ReadAllTextAsync(GlobalHooksPath)).Should().Contain("audit").And.NotContain("dtk hook cursor");
    }
}
