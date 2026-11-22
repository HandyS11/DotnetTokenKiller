using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class SharedInstructionArtifactsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-shared-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task Section_IsExactlyWhatGeminiWrites()
    {
        // Antigravity CLI reads the ~/.gemini/GEMINI.md that `dtk init gemini --global` writes; one constant keeps
        // the two providers from rewriting each other's section.
        await new GeminiCliIntegrator(new HomePaths(Path.Combine(_tempDir, "home"))).IntegrateAsync(_tempDir, false, default);

        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "GEMINI.md"))).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public void SkillMarkdown_SatisfiesEveryHarnessSkillLoader()
    {
        var frontmatter = SharedInstructionArtifacts.SkillMarkdown.Split("---")[1];

        // OpenCode requires a lowercase-hyphenated name equal to the folder name, at most 64 characters for Codex.
        // Codex, OpenCode and Antigravity all require a description.
        var folder = Path.GetFileName(Path.GetDirectoryName(SharedInstructionArtifacts.SkillPath("root")));
        folder.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
        folder.Length.Should().BeLessThanOrEqualTo(64);
        frontmatter.Should().Contain($"name: {folder}\n");
        frontmatter.Should().Contain("description: '");
    }

    /// <summary>What an agent needs from the shared instructions beyond the commands themselves.</summary>
    public static TheoryData<string> TaughtPoints() => new(
        "`dtk log build`",
        "`dtk pipe <subcommand>`",
        "`-q`",
        "`--tee <file>`",
        "`--fail-on-empty`",
        "DTK_RAW=1",
        "`\\dotnet …`",
        "`command dotnet …`",
        "`rtk proxy dotnet …`",
        "`dtk test`, `dtk build`",
        "Don't pipe it into `tail` or `grep`",
        "instead of running it again");

    /// <summary>What the skill, the reference for dtk's options beyond plain use, keeps.</summary>
    public static TheoryData<string> ReferencePoints() => new(
        "`--tee <file>`",
        "`--show-output`",
        "`--fail-on-empty`",
        "`--names`",
        "`--max-lines N`",
        "`dtk log [build|test]`",
        "`--list`",
        "`dtk pipe <subcommand>`",
        "`-q`",
        "DTK_RAW=1",
        "`\\dotnet …`",
        "`command dotnet …`",
        "`rtk proxy dotnet …`",
        "`dtk test`, `dtk build`",
        "dtk gain");

    [Theory]
    [MemberData(nameof(ReferencePoints))]
    public void Skill_Body_KeepsTheReference(string point) =>
        SharedInstructionArtifacts.SkillMarkdown.Should().Contain(point);

    [Fact]
    public void Skill_Body_LeavesTheRulesToTheContext()
    {
        // Every agent gets the rules at its start (Claude Code's context hooks) or in its instructions file; the skill,
        // loaded on demand, does not repeat them, nor tell an agent that already runs dtk how to install it.
        foreach (var rule in IntegrationInstructions.AgentRuleBullets.Split('\n'))
        {
            SharedInstructionArtifacts.SkillMarkdown.Should().NotContain(rule);
        }

        SharedInstructionArtifacts.SkillMarkdown.Should().NotContain("## Installation");
    }

    [Fact]
    public void Skill_Body_ExplainsTheChangedFilesNarrowing()
    {
        SharedInstructionArtifacts.SkillMarkdown.Should()
            .Contain("in unchanged files not shown")
            .And.Contain("dtk log build --full | dtk pipe build")
            .And.Contain("`origin/HEAD`")
            .And.Contain("`.editorconfig`");
    }

    [Theory]
    [MemberData(nameof(TaughtPoints))]
    public void Section_TeachesTheLogTheBypassAndNotToPipe(string point) =>
        SharedInstructionArtifacts.Section.Should().Contain(point);

    [Fact]
    public void Section_StaysShortAndMakesNoInteractiveClaim()
    {
        // Loaded into every agent session, often next to the skill: every added sentence is paid for each time. The
        // bound was 1200 for 0.10's eight subcommands (1120 bytes); 0.11's five more add 183 bytes of names and examples,
        // and the options line (one line for five options, 1431 bytes in all) cannot be cut further without losing an option.
        // The agent rules every Claude Code agent also gets at session start lead the section since 0.13: they replace
        // the two bullets on tail/grep and dtk log and take 1574 bytes in all, which is the point of the section. The
        // bound is the size plus a little room.
        System.Text.Encoding.UTF8.GetByteCount(SharedInstructionArtifacts.Section).Should().BeLessThan(1600);

        // Only publish, pack and tool restore pass --interactive through; build and restore still filter it.
        SharedInstructionArtifacts.Section.Should().NotContain("--interactive");
        SharedInstructionArtifacts.SkillMarkdown.Should().NotContain("--interactive");
    }

    [Fact]
    public void SkillMarkdown_FlagsTable_DemotesVvInFavourOfTheLog()
    {
        var table = SharedInstructionArtifacts.SkillMarkdown.Split('\n').Where(line => line.StartsWith('|')).ToList();

        table.Should().NotBeEmpty();
        table.Should().NotContain(line => line.Contains("--vv", StringComparison.Ordinal));
        SharedInstructionArtifacts.SkillMarkdown.Should().Contain("prefer `dtk log`");
    }

    [Fact]
    public void SkillMarkdown_DoesNotAdviseCleaningFirstOrClaimWarningsAlwaysShow()
    {
        SharedInstructionArtifacts.SkillMarkdown.Should()
            .NotContain("`dtk dotnet clean` first")
            .And.NotContain("full warning report")
            .And.NotContain("frequency counts")
            .And.Contain("Errors and warnings are grouped by file")
            .And.Contain("build warnings of the projects compiled in that run")
            .And.Contain("up-to-date build compiles nothing and reports none");
    }

    [Fact]
    public async Task WriteAgentsFilesAsync_FreshThenRepeated_CreatesBothThenReportsBothUnchanged()
    {
        var agents = Path.Combine(_tempDir, "AGENTS.md");
        var skills = Path.Combine(_tempDir, ".agents", "skills");

        var first = new IntegrationContext(false);
        await SharedInstructionArtifacts.WriteAgentsFilesAsync(agents, skills, first, default);
        var second = new IntegrationContext(false);
        await SharedInstructionArtifacts.WriteAgentsFilesAsync(agents, skills, second, default);

        first.Created.Should().Equal(agents, SharedInstructionArtifacts.SkillPath(skills));
        second.Unchanged.Should().Equal(agents, SharedInstructionArtifacts.SkillPath(skills));
        (await File.ReadAllTextAsync(agents)).Should().Be(SharedInstructionArtifacts.Section);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(SharedInstructionArtifacts.SkillPath(skills))).Should().BeTrue();
    }
}
