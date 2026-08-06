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

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
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
    public async Task DefaultInstance_ResolvesTheRtkConfigUnderXdgConfigHome()
    {
        // The parameterless constructor is what production uses; these are the paths it picks.
        var saved = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var xdgHome = Path.Combine(_tempRoot, "xdg");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgHome);
        try
        {
            var outcome = await new RtkHookCoexistence().ReconcileRtkConfigAsync(CancellationToken.None);

            outcome.CreatedConfigPath.Should().Be(Path.Combine(xdgHome, "rtk", "config.toml"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", saved);
        }
    }

    [Fact]
    public void DefaultInstance_IgnoresARelativeXdgConfigHome_RatherThanWritingIntoTheWorkingDirectory()
    {
        // XDG requires an absolute path. Honoring a relative one would resolve a global config
        // against whatever directory dtk happened to be launched from.
        const string relative = "dtk-relative-xdg-should-be-ignored";
        var saved = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", relative);
        try
        {
            var act = () => new RtkHookCoexistence();

            act.Should().NotThrow();
            Directory.Exists(Path.Combine(Environment.CurrentDirectory, relative)).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", saved);
        }
    }

    [Fact]
    public async Task ReconcileAsync_NoRtkHook_ReturnsNone()
    {
        // No settings files → no rtk hook → reconcile short-circuits, config never created.
        var outcome = await CreateSut().ReconcileAsync(ProjectDir, CancellationToken.None);

        outcome.Should().Be(RtkReconcileOutcome.None);
        File.Exists(RtkConfigPath).Should().BeFalse();
    }
}
