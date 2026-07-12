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
    /// Introductory sentence describing dtk's purpose. Used standalone (followed by a "## Usage"
    /// subheading) by Aider, Cursor, and Windsurf, or as the lead-in of <see cref="Markdown"/> for
    /// providers that embed the instructions directly under their own heading.
    /// </summary>
    internal const string Intro =
        """
        Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
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
    internal const string Markdown =
        $"""
        {Intro}

        {UsageBody}
        """;
}
