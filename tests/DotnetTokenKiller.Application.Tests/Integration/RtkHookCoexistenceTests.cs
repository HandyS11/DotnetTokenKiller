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
    public async Task ReconcileAsync_NoRtkHook_ReturnsNone()
    {
        // No settings files → no rtk hook → reconcile short-circuits, config never created.
        var outcome = await CreateSut().ReconcileAsync(ProjectDir, CancellationToken.None);

        outcome.Should().Be(RtkReconcileOutcome.None);
        File.Exists(RtkConfigPath).Should().BeFalse();
    }
}
