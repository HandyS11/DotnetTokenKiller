using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class HomePathsTests
{
    [Fact]
    public void ResolvesProviderDirectoriesUnderTheGivenHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var sut = new HomePaths(home);

        sut.Home.Should().Be(home);
        sut.ClaudeDir.Should().Be(Path.Combine(home, ".claude"));
        sut.GeminiDir.Should().Be(Path.Combine(home, ".gemini"));
        sut.AiderConfPath.Should().Be(Path.Combine(home, ".aider.conf.yml"));
        sut.AiderInstructionsPath.Should().Be(Path.Combine(home, ".aider-dtk-instructions.md"));
        sut.CopilotHooksDir.Should().Be(Path.Combine(home, ".copilot", "hooks"));
    }

    [Fact]
    public void ParameterlessCtorResolvesTheRealUserProfile()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        new HomePaths().Home.Should().Be(expected);
    }

    [Fact]
    public void CodexDir_DefaultsToDotCodexUnderHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");

        new HomePaths(home).CodexDir.Should().Be(Path.Combine(home, ".codex"));
        new HomePaths(home).AgentsSkillsDir.Should().Be(Path.Combine(home, ".agents", "skills"));
    }

    [Fact]
    public void CodexDir_HonorsAnAbsoluteCodexHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var codexHome = Path.Combine(Path.GetTempPath(), "custom-codex");

        new HomePaths(home, name => name == "CODEX_HOME" ? codexHome : null).CodexDir.Should().Be(codexHome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/codex")]
    public void CodexDir_IgnoresAnEmptyOrRelativeCodexHome(string value)
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");

        new HomePaths(home, _ => value).CodexDir.Should().Be(Path.Combine(home, ".codex"));
    }

    [Fact]
    public void OpenCodeConfigDir_DefaultsToDotConfigOpencode()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");

        new HomePaths(home).OpenCodeConfigDir.Should().Be(Path.Combine(home, ".config", "opencode"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenCodeConfigDir_HonorsOnlyAnAbsoluteXdgConfigHome(bool rooted)
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var xdg = rooted ? Path.Combine(Path.GetTempPath(), "xdg") : "relative/xdg";

        var expected = rooted ? Path.Combine(xdg, "opencode") : Path.Combine(home, ".config", "opencode");
        new HomePaths(home, name => name == "XDG_CONFIG_HOME" ? xdg : null).OpenCodeConfigDir.Should().Be(expected);
    }
}
