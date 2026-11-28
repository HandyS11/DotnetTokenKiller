using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class RtkRewriteRaceTests
{
    private const string Command = "dotnet build; git status";
    private const string DtkRewrite = "dtk dotnet build; git status";
    private const string Merged = "dtk dotnet build; rtk git status";

    private static readonly Regex UnwrappedBuild = new("(?<!dtk )dotnet build");

    private static string Dtk(string command) => UnwrappedBuild.Replace(command, "dtk dotnet build");

    [Fact]
    public void MergedCommand_BothRewrite_ReturnsRtksRewriteOfDtks()
    {
        var rtk = new FakeRtkRewriter(FakeRtkRewriter.PrefixGit);

        var merged = RtkRewriteRace.MergedCommand(Command, DtkRewrite, Dtk, rtk);

        merged.Should().Be(new RtkRewriteRace.Merge(Merged, RtkRewritesDotnet: false));
        rtk.Calls.Should().HaveCount(3);
    }

    [Fact]
    public void MergedCommand_DtkLeavesTheCommandAlone_AsksRtkNothing()
    {
        var rtk = new FakeRtkRewriter(FakeRtkRewriter.PrefixGit);

        RtkRewriteRace.MergedCommand(Command, Command, Dtk, rtk).Should().BeNull();

        rtk.Calls.Should().BeEmpty();
    }

    public static TheoryData<int?, string> NonRewrites => new()
    {
        { 1, "" },
        { 2, "" },
        { 0, Command },
        { 3, "" },
        { 7, "x" },
        { null, "" }
    };

    [Theory]
    [MemberData(nameof(NonRewrites))]
    public void MergedCommand_RtkWouldNotRewrite_ReturnsNull(int? exitCode, string output)
    {
        var rtk = new FakeRtkRewriter(_ => exitCode is null ? null : new RtkRewrite(exitCode.Value, output));

        RtkRewriteRace.MergedCommand(Command, DtkRewrite, Dtk, rtk).Should().BeNull();
    }

    [Fact]
    public void MergedCommand_RtkRewritesOnlyTheDotnetSegment_ReturnsDtksRewrite()
    {
        var rtk = new FakeRtkRewriter(c => c switch
        {
            "dotnet build" => new RtkRewrite(0, "rtk dotnet build"),
            _ => new RtkRewrite(1, "")
        });

        RtkRewriteRace.MergedCommand("dotnet build", "dtk dotnet build", Dtk, rtk)
            .Should().Be(new RtkRewriteRace.Merge("dtk dotnet build", RtkRewritesDotnet: true));
    }

    [Fact]
    public void MergedCommand_RtkFailsOnTheSecondCall_ReturnsNull()
    {
        var rtk = new FakeRtkRewriter(c => c == Command ? new RtkRewrite(0, "rtk git status") : null);

        RtkRewriteRace.MergedCommand(Command, DtkRewrite, Dtk, rtk).Should().BeNull();
    }

    [Fact]
    public void MergedCommand_MergeNotStableUnderRtk_ReturnsNull()
    {
        var rtk = new FakeRtkRewriter(c => c == Merged ? new RtkRewrite(0, "rtk rtk git status") : FakeRtkRewriter.PrefixGit(c));

        RtkRewriteRace.MergedCommand(Command, DtkRewrite, Dtk, rtk).Should().BeNull();
    }

    [Fact]
    public void MergedCommand_MergeNotStableUnderDtk_ReturnsNull()
    {
        var rtk = new FakeRtkRewriter(FakeRtkRewriter.PrefixGit);

        RtkRewriteRace.MergedCommand(Command, DtkRewrite, c => c + " --x", rtk).Should().BeNull();
    }

    [Fact]
    public void DenyReply_IsClaudesDenyWithTheReasonAndTheMergedCommand()
    {
        var output = JsonNode.Parse(RtkRewriteRace.DenyReply(new RtkRewriteRace.Merge(Merged, RtkRewritesDotnet: false)))!["hookSpecificOutput"]!.AsObject();

        output["hookEventName"]!.GetValue<string>().Should().Be("PreToolUse");
        output["permissionDecision"]!.GetValue<string>().Should().Be("deny");
        output["permissionDecisionReason"]!.GetValue<string>().Should().Be(RtkRewriteRace.ReasonLead + Merged);
        output.Count.Should().Be(3);
    }

    [Fact]
    public void DenyReply_RtkRewritesDotnet_ExplainsTheRemedyAfterTheCommand()
    {
        var output = JsonNode.Parse(RtkRewriteRace.DenyReply(new RtkRewriteRace.Merge("dtk dotnet build", RtkRewritesDotnet: true)))!
            ["hookSpecificOutput"]!.AsObject();

        output["permissionDecisionReason"]!.GetValue<string>().Should().Be(
            RtkRewriteRace.ReasonLead + "dtk dotnet build\n(rtk also rewrites dotnet commands: run 'dtk init claude' again so it leaves them to dtk.)");
    }
}
