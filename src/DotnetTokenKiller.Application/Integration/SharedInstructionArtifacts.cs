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
    /// The skill's frontmatter <c>description:</c> line: Claude Code's <em>skill-trigger</em> text, which decides when an
    /// agent loads the skill. Every agent already gets the agent rules at its start (Claude Code's context hooks) or in
    /// its instructions file, so the skill is the reference for what they leave out, and its trigger names those options
    /// rather than every <c>dotnet</c> command: a trigger on every build loaded the whole skill at the start of most
    /// sessions, only to repeat the rules. A single-quoted YAML value, so it holds no apostrophe.
    /// </summary>
    private const string SkillDescription =
        "Reference for DotnetTokenKiller (dtk) options beyond plain use: keeping the raw output of a run (--tee), "
        + "the console output of a failing test (--show-output), the tests that passed (--names), failing a run that "
        + "matched no test (--fail-on-empty), capping output (--max-lines), the saved raw output of a run (dtk log), "
        + "filtering saved output (dtk pipe), every warning of a build, and token savings (dtk gain).";

    /// <summary>
    /// The skill written to <c>dotnet-token-killer/SKILL.md</c> under <c>.claude/skills</c> (Claude Code) and
    /// <c>.agents/skills</c> (Codex CLI, OpenCode, …): the reference for dtk's options beyond plain use. It embeds the
    /// shared passthrough and raw-output notes (<see cref="IntegrationInstructions.PassthroughBullets"/>,
    /// <see cref="IntegrationInstructions.RawOutputBullet"/>) and leaves the examples and the agent rules to the
    /// instructions and the context.
    /// </summary>
    internal const string SkillMarkdown =
        $"""
        ---
        name: dotnet-token-killer
        description: '{SkillDescription}'
        ---

        # DotnetTokenKiller (dtk) reference

        `dtk` wraps `dotnet` commands and keeps what an agent acts on. How to run it is in your instructions; this is the rest.

        ## Options

        - `--tee <file>` keeps the raw output of a run in a file, as evidence, beside the filtered output.
        - `--max-lines N` caps the filtered body, ending with `… N more lines (dtk log)`; the status line is never cut.
        - On `dotnet test` and `dtk pipe test`: `--names` lists the tests that passed, grouped by class; `--show-output` shows the console output of each failing test; `--fail-on-empty` makes a run in which no test matched exit 8.

        | Flag         | Purpose                                             |
        |--------------|-----------------------------------------------------|
        | `-q`         | Print only the filtered output (dotnet subcommands) |
        | `-v`         | Echo the resolved command line (dotnet subcommands) |
        | `--show-log` | Print path to full unfiltered log after a run       |

        `--vv` also dumps all the raw output; prefer `dtk log`.

        ## A run's output, again

        - `dtk log [build|test]` prints the latest run's raw output: last 100 lines, `--full` for all, `--list` for the recent runs.
        - `dtk pipe <subcommand>` filters text on stdin as that subcommand's filter would: `dtk log build --full | dtk pipe build`.

        ## Warnings in changed files

        In a git work tree, a build, test, publish or pack without errors lists only the warnings of the files the work in progress changed, and counts the others on one line (`N warnings in unchanged files not shown`); `dtk log build --full | dtk pipe build` lists them all. Every warning is listed when the work changed a project file, a `.props` or `.targets` file, `.editorconfig` or `global.json`, or the repository has no `origin/HEAD`.

        ## Passthrough and raw output

        {IntegrationInstructions.PassthroughBullets}
        {IntegrationInstructions.RawOutputBullet}

        ## Key Behaviors

        - Paths are workspace-relative (`src/Foo.cs`, not absolute)
        - Errors and warnings are grouped by file
        - Works with xUnit, NUnit, MSTest, and Reqnroll
        - Test status lines count the build warnings of the projects compiled in that run; an up-to-date build compiles nothing and reports none (so a build/publish/pack zero covers only what that run compiled)

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
