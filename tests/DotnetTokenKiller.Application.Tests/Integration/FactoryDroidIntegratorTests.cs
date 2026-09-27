using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class FactoryDroidIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-droid-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string ProjectHooksJson => Path.Combine(ProjectDir, ".factory", "hooks.json");
    private string ProjectSettingsJson => Path.Combine(ProjectDir, ".factory", "settings.json");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private HomePaths Home => new(HomeDir, name => _environment.GetValueOrDefault(name));

    private FactoryDroidIntegrator CreateSut() => new(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static JsonObject DtkHandler(string path, string? containerKey)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var events = containerKey is null ? root : root[containerKey]!.AsObject();
        return events["PreToolUse"]!.AsArray()
            .Select(group => group!["hooks"]!.AsArray().Single()!.AsObject())
            .Single(handler => handler["command"]!.GetValue<string>() == "dtk hook droid");
    }

    [Fact]
    public void ProviderName_IsDroid() => CreateSut().ProviderName.Should().Be("droid");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesInstructionsSkillAndRootHooksJson()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, ProjectHooksJson);
        result.Notes.Should().Equal(FactoryDroidIntegrator.SessionNote);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(ProjectHooksJson))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("PreToolUse");
        var group = root["PreToolUse"]![0]!;
        group["matcher"]!.GetValue<string>().Should().Be("Execute");
        DtkHandler(ProjectHooksJson, null)["timeout"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task IntegrateAsync_SettingsAlreadyRunsPreToolUse_MergesThereAndCreatesNoHooksJson()
    {
        Write(ProjectSettingsJson,
            """{"model":"x","hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(ProjectSettingsJson);
        File.Exists(ProjectHooksJson).Should().BeFalse("a hooks.json PreToolUse would shadow the user's settings hooks");
        DtkHandler(ProjectSettingsJson, "hooks").Should().NotBeNull();
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("lint").And.Contain("\"model\"");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Contain(ProjectHooksJson);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsFactoryHomeOverride()
    {
        var over = Path.Combine(_tempDir, "factory-home");
        _environment["FACTORY_HOME_OVERRIDE"] = over;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        var hooks = Path.Combine(over, ".factory", "hooks.json");
        result.CreatedFiles.Should().Contain([Path.Combine(over, ".factory", "AGENTS.md"), hooks]);
        result.CreatedFiles.Should().Contain(Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"));
        DtkHandler(hooks, null).Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal void DescribeHooks_PointsAtTheResolvedFile(bool settingsRunsHooks)
    {
        if (settingsRunsHooks)
        {
            Write(ProjectSettingsJson, """{"hooks":{"PreToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"x"}]}]}}""");
        }

        var hook = CreateSut().DescribeHooks(ProjectDir, HookScope.Project).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(settingsRunsHooks ? ProjectSettingsJson : ProjectHooksJson);
        hook.Command.Should().Be("dtk hook droid");
        hook.PayloadKind.Should().Be(HookPayloadKind.FactoryDroid);
    }

    [Fact]
    public async Task IntegrateAsync_RtkDroidHook_ExcludesDotnetInRtksConfig()
    {
        Write(Path.Combine(HomeDir, ".factory", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"rtk hook droid"}]}]}}""");

        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task UninstallAsync_MalformedCandidateFile_KeepsItAndContinuesToOthers()
    {
        Write(ProjectSettingsJson,
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}}""");
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        Write(ProjectHooksJson, "not json");

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.SkippedFiles.Should().Contain(ProjectHooksJson);
        result.Notes.Should().ContainSingle(note => note.StartsWith(ProjectHooksJson, StringComparison.Ordinal));
        (await File.ReadAllTextAsync(ProjectHooksJson)).Should().Be("not json");
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("lint").And.NotContain("dtk hook droid");
    }

    [Fact]
    public async Task UninstallAsync_RemovesDtkFromEveryCandidateAndKeepsOtherHooks()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        Write(ProjectSettingsJson,
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"dtk hook droid","timeout":10}]},{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}}""");

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Contain([ProjectHooksJson, AgentsPath, SkillPath]);
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("lint").And.NotContain("dtk hook droid");
    }

    [Fact]
    public async Task IntegrateAsync_DtkOnlyHooksJsonAndALaterSettingsGuard_MovesDtkIntoSettings()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        Write(ProjectSettingsJson,
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"guard"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(ProjectSettingsJson);
        result.RemovedFiles.Should().Equal(ProjectHooksJson);
        result.Notes.Should().Contain(FactoryDroidIntegrator.SessionNote);
        File.Exists(ProjectHooksJson).Should().BeFalse("its only PreToolUse was dtk's, which shadowed the guard");
        DtkHandler(ProjectSettingsJson, "hooks").Should().NotBeNull();
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("\"guard\"");
    }

    [Fact]
    public async Task IntegrateAsync_HooksJsonWithAUserPreToolUse_KeepsDtkThere()
    {
        Write(ProjectHooksJson,
            """{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}""");
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        Write(ProjectSettingsJson,
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"guard"}]}]}}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Contain(ProjectHooksJson);
        result.RemovedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        DtkHandler(ProjectHooksJson, null).Should().NotBeNull();
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().NotContain("dtk hook droid");
    }

    [Fact]
    public async Task IntegrateAsync_DtkInSettingsAndAUserPreToolUseAddedToHooksJson_MovesDtkIntoHooksJson()
    {
        Write(ProjectSettingsJson,
            """{"hooks":{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"guard"}]}]}}""");
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        Write(ProjectHooksJson,
            """{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}""");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain([ProjectHooksJson, ProjectSettingsJson]);
        DtkHandler(ProjectHooksJson, null).Should().NotBeNull();
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Contain("guard").And.NotContain("dtk hook droid");
    }

    [Fact]
    public async Task IntegrateAsync_MalformedOtherCandidate_KeepsItWithANoteAndStillInstalls()
    {
        Write(ProjectHooksJson, "{}");
        Write(ProjectSettingsJson, "not json");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        DtkHandler(ProjectHooksJson, null).Should().NotBeNull();
        result.UpdatedFiles.Should().Contain(ProjectHooksJson);
        result.SkippedFiles.Should().Equal(ProjectSettingsJson);
        result.Notes.Should().ContainSingle(note => note.StartsWith(ProjectSettingsJson, StringComparison.Ordinal));
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Be("not json");
    }

    [Fact]
    public async Task IntegrateAsync_OtherCandidateWithCommentsAndNoDtkHook_IsLeftAloneWithoutANote()
    {
        const string settings = "{\n  // the model\n  \"model\": \"x\",\n}\n";
        Write(ProjectSettingsJson, settings);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.SkippedFiles.Should().BeEmpty();
        result.Notes.Should().Equal(FactoryDroidIntegrator.SessionNote);
        (await File.ReadAllTextAsync(ProjectSettingsJson)).Should().Be(settings);
    }
}
