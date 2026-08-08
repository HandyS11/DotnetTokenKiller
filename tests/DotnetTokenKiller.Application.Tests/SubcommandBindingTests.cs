using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotnetTokenKiller.Application.Tests;

/// <summary>
/// Binds the Application layer's restatements of the supported subcommands back to
/// <see cref="DotnetSubcommands"/>, and locks this repo's own committed hook to the generated
/// template. These are the seams generation cannot close: a registration that is simply absent,
/// or a committed file that drifts from what would now be generated, produces no compile error
/// and no runtime error until a user hits it.
/// </summary>
public sealed class SubcommandBindingTests
{
    [Fact]
    public void EverySubcommand_ResolvesAKeyedOutputFilter()
    {
        var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        foreach (var subcommand in DotnetSubcommands.Ordered)
        {
            var filter = provider.GetKeyedService<IOutputFilter>(subcommand);

            filter.Should().NotBeNull(
                "'{0}' is a canonical subcommand, so AddApplication must register a filter keyed to it",
                subcommand);
        }
    }

    [Fact]
    public void NoFilter_IsRegisteredUnderANonCanonicalKey()
    {
        var services = new ServiceCollection().AddApplication();

        var keys = services
            .Where(descriptor => descriptor.ServiceType == typeof(IOutputFilter))
            .Select(descriptor => descriptor.ServiceKey)
            .OfType<string>();

        keys.Should().BeEquivalentTo(
            DotnetSubcommands.Ordered,
            "a filter registered under a typo'd or orphaned key would resolve for no dtk-handled subcommand "
            + "while still silently satisfying container validation");
    }

    [Fact]
    public void GeneratedHooks_DeclareExactlyTheCanonicalSubcommands()
    {
        // Pinned to the literal, known-good bytes rather than recomputed from
        // DotnetSubcommands.Sorted: if the source list shrinks or grows, the expectation must
        // not shrink or grow in lockstep with it, or this test could never fail. Adding a
        // subcommand means updating this literal by hand — that is the intended tripwire, since
        // it forces whoever adds one to also regenerate the committed hook.
        const string expectedTuple =
            "_DTK_SUBCOMMANDS = (\"build\", \"clean\", \"format\", \"list package\", \"restore\", \"test\")";

        foreach (var (name, hook) in new (string Name, string Hook)[]
                 {
                     ("Claude", HookScriptTemplates.ClaudeHook),
                     ("Gemini", HookScriptTemplates.GeminiHook),
                     ("Copilot CLI", HookScriptTemplates.CopilotCliHook)
                 })
        {
            hook.Should().Contain(
                expectedTuple,
                "the {0} hook's subcommand tuple must match the canonical set — if it doesn't, regenerate "
                + "'.claude/hooks/dotnet-to-dtk.py' from HookScriptTemplates.ClaudeHook and update this pinned "
                + "literal, rather than editing the literal alone",
                name);
        }
    }

    [Fact]
    public void GeneratedHooks_DocumentEveryCanonicalSubcommand()
    {
        // Pinned to the literal, known-good bytes for the same reason as the tuple assertion
        // above: deriving the expected alternation from DotnetSubcommands.Ordered would make the
        // test move in lockstep with the thing it is supposed to be pinning. Adding a
        // subcommand requires updating this literal, which forces regenerating the hooks too.
        const string expected = "rewrites `dotnet build|test|restore|clean|format|list package`";

        HookScriptTemplates.ClaudeHook.Should().Contain(
            expected,
            "the Claude hook's docstring must list every canonical subcommand, or a user reading it would "
            + "not know the hook covers the newest one");
        HookScriptTemplates.GeminiHook.Should().Contain(
            expected,
            "the Gemini hook's docstring must list every canonical subcommand, or a user reading it would "
            + "not know the hook covers the newest one");
        HookScriptTemplates.CopilotCliHook.Should().Contain(
            expected,
            "the Copilot CLI hook's docstring must list every canonical subcommand, or a user reading it "
            + "would not know the hook covers the newest one");
    }

    [Fact]
    public void RepoClaudeHook_MatchesTheGeneratedHook()
    {
        var repoRoot = FindRepoRoot();
        var hookPath = Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py");

        File.Exists(hookPath).Should().BeTrue("this repo ships its own copy of the Claude hook at {0}", hookPath);

        var committed = File.ReadAllText(hookPath).ReplaceLineEndings("\n");

        // The committed copy is compared against the *stamped* form, because that is what a user
        // receives. Comparing against the bare template would let this repo's copy and the
        // installed one diverge in exactly the field that decides whether dtk will refresh it.
        committed.Should().Be(
            ArtifactStamping.Apply(HookScriptTemplates.ClaudeHook, StampStyle.HashComment),
            "the committed hook must be regenerated whenever the template changes, or this repo's own "
            + "agent sessions silently stop rewriting the newest subcommand");
    }

