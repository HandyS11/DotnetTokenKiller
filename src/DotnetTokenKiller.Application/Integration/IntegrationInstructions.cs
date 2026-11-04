using DotnetTokenKiller.Domain;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Shared "how to use dtk" instructions markdown, embedded verbatim by every provider integrator
/// that documents dtk usage (Aider, Claude Code, Cursor, Devin, Gemini CLI, GitHub Copilot, GitHub Copilot
/// CLI, JetBrains AI). Keeping this text in one place means those copies can never drift
/// out of sync; each integrator still supplies its own heading, section markers, or frontmatter
/// around it.
/// </summary>
internal static class IntegrationInstructions
{
    /// <summary>
    /// The canonical subcommands as an Oxford-comma prose list, e.g.
    /// <c>build, test, restore, clean, format, and list package</c>.
    /// </summary>
    internal static readonly string SubcommandProse = BuildProse(DotnetSubcommands.Ordered);

    /// <summary>The canonical subcommands as a <c>build|test|…</c> alternation, in display order.</summary>
    internal static readonly string SubcommandAlternation = string.Join("|", DotnetSubcommands.Ordered);

    /// <summary>The canonical subcommands as a <c>build/test/…</c> slash-separated list, in display order.</summary>
    internal static readonly string SubcommandSlashAlternation = string.Join("/", DotnetSubcommands.Ordered);

    /// <summary>
    /// The canonical subcommands as an Oxford-comma prose list of backtick-wrapped names, with the
    /// first name also carrying a <c>dotnet </c> prefix inside its backticks, e.g.
    /// <c>`dotnet build`, `test`, `restore`, `clean`, `format`, and `list package`</c>. Used where the
    /// surrounding sentence reads as "a drop-in replacement for `dotnet &lt;subcommand&gt;`" rather
    /// than as a list of dtk's own subcommands.
    /// </summary>
    internal static readonly string SubcommandBacktickProse = BuildProse(AsDotnetInvocations(DotnetSubcommands.Ordered));

    /// <summary>
    /// Introductory sentence describing dtk's purpose. Used standalone (followed by a "## Usage"
    /// subheading) by Aider, Cursor, and Devin, or as the lead-in of <see cref="Markdown"/> for
    /// providers that embed the instructions directly under their own heading. One sentence, because it is loaded
    /// in every agent session: the 0.9.0 text added "<c>dtk</c> filters output to actionable signal only, reducing
    /// noise by 50-97%.", a claim the agent cannot act on.
    /// </summary>
    internal static readonly string Intro =
        $"""
        Use `dtk` instead of raw `dotnet` for {SubcommandProse} commands.
        """;

    /// <summary>
    /// Example command block plus the notes common to every provider's dtk usage instructions: forwarding, exit
    /// codes, passthrough, the status line, <c>dtk log</c>, <c>dtk pipe</c> and the raw-output switch. Every sentence
    /// costs tokens on each agent session, so a note belongs here only if every harness benefits from it.
    /// </summary>
    internal const string UsageBody =
        """
        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        dtk dotnet format
        dtk dotnet format --verify-no-changes
        dtk dotnet list package --outdated
        dtk dotnet publish -c Release
        dtk dotnet pack
        dtk dotnet tool restore
        dtk dotnet ef migrations add <Name>
        dtk dotnet stryker
        dtk dotnet jb cleanupcode <solution>
        ```

        - Arguments and flags are forwarded unchanged; `dtk test`, `dtk build` etc. work without `dotnet`.
        - Exit codes are preserved.
        - Unknown subcommands (e.g. `run`, `watch`) and `--format json`/`-getProperty:` output pass through unchanged.
        - Don't pipe dtk output into tail or grep: its last line is the status.
        - `dtk log [build|test]` prints the latest run's raw output instead of re-running: last 100 lines, `--full` for all, `--list`.
        - `dtk pipe <subcommand>` filters stdin. `-q` on dotnet's own subcommands prints only the filtered output.
        - Options on filtered runs: `--tee <path>` keeps the raw output as evidence, `--max-lines N` caps the body; `test` also takes `--names` (passed tests), `--show-output` (failing tests' output) and `--fail-on-empty` (exit 8 if no test ran).
        - Raw output: `DTK_RAW=1` (environment or prefix); a hook that rewrites plain `dotnet …` skips `\dotnet …`, `command dotnet …` and `rtk proxy dotnet …`.
        """;

    /// <summary>
    /// <see cref="Intro"/> and <see cref="UsageBody"/> joined by a blank line, matching the layout
    /// used when no separate "## Usage" subheading sits between them (Gemini CLI, GitHub Copilot,
    /// JetBrains AI).
    /// </summary>
    internal static readonly string Markdown =
        $"""
        {Intro}

        {UsageBody}
        """;

