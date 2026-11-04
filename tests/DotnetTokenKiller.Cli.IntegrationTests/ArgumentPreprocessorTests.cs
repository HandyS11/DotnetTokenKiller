using System.Diagnostics.CodeAnalysis;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using Spectre.Console.Cli;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class ArgumentPreprocessorTests
{
    private static readonly Dictionary<string, string> NoEnvironment = [];

    private static readonly Dictionary<string, string> RawOutput = new() { ["DTK_RAW"] = "1" };

    // ── IsPassthrough ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dotnet", "run")]
    [InlineData("dotnet", "watch")]
    [InlineData("DOTNET", "RUN")]
    public void IsPassthrough_ReturnsTrue_ForUnknownSubcommand(string exe, string sub)
    {
        ArgumentPreprocessor.IsPassthrough([exe, sub]).Should().BeTrue();
    }

    [Theory]
    [InlineData("dotnet", "build")]
    [InlineData("dotnet", "test")]
    [InlineData("dotnet", "restore")]
    [InlineData("dotnet", "clean")]
    [InlineData("dotnet", "format")]
    [InlineData("dotnet", "publish")]
    [InlineData("dotnet", "pack")]
    [InlineData("DOTNET", "BUILD")]
    public void IsPassthrough_ReturnsFalse_ForKnownSubcommand(string exe, string sub)
    {
        ArgumentPreprocessor.IsPassthrough([exe, sub]).Should().BeFalse();
    }

    [Theory]
    [InlineData("dotnet", "publish", "--interactive")]
    [InlineData("dotnet", "pack", "-o", "out", "--interactive")]
    [InlineData("DOTNET", "PUBLISH", "--interactive")]
    public void IsPassthrough_ReturnsTrue_ForAnInteractivePublishOrPack(params string[] args)
    {
        // Filtering captures the output and closes stdin, so a credential provider's prompt would
        // never reach the user; these runs keep the terminal, as they did before dtk filtered them.
        ArgumentPreprocessor.IsPassthrough(ArgumentPreprocessor.Normalize(args)).Should().BeTrue();
    }

    [Theory]
    [InlineData("dotnet", "list", "package", "--format", "json")]
    [InlineData("dotnet", "list", "package", "--format=json")]
    [InlineData("DOTNET", "LIST", "PACKAGE", "--FORMAT", "JSON")]
    [InlineData("dotnet", "build", "-getProperty:Version")]
    [InlineData("dotnet", "test", "--getItem:Compile")]
    [InlineData("dotnet", "publish", "/getTargetResult:Build")]
    [InlineData("DOTNET", "BUILD", "-GETPROPERTY:Version")]
    public void IsPassthrough_ReturnsTrue_ForMachineReadableOutput(params string[] args)
    {
        // The output is a document or a value that another program parses: dtk's summary line or a
        // trailing warning would corrupt it, so these run unfiltered.
        ArgumentPreprocessor.IsPassthrough(ArgumentPreprocessor.Normalize(args)).Should().BeTrue();
    }

    [Theory]
    [InlineData("dotnet", "list", "package", "--outdated")]
    [InlineData("dotnet", "list", "package", "--format", "console")]
    [InlineData("dotnet", "build", "-c", "Release")]
    public void IsPassthrough_ReturnsFalse_ForHumanReadableOutput(params string[] args)
    {
        ArgumentPreprocessor.IsPassthrough(args).Should().BeFalse();
    }

    [Fact]
    public void PassthroughArguments_UnknownSubcommand_IsForwardedVerbatim()
    {
        // dtk does not filter `dotnet run`, so its -q or --quiet belongs to dotnet (or the app), not to dtk.
        ArgumentPreprocessor.PassthroughArguments(["dotnet", "run", "-q", "--", "-v"])
            .Should().Equal("run", "-q", "--", "-v");
    }

    [Fact]
    public void PassthroughArguments_FilteredSubcommand_DropsDtkOptionsLikeRawOutput()
    {
        // A filtered subcommand passed through is the same as DTK_RAW=1: dotnet rejects dtk's own options.
        ArgumentPreprocessor.PassthroughArguments(
                ["dotnet", "build", "-q", "-getProperty:Version", "-v", "minimal", "--show-log"])
            .Should().Equal("build", "-getProperty:Version", "-v", "minimal");
    }

    [Fact]
    public void PassthroughArguments_ToolRunKeepsTeeVerbatim()
    {
        // dtk reads its options only on the subcommands it filters: `--tee y` belongs to the tool.
        ArgumentPreprocessor.PassthroughArguments(["dotnet", "tool", "run", "x", "--tee", "y"])
            .Should().Equal("tool", "run", "x", "--tee", "y");
    }

    [Fact]
    public void PassthroughArguments_FilteredSubcommand_DropsValueOptionAndValue()
    {
        ArgumentPreprocessor.PassthroughArguments(
                ["dotnet", "list", "package", "--max-lines", "5", "--format", "json", "--tee=out.json"])
            .Should().Equal("list", "package", "--format", "json");
    }

    [Fact]
    public void IsPassthrough_ReturnsFalse_ForAnInteractiveBuild()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "build", "--interactive"]).Should().BeFalse();
    }

    [Fact]
    public void IsPassthrough_ReturnsFalse_WhenFewerThanTwoArgs()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet"]).Should().BeFalse();
        ArgumentPreprocessor.IsPassthrough([]).Should().BeFalse();
    }

    [Fact]
    public void IsPassthrough_ReturnsFalse_WhenFirstArgIsNotDotnet()
    {
        ArgumentPreprocessor.IsPassthrough(["other", "run"]).Should().BeFalse();
    }

    // ── InsertSeparator ──────────────────────────────────────────────────────

    [Fact]
    public void InsertSeparator_InsertsDoubleDash_BeforeDotnetArgs()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "build", "MyProject.slnx"]);

        result.Should().Equal("dotnet", "build", "--", "MyProject.slnx");
    }

    [Fact]
    public void InsertSeparator_PartitionsDtkFlagsBeforeDoubleDash()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "test", "-v", "--filter", "Category=Unit"]);

        result.Should().Equal("dotnet", "test", "-v", "--", "--filter", "Category=Unit");
    }

    [Fact]
    public void InsertSeparator_PartitionsMultipleDtkFlagsBeforeDoubleDash()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "build", "--show-log", "-v", "MyProject.slnx"]);

        result.Should().Equal("dotnet", "build", "--show-log", "-v", "--", "MyProject.slnx");
    }

    [Fact]
    public void InsertSeparator_ForwardsUserSeparatorVerbatim_WhenDoubleDashPresent()
    {
        // The user's own "--" must survive to dotnet, so dtk inserts its own separator
        // and leaves the user's untouched (Spectre consumes only the first "--").
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "build", "--", "MyProject.slnx"]);

        result.Should().Equal("dotnet", "build", "--", "--", "MyProject.slnx");
    }

    [Theory]
    [InlineData(
        "dotnet test --filter Category=Unit -- RunConfiguration.X=1",
        "dotnet test -- --filter Category=Unit -- RunConfiguration.X=1")]
    [InlineData(
        "dotnet test -- RunConfiguration.MaxCpuCount=4",
        "dotnet test -- -- RunConfiguration.MaxCpuCount=4")]
    [InlineData(
        "dotnet restore --bogusopt bogusvalue -- proj.csproj",
        "dotnet restore -- --bogusopt bogusvalue -- proj.csproj")]
    [InlineData(
        "dotnet test -v --filter Category=Unit -- RunConfiguration.X=1",
        "dotnet test -v -- --filter Category=Unit -- RunConfiguration.X=1")]
    public void InsertSeparator_UserSeparator_PartitionsDtkFlagsThenForwardsVerbatim(
        string input, string expected)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);

        // After Spectre consumes the first "--", dotnet receives everything past it verbatim,
        // including the user's own "--" — e.g. `dotnet test -- RunConfiguration.MaxCpuCount=4`.
        var result = ArgumentPreprocessor.InsertSeparator(input.Split(' '));

        result.Should().Equal(expected.Split(' '));
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenOnlyDtkFlagsPresent()
    {
        var args = new[]
        {
            "dotnet", "build", "-v"
        };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenExactlyTwoArgs()
    {
        var args = new[]
        {
            "dotnet", "build"
        };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenATwoTokenSubcommandUsesUpEveryArgument()
    {
        // "list package" is two tokens, so this is the two-token equivalent of `dtk dotnet build`:
        // there is nothing after the subcommand to separate, and inserting "--" at the end would
        // hand Spectre a trailing separator with no arguments behind it.
        var args = new[]
        {
            "dotnet", "list", "package"
        };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenUnknownSubcommand()
    {
        var args = new[]
        {
            "dotnet", "run", "--project", "MyProject.csproj"
        };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_IsCaseInsensitive_ForSubcommand()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["DOTNET", "BUILD", "MyProject.slnx"]);

        result.Should().Equal("DOTNET", "BUILD", "--", "MyProject.slnx");
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenOnlyQuietFlagPresent()
    {
        var args = new[]
        {
            "dotnet", "test", "-q"
        };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenOnlyQuietLongFlagPresent()
    {
        var args = new[]
        {
            "dotnet", "test", "--quiet"
        };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_PartitionsQuietFlagBeforeDoubleDash()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "test", "-q", "--filter", "Category=Unit"]);

        result.Should().Equal("dotnet", "test", "-q", "--", "--filter", "Category=Unit");
    }

    [Fact]
    public void InsertSeparator_PartitionsQuietLongFlagBeforeDoubleDash()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "test", "--quiet", "--filter", "Category=Unit"]);

        result.Should().Equal("dotnet", "test", "--quiet", "--", "--filter", "Category=Unit");
    }

    // ── Verbosity values ─────────────────────────────────────────────────────
    // `-v <level>` is dotnet's own verbosity option; dtk's bare `-v` stays its verbose flag.

    [Theory]
    [InlineData("q")]
    [InlineData("quiet")]
    [InlineData("m")]
    [InlineData("minimal")]
    [InlineData("n")]
    [InlineData("normal")]
    [InlineData("d")]
    [InlineData("detailed")]
    [InlineData("diag")]
    [InlineData("diagnostic")]
    [InlineData("Q")]
    public void InsertSeparator_VerbosityValueAfterV_StaysWithDotnet(string level)
    {
        var result = ArgumentPreprocessor.InsertSeparator(["dotnet", "test", "-v", level]);

        result.Should().Equal("dotnet", "test", "--", "-v", level);
    }

    [Fact]
    public void InsertSeparator_BareV_IsStillDtkVerbose()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "test", "-v", "--filter", "X"]);

        result.Should().Equal("dotnet", "test", "-v", "--", "--filter", "X");
    }

    [Fact]
    public void InsertSeparator_VerbosityValueAfterV_WithOtherDtkFlag_KeepsDtkFlagFirst()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "build", "--show-log", "-v", "q", "MyProject.slnx"]);

        result.Should().Equal("dotnet", "build", "--show-log", "--", "-v", "q", "MyProject.slnx");
    }

    [Fact]
    public void InsertSeparator_VerbosityValueAfterV_AfterUserSeparator_IsForwardedVerbatim()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "test", "--", "-v", "q"]);

        result.Should().Equal("dotnet", "test", "--", "--", "-v", "q");
    }

    [Theory]
    [InlineData("q")]
    [InlineData("QUIET")]
    [InlineData("Minimal")]
    [InlineData("diagnostic")]
    public void IsVerbosityValue_KnownLevel_IsTrueCaseInsensitively(string token)
    {
        ArgumentPreprocessor.IsVerbosityValue(token).Should().BeTrue();
    }

    [Theory]
    [InlineData("--filter")]
    [InlineData("MyProject.slnx")]
    [InlineData("v")]
    [InlineData("quietly")]
    [InlineData("--quiet")]
    public void IsVerbosityValue_OtherToken_IsFalse(string token)
    {
        ArgumentPreprocessor.IsVerbosityValue(token).Should().BeFalse();
    }

    // ── Raw output (DTK_RAW) ─────────────────────────────────────────────────

    [Fact]
    public void IsRawRequested_RecognisesOneAndRejectsZero()
    {
        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], "1").Should().BeTrue();
        ArgumentPreprocessor.IsRawRequested(["DOTNET", "test", "-v", "q"], "true").Should().BeTrue();
        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], "yes").Should().BeTrue();

        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], "0").Should().BeFalse();
        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], "false").Should().BeFalse();
        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], "FALSE").Should().BeFalse();
        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], "").Should().BeFalse();
        ArgumentPreprocessor.IsRawRequested(["dotnet", "build"], null).Should().BeFalse();
        ArgumentPreprocessor.IsRawRequested(["gain"], "1").Should().BeFalse("only a wrapped dotnet run has raw output");
        ArgumentPreprocessor.IsRawRequested([], "1").Should().BeFalse();
    }

    [Fact]
    public void StripDtkOptions_RemovesDtkFlagsButKeepsUserSeparatorTail()
    {
        var result = ArgumentPreprocessor.StripDtkOptions(
        [
            "test", "--vv", "--show-log", "-q", "--quiet", "--verbose", "-v", "--filter", "X",
            "-v", "q", "--help", "--", "-v", "--quiet"
        ]);

        // -v q is dotnet's verbosity and --help dotnet's own help; after the user's "--" nothing is dtk's.
        result.Should().Equal("test", "--filter", "X", "-v", "q", "--help", "--", "-v", "--quiet");
    }

    [Fact]
    public void StripDtkOptions_TrailingV_IsDtkVerbose()
    {
        ArgumentPreprocessor.StripDtkOptions(["build", "MyApp.slnx", "-v"]).Should().Equal("build", "MyApp.slnx");
    }

    [Theory]
    [InlineData("test --tee out.log --max-lines=3 --filter X", "test --filter X")]
    [InlineData("test --tee:out.log --max-lines 3 --filter X", "test --filter X")]
    [InlineData("test --max-lines -1 --filter X", "test --filter X")]
    [InlineData("test --names --show-output --fail-on-empty --filter X", "test --filter X")]
    [InlineData("test --names=true --show-output:false --fail-on-empty=true --filter X", "test --filter X")]
    // A value option missing its value goes alone: dotnet's next option is never taken for it.
    [InlineData("test --tee --filter X", "test --filter X")]
    // --max-lines takes only an integer: a project after it is dotnet's.
    [InlineData("build --max-lines MyApp.slnx", "build MyApp.slnx")]
    public void StripDtkOptions_DropsValueOptionAndValue(string input, string expected)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);

        ArgumentPreprocessor.StripDtkOptions(input.Split(' ')).Should().Equal(expected.Split(' '));
    }

    [Fact]
    public void StripDtkOptions_LeavesTokensAfterUserSeparator()
    {
        ArgumentPreprocessor.StripDtkOptions(["test", "--", "--tee", "x"]).Should().Equal("test", "--", "--tee", "x");
    }

    // ── Value options and test-only options ──────────────────────────────────
    // `--tee <path>` and `--max-lines <n>` take a value, which moves with them; the test-only flags are dtk's too.

    [Theory]
    [InlineData("dotnet test --tee out.log --filter X", "dotnet test --tee out.log -- --filter X")]
    [InlineData("dotnet test --tee=out.log --filter X", "dotnet test --tee=out.log -- --filter X")]
    [InlineData("dotnet test --tee:out.log --filter X", "dotnet test --tee:out.log -- --filter X")]
    [InlineData("dotnet test --max-lines 5 --filter X", "dotnet test --max-lines 5 -- --filter X")]
    [InlineData("dotnet test --filter X --max-lines=5", "dotnet test --max-lines=5 -- --filter X")]
    [InlineData("dotnet test --max-lines -1 --filter X", "dotnet test --max-lines -1 -- --filter X")]
    [InlineData("dotnet build -q --tee out.log MyApp.slnx", "dotnet build -q --tee out.log -- MyApp.slnx")]
    public void InsertSeparator_MovesValueOptionWithItsValue(string input, string expected)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);

        ArgumentPreprocessor.InsertSeparator(input.Split(' ')).Should().Equal(expected.Split(' '));
    }

    [Fact]
    public void InsertSeparator_ValueOptionWithoutValue_DoesNotTakeTheNextOption()
    {
        // `--filter` is dotnet's: it must never become the path (MissingOptionValue reports the missing value).
        ArgumentPreprocessor.InsertSeparator(["dotnet", "test", "--tee", "--filter", "X"])
            .Should().Equal("dotnet", "test", "--tee", "--", "--filter", "X");
    }

    [Theory]
    [InlineData("dotnet build --max-lines MyApp.slnx", "dotnet build --max-lines -- MyApp.slnx")]
    [InlineData("dotnet build --max-lines /getProperty:Version", "dotnet build --max-lines -- /getProperty:Version")]
    [InlineData("dotnet test --max-lines abc", "dotnet test --max-lines -- abc")]
    public void InsertSeparator_MaxLinesFollowedByNonInteger_LeavesTheTokenToDotnet(string input, string expected)
    {
        // --max-lines takes only an integer as its next token, so the token stays dotnet's and the number is missing.
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);
        var args = input.Split(' ');

        ArgumentPreprocessor.InsertSeparator(args).Should().Equal(expected.Split(' '));
        ArgumentPreprocessor.MissingOptionValue(args).Should().Be("--max-lines");
    }

    [Fact]
    public void InsertSeparator_ValueOptionAfterUserSeparator_IsForwardedVerbatim()
    {
        ArgumentPreprocessor.InsertSeparator(["dotnet", "test", "--filter", "X", "--", "--tee", "x"])
            .Should().Equal("dotnet", "test", "--", "--filter", "X", "--", "--tee", "x");
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenOnlyAValueOptionIsPresent()
    {
        string[] args = ["dotnet", "test", "--tee", "out.log"];

        ArgumentPreprocessor.InsertSeparator(args).Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_PartitionsTestOnlyOptions()
    {
        ArgumentPreprocessor.InsertSeparator(
                ["dotnet", "test", "--names", "--filter", "X", "--show-output", "--fail-on-empty"])
            .Should().Equal("dotnet", "test", "--names", "--show-output", "--fail-on-empty", "--", "--filter", "X");
    }

    [Fact]
    public void InsertSeparator_PartitionsJoinedTestOnlyOptions()
    {
        ArgumentPreprocessor.InsertSeparator(["dotnet", "test", "--names=true", "--filter", "X", "--fail-on-empty:true"])
            .Should().Equal("dotnet", "test", "--names=true", "--fail-on-empty:true", "--", "--filter", "X");
    }

    [Theory]
    [InlineData("dotnet test --tee", "--tee")]
    [InlineData("dotnet test --tee --filter X", "--tee")]
    [InlineData("dotnet test --max-lines", "--max-lines")]
    [InlineData("dotnet test --max-lines --tee out.log", "--max-lines")]
    [InlineData("dotnet test --tee -- out.log", "--tee")]
    [InlineData("dotnet build --max-lines MyApp.slnx", "--max-lines")]
    [InlineData("dotnet build --max-lines /getProperty:Version", "--max-lines")]
    [InlineData("dotnet test --max-lines abc", "--max-lines")]
    [InlineData("dotnet test --max-lines 5", null)]
    [InlineData("dotnet test --tee=a", null)]
    [InlineData("dotnet test --max-lines:5", null)]
    [InlineData("dotnet test --max-lines -1", null)]
    [InlineData("dotnet test --tee out.log --max-lines 5", null)]
    [InlineData("dotnet test -- --tee", null)]
    [InlineData("dotnet tool run x --tee", null)]
    public void MissingOptionValue_Detects(string input, string? expected)
    {
        ArgumentNullException.ThrowIfNull(input);

        ArgumentPreprocessor.MissingOptionValue(input.Split(' ')).Should().Be(expected);
    }

    [Theory]
    [InlineData("dotnet build --names", "--names")]
    [InlineData("dotnet build MyApp.slnx --show-output", "--show-output")]
    [InlineData("dotnet list package --fail-on-empty", "--fail-on-empty")]
    [InlineData("dotnet build --names=true", "--names")]
    [InlineData("dotnet test --names=true", null)]
    [InlineData("dotnet test --names", null)]
    [InlineData("dotnet test --names --show-output --fail-on-empty", null)]
    [InlineData("dotnet build -- --names", null)]
    [InlineData("dotnet tool run x --names", null)]
    public void MisplacedTestOption_Detects(string input, string? expected)
    {
        ArgumentNullException.ThrowIfNull(input);

        ArgumentPreprocessor.MisplacedTestOption(input.Split(' ')).Should().Be(expected);
    }

    [Theory]
    [InlineData("test --fail-on-empty", "--fail-on-empty")]
    [InlineData("test --tee=x", "--tee")]
    [InlineData("test --tee:x", "--tee")]
    [InlineData("publish --interactive --tee x", "--tee")]
    [InlineData("test --max-lines 5 --names --show-output", null)]
    [InlineData("test -- --tee x", null)]
    [InlineData("tool run x --tee y", null)]
    public void OptionRefusedOnPassthrough_Detects(string input, string? expected)
    {
        ArgumentNullException.ThrowIfNull(input);

        ArgumentPreprocessor.OptionRefusedOnPassthrough(input.Split(' ')).Should().Be(expected);
    }

    [Theory]
    [InlineData("test --max-lines 5 --names -q --show-output", "--max-lines --names --show-output")]
    [InlineData("build -getProperty:X --max-lines=5 --max-lines 3", "--max-lines")]
    [InlineData("test -q --vv --show-log --fail-on-empty", "")]
    [InlineData("test -- --max-lines 5", "")]
    [InlineData("tool run x --max-lines 5", "")]
    public void OptionsDroppedOnPassthrough_ListsEachDisplayOptionOnce(string input, string expected)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);

        ArgumentPreprocessor.OptionsDroppedOnPassthrough(input.Split(' '))
            .Should().Equal(expected.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("dotnet test --tee", null, true, "dtk: --tee needs a value")]
    [InlineData("dotnet test --max-lines", null, true, "dtk: --max-lines needs a number")]
    [InlineData("dotnet build --max-lines MyApp.slnx", "1", true, "dtk: --max-lines needs a number")]
    [InlineData("dotnet build --max-lines /getProperty:Version", null, true, "dtk: --max-lines needs a number")]
    [InlineData("dotnet build --names", null, true, "dtk: --names applies only to dotnet test")]
    [InlineData("dotnet test --fail-on-empty", "1", true, "dtk: --fail-on-empty needs a filtered run, but DTK_RAW=1 is set")]
    [InlineData("dotnet publish --interactive --tee x", null, true,
        "dtk: --tee needs a filtered run, but --interactive is set")]
    [InlineData("dotnet build -getProperty:X --tee=x", null, true,
        "dtk: --tee needs a filtered run, but the output is machine-readable")]
    [InlineData("dotnet pack --interactive --max-lines 1", null, false, "dtk: --max-lines ignored: --interactive is set")]
    [InlineData("dotnet test --max-lines 5 --names", "1", false,
        "dtk: --max-lines ignored: DTK_RAW=1 is set|dtk: --names ignored: DTK_RAW=1 is set")]
    // A filtered run honours every option; an unfiltered subcommand's arguments are not dtk's.
    [InlineData("dotnet test --max-lines 5 --tee x --fail-on-empty", null, false, "")]
    [InlineData("dotnet test --max-lines 5 --tee x", "0", false, "")]
    [InlineData("dotnet tool run x --tee", "1", false, "")]
    [InlineData("gain --days 7", "1", false, "")]
    public void CheckOptions_ReportsUsageErrorsRefusalsAndDroppedOptions(
        string input, string? dtkRawValue, bool isUsageError, string messages)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(messages);

        var (actualMessages, actualIsUsageError) = ArgumentPreprocessor.CheckOptions(input.Split(' '), dtkRawValue);

        actualIsUsageError.Should().Be(isUsageError);
        actualMessages.Should().Equal(messages.Split('|', StringSplitOptions.RemoveEmptyEntries));
    }

    // ── Usage errors and passthrough refusals, end to end ────────────────────
    // A fake `dotnet` records whether dtk ran it: a usage error or a refusal must stop dtk before any child starts.

    [UnixTheory(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    [InlineData("--max-lines", "-1")]
    [InlineData("--max-lines=-1")]
    public async Task MaxLines_Negative_IsUsageError(params string[] option)
    {
        var (_, _, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, ["dotnet", "test", .. option]);

        exitCode.Should().NotBe(0);
        childArguments.Should().BeNull("a usage error never runs the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task MaxLines_NonNumeric_IsUsageError()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, "dotnet", "test", "--max-lines", "abc");

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be($"dtk: --max-lines needs a number{Environment.NewLine}");
        childArguments.Should().BeNull("a usage error never runs the child");
    }

    [UnixTheory(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    [InlineData("dtk: --tee needs a value", "--tee")]
    [InlineData("dtk: --tee needs a value", "--tee", "--filter", "X")]
    [InlineData("dtk: --max-lines needs a number", "--max-lines")]
    public async Task ValueOptionWithoutValue_IsUsageErrorNamingTheOption(string expected, params string[] options)
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, ["dotnet", "test", .. options]);

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be(expected + Environment.NewLine);
        childArguments.Should().BeNull("a usage error never runs the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task DtkRaw_MaxLinesBeforeAProject_IsUsageErrorWithoutRunningTheChild()
    {
        // Passed through, nothing would validate the value: taking the project for it would drop it from the run.
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            RawOutput, "dotnet", "build", "--max-lines", "MyApp.slnx");

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be($"dtk: --max-lines needs a number{Environment.NewLine}");
        childArguments.Should().BeNull("a usage error never runs the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task MaxLinesBeforeAnMsBuildQuery_IsUsageErrorWithoutRunningTheChild()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, "dotnet", "build", "--max-lines", "/getProperty:Version");

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be($"dtk: --max-lines needs a number{Environment.NewLine}");
        childArguments.Should().BeNull("a usage error never runs the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task TestOptionOnAnotherSubcommand_IsUsageError()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, "dotnet", "build", "--names");

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be($"dtk: --names applies only to dotnet test{Environment.NewLine}");
        childArguments.Should().BeNull("a usage error never runs the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task DtkRaw_FailOnEmpty_IsRefusedWithoutRunningTheChild()
    {
        // Run raw, the verdict is dotnet's, so honouring the flag is impossible; running anyway would be a false green.
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            RawOutput, "dotnet", "test", "--fail-on-empty");

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be(
            $"dtk: --fail-on-empty needs a filtered run, but DTK_RAW=1 is set{Environment.NewLine}");
        childArguments.Should().BeNull("a refused run never starts the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task InteractivePublish_Tee_IsRefusedWithoutRunningTheChild()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, "dotnet", "publish", "--interactive", "--tee", "out.log");

        exitCode.Should().Be(ExitCodes.UsageError);
        stdErr.Should().Be($"dtk: --tee needs a filtered run, but --interactive is set{Environment.NewLine}");
        childArguments.Should().BeNull("a refused run never starts the child");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task MachineReadableRun_MaxLines_IsDroppedWithAWarning()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, "dotnet", "build", "-getProperty:Version", "--max-lines", "5");

        exitCode.Should().Be(0);
        stdErr.Should().Be(
            $"dtk: --max-lines ignored: the output is machine-readable{Environment.NewLine}");
        childArguments.Should().Equal("build", "-getProperty:Version");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task DtkRaw_DisplayOptions_AreDroppedWithAWarningEach()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            RawOutput, "dotnet", "test", "--max-lines=5", "--filter", "X", "--show-output");

        exitCode.Should().Be(0);
        stdErr.Should().Be(
            $"dtk: --max-lines ignored: DTK_RAW=1 is set{Environment.NewLine}" +
            $"dtk: --show-output ignored: DTK_RAW=1 is set{Environment.NewLine}");
        childArguments.Should().Equal("test", "--filter", "X");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task UnfilteredSubcommand_KeepsOptionsSpelledLikeDtksVerbatim()
    {
        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            RawOutput, "dotnet", "tool", "run", "x", "--tee", "--fail-on-empty", "--names", "--max-lines");

        exitCode.Should().Be(0);
        stdErr.Should().BeEmpty();
        childArguments.Should().Equal("tool", "run", "x", "--tee", "--fail-on-empty", "--names", "--max-lines");
    }

    // ── --tee <path>, end to end ─────────────────────────────────────────────
    // Spectre binds every form the preprocessor moves ahead of the inserted `--` to OutputDisplaySettings.TeePath.

    [UnixTheory(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    [InlineData("--tee", " ")]
    [InlineData("--tee", "=")]
    [InlineData("--tee", ":")]
    public async Task Tee_EveryForm_WritesTheRawOutputAndNeverReachesTheChild(string option, string joiner)
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "raw.log");
        string[] tee = joiner == " " ? [option, path] : [option + joiner + path];
        var environment = new Dictionary<string, string>
        {
            [IntegrationTestHelper.FakeDotnetStdoutVariable] = "\e[33mwarning\e[0m CS0168\nBuild succeeded.\n"
        };

        var (_, stdErr, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            environment, ["dotnet", "build", .. tee, "--no-restore"]);

        exitCode.Should().Be(0, stdErr);
        childArguments.Should().Equal("build", "--no-restore");
        (await File.ReadAllTextAsync(path)).Should().Be("warning CS0168\nBuild succeeded.\n");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Tee_UncreatablePath_IsUsageErrorWithoutRunningTheChild()
    {
        // As with shell redirection, a missing directory is not created: the run stops instead of starting
        // without the evidence it was asked to keep.
        var path = Path.Combine(IntegrationTestHelper.NewIsolatedDir(), "missing", "raw.log");

        var (stdOut, _, exitCode, childArguments) = await IntegrationTestHelper.RunDtkWithFakeDotnetAsync(
            NoEnvironment, "dotnet", "test", "--tee", path);

        exitCode.Should().Be(ExitCodes.UsageError);
        stdOut.Should().StartWith($"✗ dtk: --tee cannot create {path}: ").And.EndWith(Environment.NewLine);
        stdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle("it is the run's only line");
        childArguments.Should().BeNull("a refused run never starts the child");
        File.Exists(path).Should().BeFalse();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task PipeTee_WritesThePipedOutput()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "raw.log");

        var (_, stdErr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsInDirAsync(
            dir, "Build succeeded.", "pipe", "build", $"--tee={path}");

        exitCode.Should().Be(0, stdErr);
        (await File.ReadAllTextAsync(path)).Should().Be("Build succeeded.\n");
    }

    // ── Help routing ─────────────────────────────────────────────────────────
    // --help/-h must reach Spectre (dtk's own help), never be forwarded to dotnet.

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void InsertSeparator_HelpFlag_IsKeptBeforeSeparator_NotForwarded(string helpFlag)
    {
        ArgumentNullException.ThrowIfNull(helpFlag);

        var result = ArgumentPreprocessor.InsertSeparator(["dotnet", "build", helpFlag]);

        // No "--" inserted: Spectre sees --help/-h and renders help instead of a filtered build.
        result.Should().Equal("dotnet", "build", helpFlag);
    }

    [Fact]
    public void InsertSeparator_HelpFlag_PartitionedBeforeSeparator_WithTrailingArgs()
    {
        var result = ArgumentPreprocessor.InsertSeparator(["dotnet", "test", "--help", "--filter", "X"]);

        result.Should().Equal("dotnet", "test", "--help", "--", "--filter", "X");
    }

    // ── Casing normalization ─────────────────────────────────────────────────

    [Theory]
    [InlineData("DOTNET BUILD MyProject.slnx", "dotnet build MyProject.slnx")]
    [InlineData("Dotnet Test", "dotnet test")]
    [InlineData("dotnet BUILD x", "dotnet build x")]
    public void Normalize_CanonicalizesKnownInvocationCasing(string input, string expected)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);

        var result = ArgumentPreprocessor.Normalize(input.Split(' '));

        result.Should().Equal(expected.Split(' '));
    }

    [Theory]
    [InlineData("DOTNET RUN")]  // unknown subcommand -> passthrough, forwarded verbatim
    [InlineData("other build")] // not a dotnet invocation
    [InlineData("dotnet")]      // too short to be an invocation
    // dtk's own verbs and other commands never get the `dotnet` prefix that `dtk test` does.
    [InlineData("log build")]
    [InlineData("log")]
    [InlineData("reset")]
    [InlineData("gain")]
    [InlineData("gain --days 7")]
    [InlineData("config show")]
    [InlineData("doctor")]
    [InlineData("init claude")]
    [InlineData("integrate claude")]
    [InlineData("pipe build")]
    [InlineData("completion bash")]
    [InlineData("cli version")]
    [InlineData("hook claude")]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("-v test")]
    [InlineData("list reference")]
    [InlineData("list")]
    [InlineData("run")]
    [InlineData("tests")]
    public void Normalize_LeavesNonCanonicalizableInvocationsUnchanged(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var args = input.Split(' ');

        ArgumentPreprocessor.Normalize(args).Should().Equal(args);
    }

    // ── Shortcut: `dtk test` for `dtk dotnet test` ───────────────────────────

    [Theory]
    [InlineData("test --filter X", "dotnet test --filter X")]
    [InlineData("build", "dotnet build")]
    [InlineData("build MyApp.slnx --no-restore", "dotnet build MyApp.slnx --no-restore")]
    [InlineData("restore", "dotnet restore")]
    [InlineData("clean -q", "dotnet clean -q")]
    [InlineData("format --verify-no-changes", "dotnet format --verify-no-changes")]
    [InlineData("publish -c Release", "dotnet publish -c Release")]
    [InlineData("pack -o out", "dotnet pack -o out")]
    [InlineData("list package", "dotnet list package")]
    [InlineData("list package --outdated", "dotnet list package --outdated")]
    [InlineData("TEST --filter X", "dotnet test --filter X")]
    [InlineData("List Package", "dotnet list package")]
    public void Normalize_FilteredSubcommandWithoutDotnet_GetsTheDotnetPrefix(string input, string expected)
    {
        // An agent types `dtk test`; without the prefix Spectre answers "Unknown command 'test'. Did you mean
        // 'reset'?", which suggests the command that wipes the tracking data.
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expected);

        ArgumentPreprocessor.Normalize(input.Split(' ')).Should().Equal(expected.Split(' '));
    }

    [Fact]
    public void Normalize_FilteredSubcommandWithoutDotnet_DoesNotModifyTheCallersArray()
    {
        string[] args = ["test", "--filter", "X"];

        _ = ArgumentPreprocessor.Normalize(args);

        args.Should().Equal("test", "--filter", "X");
    }

    [Fact]
    public void Normalize_ShortcutThenTheRestOfThePipeline_SeesAnOrdinaryDotnetInvocation()
    {
        var normalized = ArgumentPreprocessor.Normalize(["test", "--filter", "X", "-q"]);

        ArgumentPreprocessor.IsPassthrough(normalized).Should().BeFalse();
        ArgumentPreprocessor.InsertSeparator(normalized)
            .Should().Equal("dotnet", "test", "-q", "--", "--filter", "X");
    }

    [Fact]
    public void Normalize_ShortcutWithMachineReadableOutput_IsPassedThrough()
    {
        var normalized = ArgumentPreprocessor.Normalize(["list", "package", "--format", "json"]);

        ArgumentPreprocessor.IsPassthrough(normalized).Should().BeTrue();
    }

    [Fact]
    public void Normalize_ShortcutUnderRawOutput_IsRequestedRaw()
    {
        var normalized = ArgumentPreprocessor.Normalize(["build"]);

        ArgumentPreprocessor.IsRawRequested(normalized, "1").Should().BeTrue();
    }

    // ── Verbosity flag parsing ───────────────────────────────────────────────
    // Regression: `bool[] Verbose` made Spectre demand a value for `-v`
    // ("Option 'verbose' is defined but no value has been provided").

    [Fact]
    public void DotnetCommandSettings_NoVerbosityFlag_ParsesAsLevel0()
    {
        var exitCode = RunCapturingVerbosity("build", "MyProject.slnx");

        exitCode.Should().Be(0);
        VerbosityCaptureCommand.LastLevel.Should().Be(0);
    }

    [Fact]
    public void DotnetCommandSettings_SingleV_ParsesAsLevel1()
    {
        var exitCode = RunCapturingVerbosity("build", "-v", "MyProject.slnx");

        exitCode.Should().Be(0);
        VerbosityCaptureCommand.LastLevel.Should().Be(1);
    }

    [Fact]
    public void DotnetCommandSettings_DoubleV_ParsesAsLevel2()
    {
        var exitCode = RunCapturingVerbosity("build", "--vv", "MyProject.slnx");

        exitCode.Should().Be(0);
        VerbosityCaptureCommand.LastLevel.Should().Be(2);
    }

    private static int RunCapturingVerbosity(params string[] args)
    {
        VerbosityCaptureCommand.LastLevel = -1;
        var app = new CommandApp();
        app.Configure(config => config.AddCommand<VerbosityCaptureCommand>("build"));
        return app.Run(args);
    }

    [SuppressMessage("Performance", "CA1812",
        Justification = "Instantiated by Spectre.Console.Cli via reflection.")]
    private sealed class VerbosityCaptureCommand : Command<DotnetCommandSettings>
    {
        public static int LastLevel { get; set; } = -1;

        public override int Execute(
            CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
        {
            LastLevel = settings.VerbosityLevel;
            return 0;
        }
    }

    // ── Token-sequence matching regression guards ───────────────────────────
    // These pin today's single-token behaviour so a later switch to TryMatch-based
    // routing (which must support multi-token subcommands like `list package`)
    // provably changes nothing for these cases.

    [Fact]
    public void IsPassthrough_ListReference_IsPassthrough()
    {
        // `list package` will be filtered but `list reference` must never be: it is the case that
        // makes first-token matching wrong. The guarantee is dispatch order — IsPassthrough runs
        // before Spectre, so this never reaches the `dotnet list` branch.
        ArgumentPreprocessor.IsPassthrough(["dotnet", "list", "reference"]).Should().BeTrue();
    }

    [Fact]
    public void IsPassthrough_BareList_IsPassthrough()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "list"]).Should().BeTrue();
    }

    [Fact]
    public void IsPassthrough_KnownSingleTokenSubcommand_IsNotPassthrough()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "build"]).Should().BeFalse();
    }

    [Fact]
    public void InsertSeparator_SingleTokenSubcommand_SeparatesAfterOneToken()
    {
        ArgumentPreprocessor.InsertSeparator(["dotnet", "build", "MyApp.slnx"])
            .Should().Equal("dotnet", "build", "--", "MyApp.slnx");
    }

    [Fact]
    public void Normalize_UppercaseSubcommand_IsCanonicalized()
    {
        ArgumentPreprocessor.Normalize(["DOTNET", "BUILD"]).Should().Equal("dotnet", "build");
    }

    [Fact]
    public void IsPassthrough_ListPackage_IsNotPassthrough()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet", "list", "package"]).Should().BeFalse();
    }

    [Fact]
    public void InsertSeparator_ListPackage_SeparatesAfterBothTokens()
    {
        ArgumentPreprocessor.InsertSeparator(["dotnet", "list", "package", "--outdated"])
            .Should().Equal("dotnet", "list", "package", "--", "--outdated");
    }

    [Fact]
    public void Normalize_UppercaseListPackage_CanonicalizesBothTokens()
    {
        ArgumentPreprocessor.Normalize(["DOTNET", "LIST", "PACKAGE"])
            .Should().Equal("dotnet", "list", "package");
    }
}
