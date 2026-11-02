using System.Runtime.InteropServices;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class RtkConfigLocatorTests
{
    private const string Fallback = "/fallback/rtk/config.toml";
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);
    private static readonly string[] RewriteDotnetBuild = ["rewrite", "dotnet build"];
    private static readonly string[] ConfigArgs = ["config"];

    private static string Expected(params string[] segments) => Path.Combine(segments);

    [Fact]
    public void PlatformDefault_Linux_UsesAnAbsoluteXdgConfigHome()
    {
        var path = RtkConfigLocator.PlatformDefault(OSPlatform.Linux, "/home/u", "/xdg/config", appData: null);

        path.Should().Be(Expected("/xdg/config", "rtk", "config.toml"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/xdg")]
    public void PlatformDefault_Linux_IgnoresAMissingEmptyOrRelativeXdgConfigHome(string? xdg)
    {
        // XDG requires an absolute path. Honoring a relative one would resolve a global config
        // against whatever directory dtk happened to be launched from.
        var path = RtkConfigLocator.PlatformDefault(OSPlatform.Linux, "/home/u", xdg, appData: null);

        path.Should().Be(Expected("/home/u", ".config", "rtk", "config.toml"));
    }

    [Fact]
    public void PlatformDefault_MacOs_UsesApplicationSupportAndIgnoresXdgConfigHome()
    {
        // Confirmed on a Mac: `rtk config` printed /Users/<user>/Library/Application Support/rtk/config.toml.
        var path = RtkConfigLocator.PlatformDefault(OSPlatform.OSX, "/Users/u", "/xdg/config", appData: null);

        path.Should().Be(Expected("/Users/u", "Library", "Application Support", "rtk", "config.toml"));
    }

    [Fact]
    public void PlatformDefault_Windows_UsesAppData()
    {
        var path = RtkConfigLocator.PlatformDefault(
            OSPlatform.Windows, @"C:\Users\u", "/xdg/config", @"C:\Users\u\AppData\Roaming");

        path.Should().Be(Expected(@"C:\Users\u\AppData\Roaming", "rtk", "config.toml"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PlatformDefault_Windows_WithoutAppData_UsesTheRoamingFolderUnderHome(string? appData)
    {
        var path = RtkConfigLocator.PlatformDefault(OSPlatform.Windows, @"C:\Users\u", xdgConfigHome: null, appData);

        path.Should().Be(Expected(@"C:\Users\u", "AppData", "Roaming", "rtk", "config.toml"));
    }

    [Fact]
    public void ParseConfigCommandOutput_ReadsThePathAfterConfigOnItsOwnLine()
    {
        const string output = "Config: /a b/c.toml\n\n[tracking]\nenabled = true\n";

        RtkConfigLocator.ParseConfigCommandOutput(output).Should().Be("/a b/c.toml");
    }

    [Fact]
    public void ParseConfigCommandOutput_HandlesCrLfAndSurroundingWhitespace()
    {
        const string output = "\r\nConfig:   C:\\Users\\u\\AppData\\Roaming\\rtk\\config.toml  \r\n\r\n[tracking]\r\n";

        RtkConfigLocator.ParseConfigCommandOutput(output).Should().Be(@"C:\Users\u\AppData\Roaming\rtk\config.toml");
    }

    [Theory]
    [InlineData("")]
    [InlineData("[tracking]\nenabled = true\n")]
    [InlineData("Config:\n[tracking]\n")]
    [InlineData("Config:   \n")]
    [InlineData("see Config: /not/a/line/start\n")]
    public void ParseConfigCommandOutput_NoConfigLine_ReturnsNull(string output)
    {
        RtkConfigLocator.ParseConfigCommandOutput(output).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RtkOnPath_UsesThePathRtkPrints()
    {
        var runner = RunnerReturning(new CommandResult("Config: /Users/u/Library/Application Support/rtk/config.toml\n", string.Empty, 0));

        var path = await RtkConfigLocator.ResolveAsync(runner, () => "/usr/local/bin/rtk", Fallback, Generous, default);

        path.Should().Be("/Users/u/Library/Application Support/rtk/config.toml");
        await runner.Received(1).RunCapturedAsync(
            "/usr/local/bin/rtk", Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(ConfigArgs)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_RtkNotOnPath_UsesThePlatformDefaultWithoutRunningAnything()
    {
        var runner = Substitute.For<ICommandRunner>();

        var path = await RtkConfigLocator.ResolveAsync(runner, () => null, Fallback, Generous, default);

        path.Should().Be(Fallback);
        await runner.DidNotReceiveWithAnyArgs().RunCapturedAsync(null!, null!, default);
    }

    [Fact]
    public async Task ResolveAsync_RtkExitsNonZero_UsesThePlatformDefault()
    {
        var runner = RunnerReturning(new CommandResult("Config: /ignored/config.toml\n", "boom", 1));

        var path = await RtkConfigLocator.ResolveAsync(runner, () => "/usr/bin/rtk", Fallback, Generous, default);

        path.Should().Be(Fallback);
    }

    [Fact]
    public async Task ResolveAsync_OutputHasNoConfigLine_UsesThePlatformDefault()
    {
        var runner = RunnerReturning(new CommandResult("rtk 0.43.0\n", string.Empty, 0));

        var path = await RtkConfigLocator.ResolveAsync(runner, () => "/usr/bin/rtk", Fallback, Generous, default);

        path.Should().Be(Fallback);
    }

    [Fact]
    public async Task ResolveAsync_RtkPrintsARelativePath_UsesThePlatformDefault()
    {
        // A relative path would resolve against the directory dtk was launched from.
        var runner = RunnerReturning(new CommandResult("Config: rtk/config.toml\n", string.Empty, 0));

        var path = await RtkConfigLocator.ResolveAsync(runner, () => "/usr/bin/rtk", Fallback, Generous, default);

        path.Should().Be(Fallback);
    }

    [Fact]
    public async Task ResolveAsync_RtkCannotBeStarted_UsesThePlatformDefault()
    {
        var runner = Substitute.For<ICommandRunner>();
        runner.RunCapturedAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(new InvalidOperationException("no such file")));

        var path = await RtkConfigLocator.ResolveAsync(runner, () => "/usr/bin/rtk", Fallback, Generous, default);

        path.Should().Be(Fallback);
    }

    [Fact]
    public async Task ResolveAsync_RtkHangs_GivesUpAfterTheTimeoutAndUsesThePlatformDefault()
    {
        var runner = HangingRunner();

        var path = await RtkConfigLocator.ResolveAsync(
            runner, () => "/usr/bin/rtk", Fallback, TimeSpan.FromMilliseconds(50), default);

        path.Should().Be(Fallback);
    }

    [Fact]
    public async Task ResolveAsync_CallerCancels_PropagatesTheCancellation()
    {
        var runner = HangingRunner();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => RtkConfigLocator.ResolveAsync(runner, () => "/usr/bin/rtk", Fallback, Generous, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TryRunAsync_ReturnsNullWhenTheCommandCannotBeStartedOrTimesOut()
    {
        var missing = Substitute.For<ICommandRunner>();
        missing.RunCapturedAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(new InvalidOperationException("no such file")));

        (await RtkConfigLocator.TryRunAsync(missing, "rtk", RewriteDotnetBuild, Generous, default)).Should().BeNull();
        (await RtkConfigLocator.TryRunAsync(HangingRunner(), "rtk", RewriteDotnetBuild, TimeSpan.FromMilliseconds(50), default))
            .Should().BeNull();
    }

    private static ICommandRunner RunnerReturning(CommandResult result)
    {
        var runner = Substitute.For<ICommandRunner>();
        runner.RunCapturedAsync(default!, default!, default).ReturnsForAnyArgs(result);
        return runner;
    }

    private static ICommandRunner HangingRunner()
    {
        var runner = Substitute.For<ICommandRunner>();
        runner.RunCapturedAsync(default!, default!, default).ReturnsForAnyArgs(async call =>
        {
            await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(2));
            return new CommandResult(string.Empty, string.Empty, 0);
        });
        return runner;
    }
}
