namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// The instruction artifacts more than one harness reads: the <c>&lt;!-- dtk --&gt;</c> section merged into
/// <c>AGENTS.md</c> and <c>GEMINI.md</c>, and the <c>dotnet-token-killer</c> skill.
/// </summary>
/// <remarks>
/// Codex CLI, OpenCode and Antigravity CLI all read <c>AGENTS.md</c> and <c>.agents/skills/</c>, and Antigravity CLI
/// also reads the <c>~/.gemini/GEMINI.md</c> that <see cref="GeminiCliIntegrator"/> writes. Every provider writing
/// one of those files writes these exact strings, so a file two providers share never flips between versions.
/// </remarks>
internal static class SharedInstructionArtifacts
{
    /// <summary>Opens the dtk-managed section.</summary>
    internal const string SectionMarker = "<!-- dtk -->";

    /// <summary>Closes the dtk-managed section.</summary>
    internal const string SectionEndMarker = "<!-- /dtk -->";

    /// <summary>The skill's folder name, which OpenCode requires to equal its frontmatter <c>name</c>.</summary>
    private const string SkillName = "dotnet-token-killer";

    /// <summary>
    /// The skill's frontmatter <c>description:</c> line. This is Claude Code's <em>skill-trigger</em>
    /// text — the one string that decides whether the skill surfaces for a given user intent — so it
    /// is derived from <see cref="IntegrationInstructions.SubcommandProse"/> rather than hand-written.
    /// A hardcoded list here meant a user who ran <c>dtk init claude</c> got a skill that never
    /// fired for package-listing intent, which is invisible from inside dtk.
    /// </summary>
    private static readonly string SkillDescription =
        "Use `dtk` (DotnetTokenKiller) instead of raw `dotnet` commands to reduce token usage when "
        + $"running `dotnet` {IntegrationInstructions.SubcommandProse} commands.";

    /// <summary>
    /// The skill written to <c>dotnet-token-killer/SKILL.md</c> under <c>.claude/skills</c> (Claude Code)
    /// and <c>.agents/skills</c> (Codex CLI). Internal (rather than private) so
    /// <c>SubcommandBindingTests</c> can pin <see cref="SkillDescription"/> the same way it pins
    /// <see cref="CopilotCliIntegrator.CopilotSection"/> — this file is a derived artifact with no
    /// compile-time link to the canonical subcommand list.
    /// </summary>
    internal static readonly string SkillMarkdown =
        $"""
        ---
        name: dotnet-token-killer
        description: '{SkillDescription}'
        ---

        # DotnetTokenKiller (dtk)

        `dtk` wraps `dotnet` commands and filters output to actionable signal only, saving 50-97% of tokens by stripping SDK banners, MSBuild noise, progress lines, and duplicate diagnostics.

        ## Installation

        ```sh
        dotnet tool install -g DotnetTokenKiller  # requires .NET 10 SDK
        ```

        ## Usage

        Drop-in replacement for {IntegrationInstructions.SubcommandBacktickProse}:

        {IntegrationInstructions.UsageBody}

        ## Flags

        | Flag         | Purpose                                           |
        |--------------|---------------------------------------------------|
        | `--show-log` | Print path to full unfiltered log after a run     |
        | `-v`         | Echo the resolved command line before running     |
        | `--vv`       | Also dump raw dotnet output and elapsed time      |

        ## Key Behaviors

        - Paths are workspace-relative (`src/Foo.cs`, not absolute)
        - Build errors grouped by file; warnings grouped by diagnostic code with frequency counts
        - Works with xUnit, NUnit, MSTest, and Reqnroll
        - Run `dtk dotnet clean` first for a full warning report (incremental builds skip unchanged files)

        ## Token Savings

        ```sh
        dtk gain               # last 30 days
        dtk gain --days 7
        dtk gain --project     # current project only
        dtk gain --json
        ```
        """;

    /// <summary>
    /// Substring present in every generation of the skill file, used to recognize an unstamped copy
    /// installed by dtk 0.6.0 or earlier. It is the frontmatter <c>name:</c> line, which has never
    /// changed and cannot without breaking Claude Code's skill lookup.
    /// </summary>
    internal const string SkillLegacySignature = "name: dotnet-token-killer";

    /// <summary>The dtk section merged into <c>AGENTS.md</c> and <c>GEMINI.md</c>.</summary>
    internal static readonly string Section =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}
        {SectionEndMarker}
        """;

    /// <summary>Where the skill lives under a harness's skills directory.</summary>
    /// <param name="skillsDirectory">A skills root such as <c>.agents/skills</c>.</param>
    internal static string SkillPath(string skillsDirectory) => Path.Combine(skillsDirectory, SkillName, "SKILL.md");

    /// <summary>Merges <see cref="Section"/> into an instructions file and writes the stamped skill.</summary>
    /// <param name="instructionsPath">The <c>AGENTS.md</c> (or <c>GEMINI.md</c>) to merge into.</param>
    /// <param name="skillsDirectory">The skills root the skill folder goes under.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteAgentsFilesAsync(
        string instructionsPath, string skillsDirectory, IntegrationContext context, CancellationToken cancellationToken)
    {
        await IntegratorHelpers.WriteSectionBasedFileAsync(
            instructionsPath, SectionMarker, SectionEndMarker, Section, context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteGeneratedFileAsync(SkillArtifact(skillsDirectory), context, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes what <see cref="WriteAgentsFilesAsync"/> writes: dtk's section and the skill.</summary>
    /// <param name="instructionsPath">The <c>AGENTS.md</c> (or <c>GEMINI.md</c>) holding the section.</param>
    /// <param name="skillsDirectory">The skills root the skill folder is under.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RemoveAgentsFilesAsync(
        string instructionsPath, string skillsDirectory, IntegrationContext context, CancellationToken cancellationToken)
    {
        await UninstallHelpers.RemoveSectionAsync(instructionsPath, SectionMarker, SectionEndMarker, context, cancellationToken)
            .ConfigureAwait(false);

        await UninstallHelpers.RemoveGeneratedFileAsync(SkillArtifact(skillsDirectory), context, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The stamped skill as a generated artifact under a harness's skills directory.</summary>
    /// <param name="skillsDirectory">A skills root such as <c>.agents/skills</c>.</param>
    internal static GeneratedArtifact SkillArtifact(string skillsDirectory) =>
        new(SkillPath(skillsDirectory), SkillMarkdown, StampStyle.HtmlComment, SkillLegacySignature);
}
