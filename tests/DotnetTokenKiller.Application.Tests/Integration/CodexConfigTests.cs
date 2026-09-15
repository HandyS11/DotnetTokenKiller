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
        config.HasHookApproval(HooksPath).Should().BeFalse();
        config.TrustsProject(ProjectDir).Should().BeFalse();
    }

    [Fact]
    public async Task HasHookApproval_StateKeyForThatFile_IsTrue()
    {
        // Literal TOML strings, so a Windows path's backslashes need no escaping.
        await File.WriteAllTextAsync(ConfigPath, $"""
            [hooks.state.'{HooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        var config = CodexConfig.Load(ConfigPath);

        config.HasHookApproval(HooksPath).Should().BeTrue();
        config.HasHookApproval(Path.Combine(_tempDir, "other", "hooks.json")).Should().BeFalse();
    }

    [Fact]
    public async Task TrustsProject_TrustedAncestor_IsTrue()
    {
        await File.WriteAllTextAsync(ConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"
            """);

        CodexConfig.Load(ConfigPath).TrustsProject(ProjectDir).Should().BeTrue();
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

    [Fact]
    public async Task Load_InvalidToml_IsNotReadable()
    {
        await File.WriteAllTextAsync(ConfigPath, "[hooks\nnot toml");

        CodexConfig.Load(ConfigPath).IsReadable.Should().BeFalse();
    }
}
