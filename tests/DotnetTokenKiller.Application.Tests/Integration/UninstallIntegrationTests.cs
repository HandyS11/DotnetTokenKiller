using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Integration;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.Integration;

/// <summary><c>dtk init &lt;provider&gt; --uninstall</c> across every provider, through <see cref="IntegrateUseCase"/>.</summary>
public sealed class UninstallIntegrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-uninstall-it-{Guid.NewGuid()}");

    public UninstallIntegrationTests()
    {
        Directory.CreateDirectory(ProjectDir);
        Directory.CreateDirectory(HomeDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    public static TheoryData<string> AllProviders =>
        ["claude", "copilot", "copilot-cli", "gemini", "codex", "opencode", "antigravity", "cursor", "windsurf", "aider", "jetbrains"];

    public static TheoryData<string> GlobalProviders => ["claude", "copilot-cli", "gemini", "codex", "opencode", "antigravity", "aider"];

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(HomeDir, ".config", "rtk", "config.toml");
    private HomePaths Home => new(HomeDir);

    private List<IProviderIntegrator> Integrators()
    {
        var home = Home;
        RtkHookCoexistence Rtk() => new(home.ClaudeDir, RtkConfigPath);

        return
        [
            new ClaudeCodeIntegrator(Rtk(), home),
            new GitHubCopilotIntegrator(),
            new CopilotCliIntegrator(home),
            new GeminiCliIntegrator(home),
            new CodexIntegrator(Rtk(), home),
            new OpenCodeIntegrator(Rtk(), home),
            new AntigravityIntegrator(Rtk(), home),
            new CursorIntegrator(),
            new WindsurfIntegrator(),
            new AiderIntegrator(home),
            new JetBrainsAiIntegrator()
        ];
    }

    private IntegrateUseCase UseCase() => new(Integrators());

    private Task<IntegrationResult> InstallAsync(string provider, bool global = false, bool force = false) =>
        global ? UseCase().RunGlobalAsync(provider, force, default) : UseCase().RunAsync(provider, ProjectDir, force, default);

    private Task<IntegrationResult> UninstallAsync(string provider, bool global = false) =>
        UseCase().UninstallAsync(provider, ProjectDir, global, default);

    /// <summary>Every directory and file under the temp root, with each file's content.</summary>
    private Dictionary<string, string> Tree() =>
        Directory.EnumerateFileSystemEntries(_tempDir, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(_tempDir, path),
                path => Directory.Exists(path) ? "<dir>" : File.ReadAllText(path));

    private static void ShouldReportNothing(IntegrationResult result)
    {
        result.RemovedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
        result.Notes.Should().BeEmpty();
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task Uninstall_AfterAProjectInstall_RestoresTheTreeAndASecondRunFindsNothing(string provider)
    {
        var before = Tree();
        await InstallAsync(provider);
        Tree().Should().NotBeEquivalentTo(before, "the install must have written something");

        var result = await UninstallAsync(provider);

        Tree().Should().BeEquivalentTo(before);
        result.RemovedFiles.Should().NotBeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        ShouldReportNothing(await UninstallAsync(provider));
    }

    [Theory]
    [MemberData(nameof(GlobalProviders))]
    public async Task Uninstall_AfterAGlobalInstall_RestoresTheTreeAndASecondRunFindsNothing(string provider)
    {
        var before = Tree();
        await InstallAsync(provider, global: true);

        await UninstallAsync(provider, global: true);

        Tree().Should().BeEquivalentTo(before);
        ShouldReportNothing(await UninstallAsync(provider, global: true));
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task Uninstall_AfterAForcedInstallIntoUserFiles_LeavesEveryUserFileAsItWas(string provider)
    {
        // Settings files are written in the format dtk writes them in, which the merge cannot preserve otherwise.
        const string userHook = "{\"matcher\":\"Bash\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo mine\"}]}";
        foreach (var settings in new[] { ".claude/settings.json", ".gemini/settings.json", ".codex/hooks.json" })
        {
            var eventKey = settings.StartsWith(".gemini", StringComparison.Ordinal) ? "BeforeTool" : "PreToolUse";
            await IntegratorHelpers.WriteSettingsJsonAsync(
                Path.Combine(ProjectDir, settings),
                new JsonObject { ["model"] = "x", ["hooks"] = new JsonObject { [eventKey] = new JsonArray(JsonNode.Parse(userHook)) } },
                default);
        }

        await IntegratorHelpers.WriteSettingsJsonAsync(
            Path.Combine(ProjectDir, ".agents", "hooks.json"),
            new JsonObject { ["mine"] = new JsonObject { ["PreToolUse"] = new JsonArray(JsonNode.Parse(userHook)) } },
            default);
        foreach (var shared in new[] { "AGENTS.md", "GEMINI.md", ".github/copilot-instructions.md", ".junie/guidelines.md" })
        {
            Write(Path.Combine("project", shared), "# Mine\n\nKeep this.\n");
        }

        Write(Path.Combine("project", ".aider.conf.yml"), "model: gpt\n");
        var before = Tree();

        await InstallAsync(provider, force: true);
        var result = await UninstallAsync(provider);

        Tree().Should().BeEquivalentTo(before);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("read:\n  - notes.md\nmodel: gpt\n")]
    [InlineData("read: [notes.md]  # mine\nmodel: gpt\n")]
    public async Task Uninstall_Aider_TakesTheInstructionsOutOfTheUsersOwnReadKey(string original)
    {
        Write(Path.Combine("project", ".aider.conf.yml"), original);
        await InstallAsync("aider", force: true);
        (await File.ReadAllTextAsync(Path.Combine(ProjectDir, ".aider.conf.yml"))).Should().Contain(".aider-dtk-instructions.md");

        await UninstallAsync("aider");

        (await File.ReadAllTextAsync(Path.Combine(ProjectDir, ".aider.conf.yml"))).Should().Be(original);
    }

    [Fact]
    public async Task Uninstall_ProviderSharingAgentsMdWithAnInstalledOne_KeepsTheSharedFilesUntilTheLastGoes()
    {
        var before = Tree();
        await InstallAsync("codex");
        await InstallAsync("opencode");
        var agents = Path.Combine(ProjectDir, "AGENTS.md");
        var skill = SharedInstructionArtifacts.SkillPath(Path.Combine(ProjectDir, ".agents", "skills"));

        var codex = await UninstallAsync("codex");

        codex.RemovedFiles.Should().Equal(Path.Combine(ProjectDir, ".codex", "hooks.json"));
        codex.UnchangedFiles.Should().Equal(agents, skill);
        codex.Notes.Should().HaveCount(2).And.OnlyContain(note => note.Contains("opencode", StringComparison.Ordinal));
        File.Exists(agents).Should().BeTrue();

        await UninstallAsync("opencode");

        Tree().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Uninstall_Copilot_KeepsTheInstructionsWhileTheCopilotCliHookIsInstalled()
    {
        await InstallAsync("copilot-cli");

        var result = await UninstallAsync("copilot");

        result.UnchangedFiles.Should().Equal(Path.Combine(ProjectDir, ".github", "copilot-instructions.md"));
        result.Notes.Should().ContainSingle().Which.Should().Contain("copilot-cli");
    }

    [Fact]
    public async Task Uninstall_GlobalGemini_KeepsGeminiMdWhileAntigravityIsInstalledGlobally()
    {
        await InstallAsync("antigravity", global: true);
        await InstallAsync("gemini", global: true);

        var result = await UninstallAsync("gemini", global: true);

        result.UnchangedFiles.Should().Equal(Path.Combine(Home.GeminiDir, "GEMINI.md"));
        result.RemovedFiles.Should().Equal(Path.Combine(Home.GeminiDir, "settings.json"));
    }

    [Fact]
    public async Task Uninstall_EditedGeneratedFile_IsKeptAndReportedAgain()
    {
        await InstallAsync("opencode");
        var plugin = Path.Combine(ProjectDir, ".opencode", "plugins", "dtk.js");
        await File.AppendAllTextAsync(plugin, "// my edit\n");

        var first = await UninstallAsync("opencode");
        var second = await UninstallAsync("opencode");

        first.SkippedFiles.Should().Equal(plugin);
        first.Notes.Should().ContainSingle().Which.Should().StartWith($"{plugin} was kept");
        second.SkippedFiles.Should().Equal(plugin);
        second.RemovedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Uninstall_CopilotCliHookFileWithAHookOfTheUsers_IsKept()
    {
        await InstallAsync("copilot-cli");
        var hookFile = Path.Combine(ProjectDir, ".github", "hooks", "dtk-dotnet.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(hookFile))!.AsObject();
        root["hooks"]!["postToolUse"] = new JsonArray(new JsonObject { ["type"] = "command", ["bash"] = "echo mine" });
        await File.WriteAllTextAsync(hookFile, root.ToJsonString());

        var result = await UninstallAsync("copilot-cli");

        result.SkippedFiles.Should().Equal(hookFile);
        File.Exists(hookFile).Should().BeTrue();
    }

    [Fact]
    public async Task Uninstall_Codex_WithAnotherHookLeftInHooksJson_WarnsThatApprovalsMayShift()
    {
        var userHook = JsonNode.Parse("{\"matcher\":\"Bash\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo mine\"}]}");
        await IntegratorHelpers.WriteSettingsJsonAsync(
            Path.Combine(ProjectDir, ".codex", "hooks.json"),
            new JsonObject { ["hooks"] = new JsonObject { ["PreToolUse"] = new JsonArray(userHook) } },
            default);
        await InstallAsync("codex");

        var result = await UninstallAsync("codex");

        result.UpdatedFiles.Should().Contain(Path.Combine(ProjectDir, ".codex", "hooks.json"));
        result.Notes.Should().Contain(CodexIntegrator.PositionNote);
    }

    [Fact]
    public async Task Uninstall_LeavesRtksDotnetExclusionInPlaceWithANote()
    {
        Write(Path.Combine("home", ".config", "rtk", "config.toml"), "[hooks]\nexclude_commands = [\"dotnet\"]\n");
        await InstallAsync("claude");

        var result = await UninstallAsync("claude");

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be("[hooks]\nexclude_commands = [\"dotnet\"]\n");
        result.Notes.Should().ContainSingle().Which.Should().Contain(RtkConfigPath);
    }

    [Fact]
    public async Task Uninstall_RepositoryScopedProviderWithGlobal_Throws()
    {
        var act = () => UninstallAsync("cursor", global: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*repository-scoped*");
    }

    [Fact]
    public async Task Doctor_AfterUninstallingEveryHook_ReportsNoHookInstalled()
    {
        var hookProviders = new[] { "claude", "copilot-cli", "gemini", "codex", "opencode", "antigravity" };
        foreach (var provider in hookProviders)
        {
            await InstallAsync(provider);
            await InstallAsync(provider, global: true);
        }

        foreach (var provider in hookProviders)
        {
            await UninstallAsync(provider);
            await UninstallAsync(provider, global: true);
        }

        var checker = new HookHealthChecker(Substitute.For<ICommandRunner>(), () => null);
        var checks = await checker.RunAsync([.. Integrators().OfType<IHookIntegrator>()], ProjectDir, default);

        checks.Should().ContainSingle().Which.Message.Should().StartWith("No rewrite hook found");
    }
}
