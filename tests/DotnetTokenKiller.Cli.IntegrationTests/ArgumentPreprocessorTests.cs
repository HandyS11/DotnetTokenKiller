using System.Diagnostics.CodeAnalysis;
using DotnetTokenKiller.Cli.Commands.Settings;
using FluentAssertions;
using Spectre.Console.Cli;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class ArgumentPreprocessorTests
{
    // ── IsPassthrough ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dotnet", "run")]
    [InlineData("dotnet", "publish")]
    [InlineData("dotnet", "pack")]
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
    [InlineData("DOTNET", "BUILD")]
    public void IsPassthrough_ReturnsFalse_ForKnownSubcommand(string exe, string sub)
    {
        ArgumentPreprocessor.IsPassthrough([exe, sub]).Should().BeFalse();
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
            "dotnet", "publish", "MyProject.csproj"
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
    public void Normalize_LeavesNonCanonicalizableInvocationsUnchanged(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var args = input.Split(' ');

        ArgumentPreprocessor.Normalize(args).Should().Equal(args);
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

        protected override int Execute(
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
