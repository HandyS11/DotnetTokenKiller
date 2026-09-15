using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CodexConfigTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-codexcfg-{Guid.NewGuid()}");

    private string ConfigPath => Path.Combine(_tempDir, "config.toml");
    private string HooksPath => Path.Combine(_tempDir, "project", ".codex", "hooks.json");
    private string ProjectDir => Path.Combine(_tempDir, "project");

    public CodexConfigTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, true);

    [Fact]
    public void Load_MissingFile_IsReadableWithNoApprovalOrTrust()
    {
        var config = CodexConfig.Load(ConfigPath);

        config.IsReadable.Should().BeTrue();
        config.HasHookApproval(HooksPath, 0, 0).Should().BeFalse();
        config.TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task HasHookApproval_StateKeyForThatHandler_IsTrueOnlyForItsFileAndPosition()
    {
        // Literal TOML strings, so a Windows path's backslashes need no escaping.
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        var config = CodexConfig.Load(ConfigPath);

        config.HasHookApproval(HooksPath, 0, 0).Should().BeTrue();
        config.HasHookApproval(Path.Combine(_tempDir, "other", "hooks.json"), 0, 0).Should().BeFalse();
        config.HasHookApproval(HooksPath, 1, 0).Should().BeFalse("Codex approves one handler, not the file");
        config.HasHookApproval(HooksPath, 0, 1).Should().BeFalse("Codex approves one handler, not its group");
    }

    [Fact]
    public async Task IsHookTurnedOff_AnotherHandlerTurnedOff_IsFalse()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            enabled = false
            """);

        CodexConfig.Load(ConfigPath).IsHookTurnedOff(HooksPath, 1, 0).Should().BeFalse();
    }

    [Fact]
    public async Task HasHookApproval_HookTurnedOffUnderHooks_IsFalseAndReportedAsTurnedOff()
    {
        // What Codex 0.154 writes when a trusted hook is switched off under /hooks.
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            enabled = false
            """);

        var config = CodexConfig.Load(ConfigPath);

        config.HasHookApproval(HooksPath, 0, 0).Should().BeFalse();
        config.IsHookTurnedOff(HooksPath, 0, 0).Should().BeTrue();
    }

    [Fact]
    public async Task HasHookApproval_HookTurnedBackOn_IsTrue()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            enabled = true
            """);

        var config = CodexConfig.Load(ConfigPath);

        config.HasHookApproval(HooksPath, 0, 0).Should().BeTrue();
        config.IsHookTurnedOff(HooksPath, 0, 0).Should().BeFalse();
    }

    [Fact]
    public async Task HasHookApproval_StateWithoutTrustedHash_IsFalse()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            enabled = true
            """);

        CodexConfig.Load(ConfigPath).HasHookApproval(HooksPath, 0, 0).Should().BeFalse();
    }

    [Fact]
    public async Task TrustsProject_TrustedDirectory_IsTrue()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{ProjectDir}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task TrustsProject_TrustedAncestorAboveTheRepositoryRoot_IsFalse()
    {
        // Codex looks up the directory itself and its repository root, never an arbitrary ancestor.
        CreateRepository(ProjectDir);
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task TrustsProject_TrustedRepositoryRootAboveTheDirectory_IsTrue()
    {
        CreateRepository(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task TrustsProject_WorktreeGitFileMarksTheRepositoryRoot_IsTrue()
    {
        // A linked worktree or submodule has a .git file rather than a directory.
        Directory.CreateDirectory(ProjectDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".git"), "gitdir: /elsewhere/.git/worktrees/wt\n");
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeTrue();
    }

    [Fact]
    public async Task TrustsProject_UntrustedDirectoryInsideATrustedRepositoryRoot_IsFalse()
    {
        // The closest entry decides, as in Codex.
        CreateRepository(_tempDir);
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"

            [projects.'{ProjectDir}']
            trust_level = "untrusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task TrustsProject_UntrustedEntry_IsFalse()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{ProjectDir}']
            trust_level = "untrusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrustsProject_KeyDifferingOnlyInCase_MatchesOnlyWhenIgnoringCase(bool ignoreCase)
    {
        // Codex lowercases project trust keys on Windows, when writing them and when looking them up.
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{ProjectDir.ToUpperInvariant()}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath, ignoreCase).TrustsProject(ProjectDir).Should().Be(ignoreCase);
    }

    [Fact]
    public async Task Load_ComparesPathKeysIgnoringCaseOnlyOnWindows()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{ProjectDir.ToUpperInvariant()}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().Be(OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrustsProject_IgnoringCase_PrefersTheLowercaseKeyAsCodexDoes(bool lowercaseFirst)
    {
        // Codex looks up the lowercased path first, and only then any key that lowercases to it.
        var lowercase = $"""
            [projects.'{LowercaseAscii(ProjectDir)}']
            trust_level = "untrusted"
            """;
        var uppercase = $"""
            [projects.'{ProjectDir.ToUpperInvariant()}']
            trust_level = "trusted"
            """;
        await File.WriteAllTextAsync(
            ConfigPath, lowercaseFirst ? $"{lowercase}\n\n{uppercase}" : $"{uppercase}\n\n{lowercase}");

        CodexConfig.Load(ConfigPath, ignoreCase: true).TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HasHookApproval_StateKeyPathDifferingOnlyInCase_MatchesOnlyWhenIgnoringCase(bool ignoreCase)
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath.ToUpperInvariant()}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        CodexConfig.Load(ConfigPath, ignoreCase).HasHookApproval(HooksPath, 0, 0).Should().Be(ignoreCase);
    }

    [Fact]
    public async Task Load_InvalidToml_IsNotReadable()
    {
        await File.WriteAllTextAsync(ConfigPath, "[hooks\nnot toml");

        CodexConfig.Load(ConfigPath).IsReadable.Should().BeFalse();
    }

    private static string LowercaseAscii(string value) =>
        string.Concat(value.Select(c => char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c));

    private static void CreateRepository(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        File.WriteAllText(Path.Combine(directory, ".git", "HEAD"), "ref: refs/heads/main\n");
    }
}
