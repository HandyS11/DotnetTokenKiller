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
}
