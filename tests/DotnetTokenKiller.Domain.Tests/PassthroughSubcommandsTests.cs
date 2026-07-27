using DotnetTokenKiller.Domain;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class PassthroughSubcommandsTests
{
    [Theory]
    [InlineData(new[] { "list", "package" }, "list package")]
    [InlineData(new[] { "list", "package", "--outdated" }, "list package")]
    [InlineData(new[] { "list", "reference" }, "list reference")]
    [InlineData(new[] { "ef", "migrations", "add", "Init" }, "ef migrations")]
    [InlineData(new[] { "ef", "database", "update" }, "ef database")]
    [InlineData(new[] { "tool", "restore" }, "tool restore")]
    [InlineData(new[] { "sln", "list" }, "sln list")]
    [InlineData(new[] { "workload", "list" }, "workload list")]
    [InlineData(new[] { "nuget", "locals", "all", "--clear" }, "nuget locals")]
    public void CommandName_QualifiesWithTheSecondVerb_WhenItIsRecognised(string[] args, string expected)
    {
        PassthroughSubcommands.CommandName(args).Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "publish" }, "publish")]
    [InlineData(new[] { "publish", "-c", "Release" }, "publish")]
    [InlineData(new[] { "pack" }, "pack")]
    [InlineData(new[] { "msbuild", "/t:Rebuild" }, "msbuild")]
    public void CommandName_UsesTheSubcommandAlone_WhenItTakesNoQualifyingVerb(string[] args, string expected)
    {
        PassthroughSubcommands.CommandName(args).Should().Be(expected);
    }

    [Theory]
    [InlineData((object)new[] { "list", "./src/App.csproj" })]
    [InlineData((object)new[] { "list", "/home/someone/secret/Thing.csproj" })]
    [InlineData((object)new[] { "ef", "--connection", "Server=db;Password=hunter2" })]
    [InlineData((object)new[] { "tool", "MyCompany.Internal.Tool" })]
    public void CommandName_DropsAnUnrecognisedSecondToken(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // The leak guarantee: an argv fragment must never become a database value.
        var name = PassthroughSubcommands.CommandName(args);

        name.Should().Be(args[0]);
        name.Should().NotContain(args[1]);
    }

    [Theory]
    [InlineData((object)new[] { "./bin/Release/App.dll" })]
    [InlineData((object)new[] { "/opt/secret/Payload.dll" })]
    [InlineData((object)new[] { "totally-made-up-verb" })]
    public void CommandName_ReturnsUnknown_WhenTheFirstTokenIsNotARecognisedVerb(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // `dotnet ./bin/App.dll` runs an assembly — token 1 is a path, not a subcommand.
        var name = PassthroughSubcommands.CommandName(args);

        name.Should().Be(PassthroughSubcommands.Unknown);
        name.Should().NotContain(args[0]);
    }

    [Theory]
    [InlineData(new[] { "--info" }, "--info")]
    [InlineData(new[] { "--version" }, "--version")]
    [InlineData(new[] { "--list-sdks" }, "--list-sdks")]
    public void CommandName_KeepsRecognisedTopLevelOptions(string[] args, string expected)
    {
        PassthroughSubcommands.CommandName(args).Should().Be(expected);
    }

    [Fact]
    public void CommandName_IsCaseInsensitive()
    {
        PassthroughSubcommands.CommandName(["LIST", "PACKAGE"]).Should().Be("list package");
    }

    [Fact]
    public void CommandName_ReturnsUnknown_ForAnEmptyArgumentList()
    {
        PassthroughSubcommands.CommandName([]).Should().Be(PassthroughSubcommands.Unknown);
    }

    [Theory]
    [InlineData((object)new[] { "publish" })]
    [InlineData((object)new[] { "pack" })]
    [InlineData((object)new[] { "list", "package" })]
    [InlineData((object)new[] { "ef", "migrations" })]
    [InlineData((object)new[] { "msbuild" })]
    public void IsMeasurable_IsTrue_ForBatchSubcommands(string[] args)
    {
        PassthroughSubcommands.IsMeasurable(args).Should().BeTrue();
    }

    [Theory]
    [InlineData((object)new[] { "run" })]
    [InlineData((object)new[] { "watch" })]
    [InlineData((object)new[] { "new", "console" })]
    [InlineData((object)new[] { "--info" })]
    [InlineData((object)new[] { "./bin/App.dll" })]
    [InlineData((object)new string[0])]
    public void IsMeasurable_IsFalse_ForInteractiveOrUnrecognisedInvocations(string[] args)
    {
        // Capturing these would change behaviour the user depends on, or measure nothing useful.
        PassthroughSubcommands.IsMeasurable(args).Should().BeFalse();
    }

    [Theory]
    [InlineData((object)new[] { "publish", "--interactive" })]
    [InlineData((object)new[] { "pack", "--interactive" })]
    [InlineData((object)new[] { "publish", "-c", "Release", "--interactive" })]
    [InlineData((object)new[] { "publish", "--INTERACTIVE" })]
    public void IsMeasurable_IsFalse_WhenInteractiveFlagIsPresent(string[] args)
    {
        // RunStreamedAsync closes the child's stdin. A command that may prompt for private-feed
        // credentials over --interactive must take the inherited-stdio passthrough path instead of
        // hanging/failing on EOF.
        PassthroughSubcommands.IsMeasurable(args).Should().BeFalse();
    }

    [Theory]
    [InlineData((object)new[] { "publish" })]
    [InlineData((object)new[] { "pack" })]
    public void IsMeasurable_IsTrue_WhenInteractiveFlagIsAbsent(string[] args)
    {
        // Guards against a fix for --interactive accidentally disabling measurement generally.
        PassthroughSubcommands.IsMeasurable(args).Should().BeTrue();
    }

    [Theory]
    [InlineData((object)new[] { "publish", "--interactive" })]
    [InlineData((object)new[] { "pack", "--interactive" })]
    public void CommandName_IsUnaffectedByTheInteractiveFlag(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // The command still records under its real name; only measurability changes.
        PassthroughSubcommands.CommandName(args).Should().Be(args[0]);
    }

    [Fact]
    public void EveryMeasurableSubcommand_IsAlsoARecognisedVerb()
    {
        // Otherwise a subcommand could be captured and then recorded as "(other)", making the
        // measurement unattributable.
        foreach (var sub in PassthroughSubcommands.Measurable)
        {
            PassthroughSubcommands.CommandName([sub]).Should().Be(sub);
        }
    }
}