    [Fact]
    public void RepoClaudeHook_CarriesAVerifiableStamp()
    {
        var repoRoot = FindRepoRoot();
        var committed = File.ReadAllText(Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py"));

        ArtifactStamping.IsAuthentic(committed).Should().BeTrue(
            "an unverifiable stamp would make dtk treat this repo's own hook as user-edited and refuse "
            + "to refresh it");
    }

    [Fact]
    public void RepoCopilotInstructions_MatchesTheGeneratedSection()
    {
        var repoRoot = FindRepoRoot();
        var instructionsPath = Path.Combine(repoRoot, ".github", "copilot-instructions.md");

        File.Exists(instructionsPath).Should().BeTrue(
            "this repo ships its own copy of the Copilot CLI instructions at {0}", instructionsPath);

        var committed = File.ReadAllText(instructionsPath);

        // Unlike '.claude/hooks/dotnet-to-dtk.py', this file has no test binding it to its
        // generator, so a subcommand addition can leave it stale silently — the exact failure
        // this test exists to prevent. The committed file predates section-merging and happens to
        // be nothing but the dtk-managed section, so it must be byte-identical to
        // CopilotCliIntegrator.CopilotSection; a repo that also carried hand-written content
        // outside the '<!-- dtk -->' / '<!-- /dtk -->' markers would need a substring assertion
        // instead.
        committed.Should().Be(
            CopilotCliIntegrator.CopilotSection,
            "the committed instructions must be regenerated (via 'dtk integrate copilot-cli' into a "
            + "scratch directory, then copied over) whenever the template changes, or this repo's own "
            + "copilot-instructions.md silently stops advertising the newest subcommand");
    }

    [Fact]
    public void IntegrationProse_ListsEveryCanonicalSubcommand()
    {
        // Pinned literals, for the same reason as the hook assertions above: deriving these from
        // DotnetSubcommands.Ordered would make the expectation move in lockstep with the source, and
        // the test could never fail. Adding a subcommand means editing these by hand.
        const string expectedProse = "build, test, restore, clean, format, and list package";
        const string expectedAlternation = "build|test|restore|clean|format|list package";
        const string expectedSlashAlternation = "build/test/restore/clean/format/list package";
        const string expectedBacktickProse =
            "`dotnet build`, `test`, `restore`, `clean`, `format`, and `list package`";

        IntegrationInstructions.SubcommandProse.Should().Be(
            expectedProse,
            "the shared instructions must name every dtk-handled subcommand, or users are told to "
            + "keep using raw dotnet for the newest one");
        IntegrationInstructions.SubcommandAlternation.Should().Be(expectedAlternation);
        IntegrationInstructions.SubcommandSlashAlternation.Should().Be(
            expectedSlashAlternation,
            "the Aider conf-section comment must name every dtk-handled subcommand, or a user "
            + "reading .aider.conf.yml would not know dtk covers the newest one");
        IntegrationInstructions.SubcommandBacktickProse.Should().Be(
            expectedBacktickProse,
            "the Claude Code skill's 'Drop-in replacement for' sentence must name every dtk-handled "
            + "subcommand, or a user reading the skill would not know dtk covers the newest one");
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "one")]
    [InlineData(2, "one and two")]
    [InlineData(3, "one, two, and three")]
    [InlineData(4, "one, two, three, and four")]
    public void BuildProse_UsesTheSerialCommaOnlyForThreeOrMoreNames(int count, string expected)
    {
        // The two-name case is unreachable while Ordered holds more than two, but the prose it
        // produces is user-facing, so the join is verified across every arm rather than only the
        // arm today's canonical list happens to take.
        string[] names = ["one", "two", "three", "four"];

        IntegrationInstructions.BuildProse(names[..count]).Should().Be(expected);
    }

    [Fact]
    public void ClaudeSkillDescription_NamesEveryCanonicalSubcommand()
    {
        // Pinned to the literal, known-good bytes for the same reason as the assertions above:
        // deriving it from DotnetSubcommands.Ordered would make the expectation move in lockstep
        // with the source and the test could never fail. Adding a subcommand means editing this by
        // hand. This one is worth pinning separately from the prose forms because it is Claude
        // Code's *skill-trigger* text: if it omits a subcommand, the skill silently never surfaces
        // for that intent, and nothing inside dtk can observe that.
        const string expectedDescription =
            "Use `dtk` (DotnetTokenKiller) instead of raw `dotnet` commands to reduce token usage "
            + "when running `dotnet` build, test, restore, clean, format, and list package commands.";

        ClaudeCodeIntegrator.SkillMarkdown.Should().Contain(
            $"description: '{expectedDescription}'",
            "the skill's frontmatter description is what Claude Code matches user intent against, so "
            + "a subcommand missing from it means the skill never fires for that subcommand");
    }

    [Fact]
    public void ClaudeSkill_EmbedsTheSharedUsageBody()
    {
        // The skill used to carry its own hardcoded 'sh' example block, which omitted
        // 'dtk dotnet list package --outdated' — so the skill named 'list package' in one sentence
        // and then contradicted itself in its own examples. Embedding the shared body is what makes
        // that impossible; this test is what keeps it embedded.
        ClaudeCodeIntegrator.SkillMarkdown.Should().Contain(
            IntegrationInstructions.UsageBody,
            "the skill must embed the shared usage body verbatim rather than restate it, or its "
            + "examples drift from every other provider's");
    }

    [Fact]
    public void SharedInstructions_MentionEverySubcommandInTheIntro()
    {
        foreach (var subcommand in DotnetSubcommands.Ordered)
        {
            IntegrationInstructions.Intro.Should().Contain(
                subcommand,
                "'{0}' is canonical, so the instructions embedded by every provider must mention it",
                subcommand);
        }
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding <c>DotnetTokenKiller.slnx</c>.
    /// xunit 2.x has no runtime skip, and the test project only ever runs from inside the repo,
    /// so not finding it is a failure rather than a skip.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No ancestor of <see cref="AppContext.BaseDirectory"/> contains <c>DotnetTokenKiller.slnx</c>.
    /// </exception>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }
}
