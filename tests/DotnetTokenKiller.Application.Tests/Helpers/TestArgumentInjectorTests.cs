using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class TestArgumentInjectorTests
{
    [Fact]
    public void ForNames_VsTest_AppendsNormalConsoleLogger()
    {
        TestArgumentInjector.ForNames(["test", "--filter", "X"], TestRunner.VsTest)
            .Should().Equal("test", "--filter", "X", "--logger", "console;verbosity=normal");
    }

    [Theory]
    [InlineData("--logger", "console;verbosity=detailed")]
    [InlineData("-l", "Console")]
    [InlineData("--logger:console;verbosity=quiet")]
    [InlineData("--logger=console")]
    [InlineData("-l:console;verbosity=minimal")]
    [InlineData("--logger", "trx", "--logger", "console;verbosity=minimal")]
    public void ForNames_VsTest_KeepsUserConsoleLogger(params string[] logger)
    {
        // The user chose the console logger's verbosity; a second console logger would override or double it.
        string[] args = ["test", .. logger];

        TestArgumentInjector.ForNames(args, TestRunner.VsTest).Should().Equal(args);
    }

    [Theory]
    [InlineData("--logger", "trx")]
    [InlineData("--logger", "html;LogFileName=console.html")]
    [InlineData("-l")]
    public void ForNames_VsTest_AnotherLogger_StillAppendsConsole(params string[] logger)
    {
        TestArgumentInjector.ForNames(["test", .. logger], TestRunner.VsTest)
            .Should().Equal(["test", .. logger, "--logger", "console;verbosity=normal"]);
    }

    [Fact]
    public void ForNames_Mtp_AppendsDetailedOutput()
    {
        TestArgumentInjector.ForNames(["test", "--filter", "X"], TestRunner.MicrosoftTestingPlatform)
            .Should().Equal("test", "--filter", "X", "--output", "Detailed");
    }

    [Theory]
    [InlineData("--output", "Normal")]
    [InlineData("--output", "Detailed")]
    [InlineData("--output=Normal")]
    [InlineData("--output:Normal")]
    public void ForNames_Mtp_KeepsUserOutput(params string[] output)
    {
        string[] args = ["test", .. output];

        TestArgumentInjector.ForNames(args, TestRunner.MicrosoftTestingPlatform).Should().Equal(args);
    }

    [Theory]
    [InlineData("test", "--filter", "X")]
    [InlineData("test", "--filter", "X", "--", "RunConfiguration.X=1")]
    [InlineData("test")]
    public void ForNames_Unknown_Unchanged(params string[] args)
    {
        TestArgumentInjector.ForNames(args, TestRunner.Unknown).Should().Equal(args);
    }

    [Fact]
    public void ForNames_InsertsBeforeUserSeparator()
    {
        // Everything after the user's `--` is the test run's (RunSettings, or the test app's own arguments).
        string[] args = ["--filter", "X", "--", "RunConfiguration.X=1"];

        TestArgumentInjector.ForNames(args, TestRunner.VsTest)
            .Should().Equal("--filter", "X", "--logger", "console;verbosity=normal", "--", "RunConfiguration.X=1");
        TestArgumentInjector.ForNames(args, TestRunner.MicrosoftTestingPlatform)
            .Should().Equal("--filter", "X", "--output", "Detailed", "--", "RunConfiguration.X=1");
    }

    [Fact]
    public void ForNames_OptionsAfterUserSeparator_AreNotTheUsers()
    {
        // A `--logger`/`--output` after `--` is not dotnet test's option, so it does not stand for one.
        TestArgumentInjector.ForNames(["test", "--", "--logger", "console"], TestRunner.VsTest)
            .Should().Equal("test", "--logger", "console;verbosity=normal", "--", "--logger", "console");
        TestArgumentInjector.ForNames(["test", "--", "--output", "Normal"], TestRunner.MicrosoftTestingPlatform)
            .Should().Equal("test", "--output", "Detailed", "--", "--output", "Normal");
    }
}