    /// <summary>
    /// SHA-256 hashes of every Cursor rule (<c>.cursor/rules/dtk.mdc</c>) a released dtk wrote, so that
    /// <c>--uninstall</c> recognizes an unedited rule from an older dtk as its own and deletes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each hash is the lowercase hex SHA-256 of the file's UTF-8 bytes with <c>\n</c> line endings, as
    /// <see cref="UninstallHelpers.RemoveOwnedFileAsync"/> computes it. They were taken from the files each released
    /// package from NuGet writes (<c>dtk integrate cursor</c>, <c>dtk init cursor</c>); the body changed in 0.4.0, 0.7.0,
    /// 0.9.0 and 0.10.0, and 0.1.0 and 0.2.0 had no Cursor integration.
    /// </para>
    /// <para>
    /// <b>Any change to the text a Cursor rule renders</b> — <see cref="Intro"/>, <see cref="UsageBody"/>,
    /// <see cref="DotnetSubcommands.Ordered"/> or <c>CursorIntegrator</c>'s own frontmatter — <b>must append the
    /// previous body's hash here</b> once that body has shipped in a release; otherwise an uninstall keeps every rule
    /// the released version wrote. <c>ReleasedOwnedFileHashesTests</c> pins the current body's hash to force the
    /// question.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyList<string> ReleasedCursorRuleHashes =
    [
        "9cd79f606fd5fa0b959b6d9a39382f8184269b9fada9e79622123d28e660d964", // 0.3.0–0.3.1
        "3002e61a693f4ab92e7938cb34ea27cb3b9d40387e1e66658ad4b670ea6313b0", // 0.4.0–0.6.0
        "735f10527ecc916333caa23a70a156ecb577b85587fc403d38ad0a18b91ecb69", // 0.7.0–0.8.0
        "90fe9cc216b6d130483a2e6cb1cdd0c2f56e3e903aa9e4db8a7d90fb2ac50c2f", // 0.9.0
        "7c369fc9f3989aea48fce6aa9e723267b637808f0844628c3c93089f134a0c7c" // 0.10.0
    ];

    /// <summary>
    /// SHA-256 hashes of every Windsurf rule (<c>.windsurf/rules/dtk.md</c>) and Aider instructions file
    /// (<c>.aider-dtk-instructions.md</c>) a released dtk wrote; the two have always had the same body.
    /// </summary>
    /// <remarks>
    /// Computed and maintained exactly as <see cref="ReleasedCursorRuleHashes"/> is: <b>any change to the text
    /// either renders must append the previous body's hash here</b> once that body has shipped in a release.
    /// </remarks>
    internal static readonly IReadOnlyList<string> ReleasedMarkdownRuleHashes =
    [
        "22afee5d48f8412be23909f8b2a5b625a64a8d21b12797b557136281352e230a", // 0.3.0–0.3.1
        "ca57ccc020e44aea0ef2d65ef268c65622ae9d00402124e75bd5e44ef515a28b", // 0.4.0–0.6.0
        "f4324d4ef087a70168f045c27cbba4885da3d9e06c8ea9d9f723210cd7e757b9", // 0.7.0–0.8.0
        "259680360aae93a3a7aecfd6b079e523175ab967886cf995d6f4446c4ae9be0d", // 0.9.0
        "9a4f734ee4318b9b203a5d87532a4fc6c25ce465a8b62fe842f428f3baca54b8" // 0.10.0
    ];

    /// <summary>Joins names into an Oxford-comma prose list, e.g. <c>build, test, and format</c>.</summary>
    /// <param name="names">The names to join, in the order they should read.</param>
    /// <returns>The joined list, or <see cref="string.Empty"/> when <paramref name="names"/> is empty.</returns>
    /// <remarks>
    /// Two names join as <c>a and b</c> with no comma: a serial comma separates three or more items,
    /// so emitting one for a pair reads as a mistake. Unreachable while
    /// <see cref="DotnetSubcommands.Ordered"/> holds more than two, but this is a general join and
    /// the shape it produces is user-facing.
    /// </remarks>
    internal static string BuildProse(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}"
        };

    /// <summary>
    /// Backtick-wraps each name for use with <see cref="BuildProse"/>, prefixing only the first with
    /// <c>dotnet </c> — the shape <see cref="SubcommandBacktickProse"/> needs and the one join Oxford
    /// commas around, so the comma/"and" logic itself is not duplicated per generated form.
    /// </summary>
    /// <param name="names">The canonical subcommand names, in display order.</param>
    private static IReadOnlyList<string> AsDotnetInvocations(IReadOnlyList<string> names) =>
        [.. names.Select((name, index) => index == 0 ? $"`dotnet {name}`" : $"`{name}`")];
}
