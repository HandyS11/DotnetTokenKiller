using DotnetTokenKiller.Domain;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Shared "how to use dtk" instructions markdown, embedded verbatim by every provider integrator
/// that documents dtk usage (Aider, Cursor, Gemini CLI, GitHub Copilot, JetBrains AI, Windsurf).
/// Keeping this text in one place means those six copies can never drift out of sync; each
/// integrator still supplies its own heading, section markers, or frontmatter around it.
/// </summary>
internal static class IntegrationInstructions
{
    /// <summary>
    /// The canonical subcommands as an Oxford-comma prose list, e.g.
    /// <c>build, test, restore, clean, and format</c>.
    /// </summary>
    internal static readonly string SubcommandProse = BuildProse(DotnetSubcommands.Ordered);

    /// <summary>The canonical subcommands as a <c>build|test|…</c> alternation, in display order.</summary>
    internal static readonly string SubcommandAlternation = string.Join("|", DotnetSubcommands.Ordered);

    /// <summary>The canonical subcommands as a <c>build/test/…</c> slash-separated list, in display order.</summary>
    internal static readonly string SubcommandSlashAlternation = string.Join("/", DotnetSubcommands.Ordered);

    /// <summary>
    /// The canonical subcommands as an Oxford-comma prose list of backtick-wrapped names, with the
    /// first name also carrying a <c>dotnet </c> prefix inside its backticks, e.g.
    /// <c>`dotnet build`, `test`, `restore`, `clean`, and `format`</c>. Used where the surrounding
    /// sentence reads as "a drop-in replacement for `dotnet &lt;subcommand&gt;`" rather than as a
    /// list of dtk's own subcommands.
    /// </summary>
    internal static readonly string SubcommandBacktickProse = BuildProse(AsDotnetInvocations(DotnetSubcommands.Ordered));

    /// <summary>
    /// Introductory sentence describing dtk's purpose. Used standalone (followed by a "## Usage"
    /// subheading) by Aider, Cursor, and Windsurf, or as the lead-in of <see cref="Markdown"/> for
    /// providers that embed the instructions directly under their own heading.
    /// </summary>
    internal static readonly string Intro =
        $"""
        Use `dtk` instead of raw `dotnet` for {SubcommandProse} commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.
        """;

    /// <summary>
    /// Example command block plus the forwarding/exit-code/passthrough notes, common to every
    /// provider's dtk usage instructions.
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
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
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

    private static string BuildProse(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
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
