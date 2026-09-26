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

    [Fact]
    public void AntigravityDirs_LiveUnderDotGemini()
    {
        var home = Path.Combine(Path.GetTempPath(), "dtk-home-test");
        var sut = new HomePaths(home);

        sut.AntigravityConfigDir.Should().Be(Path.Combine(home, ".gemini", "config"));
        sut.AntigravitySkillsDir.Should().Be(Path.Combine(home, ".gemini", "config", "skills"));
    }

    [Fact]
    public void PiAgentDir_DefaultsToDotPiAgent()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        new HomePaths(home).PiAgentDir.Should().Be(Path.Combine(home, ".pi", "agent"));
    }

    [Theory]
    [InlineData("rooted")]
    [InlineData("relative")]
    [InlineData("empty")]
    public void PiAgentDir_HonorsOnlyAnAbsolutePiCodingAgentDir(string kind)
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        var custom = Path.Combine(Path.GetTempPath(), "pi-agent");
        var value = kind switch { "rooted" => custom, "relative" => "pi-agent", _ => string.Empty };

        var expected = kind == "rooted" ? custom : Path.Combine(home, ".pi", "agent");
        new HomePaths(home, name => name == "PI_CODING_AGENT_DIR" ? value : null).PiAgentDir.Should().Be(expected);
    }

    [Theory]
    [InlineData("~")]
    [InlineData("~/custom-agent")]
    public void PiAgentDir_ExpandsALeadingTildeAgainstHome(string value)
    {
        var home = Path.Combine(Path.GetTempPath(), "home");

        var expected = value == "~" ? home : Path.Combine(home, "custom-agent");
        new HomePaths(home, name => name == "PI_CODING_AGENT_DIR" ? value : null).PiAgentDir.Should().Be(expected);
    }

    [Fact]
    public void CodexDir_DoesNotExpandATilde()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");

        new HomePaths(home, name => name == "CODEX_HOME" ? "~/codex" : null).CodexDir.Should().Be(Path.Combine(home, ".codex"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void OhMyPiAgentDir_WithNoProfile_HonorsPiCodingAgentDir(string? ompProfile, string? piProfile)
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        var custom = Path.Combine(Path.GetTempPath(), "shared-agent");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PI_CODING_AGENT_DIR"] = custom,
            ["OMP_PROFILE"] = ompProfile,
            ["PI_PROFILE"] = piProfile
        };

        new HomePaths(home, name => environment.GetValueOrDefault(name)).OhMyPiAgentDir.Should().Be(custom);
        new HomePaths(home, name => name == "PI_CODING_AGENT_DIR" ? "~/agent" : null).OhMyPiAgentDir
            .Should().Be(Path.Combine(home, "agent"));
    }

    [Theory]
    [InlineData("OMP_PROFILE")]
    [InlineData("PI_PROFILE")]
    public void OhMyPiAgentDir_WithAProfile_IgnoresPiCodingAgentDir(string profileVariable)
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PI_CODING_AGENT_DIR"] = Path.Combine(Path.GetTempPath(), "shared-agent"),
            [profileVariable] = "work"
        };

        new HomePaths(home, name => environment.GetValueOrDefault(name)).OhMyPiAgentDir
            .Should().Be(Path.Combine(home, ".omp", "agent"));
    }

    [Fact]
    public void OhMyPiAgentDir_IsDotOmpAgent()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        new HomePaths(home).OhMyPiAgentDir.Should().Be(Path.Combine(home, ".omp", "agent"));
    }

    [Fact]
    public void CursorDir_IsDotCursorUnderHome()
    {
        new HomePaths("/home/u").CursorDir.Should().Be(Path.Combine("/home/u", ".cursor"));
    }
}
