using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class RtkHookCoexistenceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dtk-rtk-{Guid.NewGuid()}");
    private string UserClaudeDir => Path.Combine(_tempRoot, "home", ".claude");
    private string ProjectDir => Path.Combine(_tempRoot, "project");
    private string RtkConfigPath => Path.Combine(_tempRoot, "config", "rtk", "config.toml");

    private RtkHookCoexistence CreateSut() => new(UserClaudeDir, RtkConfigPath);

    private static Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Fact]
    public void IsRtkHookPresent_NoSettingsFiles_ReturnsFalse()
    {
        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileRtkConfig_InvalidConfigPath_ReturnsAdviceWithoutThrowing()
    {
        // An invalid rtk config path (e.g. from a malformed XDG_CONFIG_HOME) makes File/Directory
        // APIs throw ArgumentException/NotSupportedException; reconcile must degrade to advice, not throw.
        var sut = new RtkHookCoexistence(UserClaudeDir, "invalid\0path/config.toml");

        var outcome = await sut.ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().ContainSingle();
    }

    [Fact]
    public async Task IsRtkHookPresent_RtkHookInUserSettings_ReturnsTrue()
    {
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "rtk hook claude" } ] } ] } }
            """);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task IsRtkHookPresent_RtkHookInProjectLocalSettings_ReturnsTrue()
    {
        await WriteAsync(Path.Combine(ProjectDir, ".claude", "settings.local.json"), """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "/usr/local/bin/rtk hook claude" } ] } ] } }
            """);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task IsRtkHookPresent_OnlyDtkHook_ReturnsFalse()
    {
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), """
            { "hooks": { "PreToolUse": [ { "matcher": "Bash",
              "hooks": [ { "type": "command", "command": "python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py" } ] } ] } }
            """);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task IsRtkHookPresent_MalformedSettings_IsIgnored()
    {
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), "NOT JSON {{{");

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileRtkConfig_MissingFile_CreatesWithDotnetExcluded()
    {
        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().Be(RtkConfigPath);
        outcome.Notes.Should().ContainSingle();
        var toml = await File.ReadAllTextAsync(RtkConfigPath);
        toml.Should().Contain("[hooks]");
        toml.Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task ReconcileRtkConfig_HooksTableWithoutKey_AddsKey()
    {
        await WriteAsync(RtkConfigPath, "# my rtk config\n[hooks]\ntransparent_prefixes = []\n");

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.UpdatedConfigPath.Should().Be(RtkConfigPath);
        var toml = await File.ReadAllTextAsync(RtkConfigPath);
        toml.Should().Contain("# my rtk config");           // comment preserved
        toml.Should().Contain("transparent_prefixes = []"); // sibling key preserved
        toml.Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task ReconcileRtkConfig_ExistingArrayWithoutDotnet_AppendsDotnet()
    {
        await WriteAsync(RtkConfigPath, "[hooks]\nexclude_commands = [\"git\", \"npm\"]\n");

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.UpdatedConfigPath.Should().Be(RtkConfigPath);
        var toml = await File.ReadAllTextAsync(RtkConfigPath);
        toml.Should().Contain("\"git\"");
        toml.Should().Contain("\"npm\"");
        toml.Should().Contain("\"dotnet\"");
    }

    [Fact]
    public async Task ReconcileRtkConfig_DotnetAlreadyExcluded_IsNoOp()
    {
        const string original = "[hooks]\nexclude_commands = [\"dotnet\", \"git\"]\n";
        await WriteAsync(RtkConfigPath, original);

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().BeEmpty();
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(original); // byte-unchanged
    }

    [Fact]
    public async Task ReconcileRtkConfig_NoHooksTable_AppendsSection()
    {
        await WriteAsync(RtkConfigPath, "[filters]\nenabled = true\n");

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.UpdatedConfigPath.Should().Be(RtkConfigPath);
        var toml = await File.ReadAllTextAsync(RtkConfigPath);
        toml.Should().Contain("[filters]");
        toml.Should().Contain("[hooks]");
        toml.Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Fact]
    public async Task ReconcileRtkConfig_MalformedToml_ReturnsAdviceWithoutThrowing()
    {
        const string broken = "[hooks\nexclude_commands = [";
        await WriteAsync(RtkConfigPath, broken);

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().ContainSingle(n => n.Contains("exclude_commands"));
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(broken); // never clobbered
    }

    [Fact]
    public async Task ReconcileRtkConfig_HooksHeaderWithTrailingComment_LeavesFileUntouchedAndAdvises()
    {
        const string original = "[hooks] # trailing comment\ntransparent_prefixes = []\n";
        await WriteAsync(RtkConfigPath, original);

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(original); // byte-unchanged
    }

    [Fact]
    public async Task ReconcileRtkConfig_ExcludeArrayInDifferentTable_DoesNotClobberForeignArrayAndAdvises()
    {
        const string original = "[other]\nexclude_commands = [\"x\"]\n[hooks]\nexclude_commands = [\"git\"]\n";
        await WriteAsync(RtkConfigPath, original);

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(original); // byte-unchanged
    }

    [Fact]
    public async Task ReconcileRtkConfig_NonArrayExcludeCommandsValue_LeavesFileUntouchedAndAdvises()
    {
        const string original = "[hooks]\nexclude_commands = \"git\"\n";
        await WriteAsync(RtkConfigPath, original);

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(original); // byte-unchanged
    }

    [Theory]
    [InlineData("""{ "permissions": { "allow": [] } }""")]                       // no hooks section
    [InlineData("""{ "hooks": [] }""")]                                          // hooks is not an object
    [InlineData("""{ "hooks": { "PostToolUse": [] } }""")]                       // no PreToolUse
    [InlineData("""{ "hooks": { "PreToolUse": {} } }""")]                        // PreToolUse is not an array
    [InlineData("""{ "hooks": { "PreToolUse": [ { "matcher": "Bash" } ] } }""")] // entry has no hooks array
    [InlineData("""{ "hooks": { "PreToolUse": [ { "hooks": {} } ] } }""")]       // inner hooks is not an array
    [InlineData("""{ "hooks": { "PreToolUse": [ { "hooks": [ { "type": "command" } ] } ] } }""")] // no command
    [InlineData("""{ "hooks": { "PreToolUse": [ { "hooks": [ { "command": 42 } ] } ] } }""")]     // command not a string
    public async Task IsRtkHookPresent_SettingsWithoutAnRtkHookCommand_ReturnsFalse(string settings)
    {
        // Every shape here is valid JSON that simply is not an rtk hook registration. Reading them
        // must be a quiet "no", not a crash — these are another tool's files and dtk does not own them.
        await WriteAsync(Path.Combine(UserClaudeDir, "settings.json"), settings);

        CreateSut().IsRtkHookPresent(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileRtkConfig_NestedArrayInsideExcludeCommands_LeavesFileUntouchedAndAdvises()
    {
        // The regex edit stops at the first ']', so a nested array makes the candidate text invalid
        // TOML. The re-parse before writing is what catches that; without it this would clobber the file.
        const string original = "[hooks]\nexclude_commands = [[\"a\"], \"git\"]\n";
        await WriteAsync(RtkConfigPath, original);

        var outcome = await CreateSut().ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().BeNull();
        outcome.UpdatedConfigPath.Should().BeNull();
        outcome.Notes.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Be(original); // byte-unchanged
    }

    [Fact]
    public async Task ReconcileRtkConfig_WritesWhereTheResolverSaysRtkReadsItsConfig()
    {
        // rtk on macOS reads ~/Library/Application Support/rtk/config.toml, not ~/.config/rtk/config.toml.
        // The exclusion must land in the file the resolver reports, and nowhere else.
        var rtkReads = Path.Combine(_tempRoot, "home", "Library", "Application Support", "rtk", "config.toml");
        var sut = new RtkHookCoexistence(UserClaudeDir, _ => Task.FromResult(rtkReads));

        var outcome = await sut.ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.CreatedConfigPath.Should().Be(rtkReads);
        (await File.ReadAllTextAsync(rtkReads)).Should().Contain("exclude_commands = [\"dotnet\"]");
        File.Exists(Path.Combine(_tempRoot, "home", ".config", "rtk", "config.toml")).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileRtkConfig_AdviceNoteNamesTheResolvedPath()
    {
        var rtkReads = Path.Combine(_tempRoot, "home", "Library", "Application Support", "rtk", "config.toml");
        await WriteAsync(rtkReads, "[hooks]\nexclude_commands = [[\"a\"], \"git\"]\n"); // the edit cannot be verified
        var sut = new RtkHookCoexistence(UserClaudeDir, _ => Task.FromResult(rtkReads));

        var outcome = await sut.ReconcileRtkConfigAsync(CancellationToken.None);

        outcome.Notes.Should().ContainSingle().Which.Should().Contain(rtkReads);
    }

    [Fact]
    public async Task ResolveConfigPathAsync_ReturnsTheResolvedPath()
    {
        (await CreateSut().ResolveConfigPathAsync(CancellationToken.None)).Should().Be(RtkConfigPath);
    }

    [Fact]
    public async Task NoteRemainingExclusion_RtkHookDetectedAndConfigExcludesDotnet_NamesTheResolvedPath()
    {
        var rtkReads = Path.Combine(_tempRoot, "home", "Library", "Application Support", "rtk", "config.toml");
        await WriteAsync(rtkReads, "[hooks]\nexclude_commands = [\"dotnet\"]\n");
        var sut = new RtkHookCoexistence(UserClaudeDir, _ => Task.FromResult(rtkReads));
        var context = UninstallContextThatRemovedSomething();

        await sut.NoteRemainingExclusionAsync(context, rtkHookDetected: true, CancellationToken.None);

        context.Notes.Should().ContainSingle().Which.Should().Contain(rtkReads);
    }

    [Fact]
    public async Task NoteRemainingExclusion_NoRtkHook_SaysNothing()
    {
        await WriteAsync(RtkConfigPath, "[hooks]\nexclude_commands = [\"dotnet\"]\n");
        var context = UninstallContextThatRemovedSomething();

        await CreateSut().NoteRemainingExclusionAsync(context, rtkHookDetected: false, CancellationToken.None);

        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task NoteRemainingExclusion_ConfigDoesNotExcludeDotnet_SaysNothing()
    {
        await WriteAsync(RtkConfigPath, "[hooks]\nexclude_commands = [\"git\"]\n");
        var context = UninstallContextThatRemovedSomething();

        await CreateSut().NoteRemainingExclusionAsync(context, rtkHookDetected: true, CancellationToken.None);

        context.Notes.Should().BeEmpty();
    }

    private IntegrationContext UninstallContextThatRemovedSomething()
    {
        var context = IntegrationContext.ForUninstall(ProjectDir, new Dictionary<string, string>());
        context.Removed.Add(Path.Combine(ProjectDir, "removed"));
        return context;
    }

    [Fact]
    public async Task ReconcileAsync_NoRtkHook_ReturnsNone()
    {
        // No settings files → no rtk hook → reconcile short-circuits, config never created.
        var outcome = await CreateSut().ReconcileAsync(ProjectDir, CancellationToken.None);

        outcome.Should().Be(RtkReconcileOutcome.None);
        File.Exists(RtkConfigPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook codex"}]}]}}""")]
    [InlineData("""{"rtk-rewrite":{"PreToolUse":[{"matcher":"run_command","hooks":[{"command":"rtk hook antigravity"}]}]}}""")]
    [InlineData("const result = await $`rtk rewrite ${command}`.quiet().nothrow()")]
    public async Task ReconcileFilesAsync_HarnessFileRunsAnRtkRewrite_ExcludesDotnet(string content)
    {
        var file = Path.Combine(ProjectDir, "harness", "hooks.json");
        await WriteAsync(file, content);

        var outcome = await CreateSut().ReconcileFilesAsync([Path.Combine(ProjectDir, "missing.json"), file], CancellationToken.None);

        outcome.CreatedConfigPath.Should().Be(RtkConfigPath);
        (await File.ReadAllTextAsync(RtkConfigPath)).Should().Contain("exclude_commands = [\"dotnet\"]");
    }

    [Theory]
    [InlineData("""{"hooks":{"PreToolUse":[{"hooks":[{"command":"dtk hook codex"}]}]}}""")]
    [InlineData("# we used to run rtk here")]
    [InlineData("trtk hook codex")]
    public async Task ReconcileFilesAsync_NoRtkRewrite_ChangesNothing(string content)
    {
        var file = Path.Combine(ProjectDir, "harness", "hooks.json");
        await WriteAsync(file, content);

        var outcome = await CreateSut().ReconcileFilesAsync([file], CancellationToken.None);

        outcome.Should().Be(RtkReconcileOutcome.None);
        File.Exists(RtkConfigPath).Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_CopiesPathsAndNotesIntoTheContext()
    {
        var context = new IntegrationContext(false);

        new RtkReconcileOutcome("created.toml", "updated.toml", ["note"]).ApplyTo(context);

        context.Created.Should().Equal("created.toml");
        context.Updated.Should().Equal("updated.toml");
        context.Notes.Should().Equal("note");
    }
}
