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
}
