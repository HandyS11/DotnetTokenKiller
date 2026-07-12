using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Integration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Installs dtk integration artifacts for the given AI assistant provider.</summary>
/// <param name="integrateUseCase">The integration use case.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class IntegrateCommand(IntegrateUseCase integrateUseCase, IAnsiConsole console)
    : AsyncCommand<IntegrateCommandSettings>
{
    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        IntegrateCommandSettings settings,
        CancellationToken cancellationToken)
        => RunAsync(settings, cancellationToken);

    internal async Task<int> RunAsync(
        IntegrateCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var availableProviders = integrateUseCase.AvailableProviders.ToList();
        var canonicalProvider = availableProviders.FirstOrDefault(
            p => string.Equals(p, settings.Provider, StringComparison.OrdinalIgnoreCase));

        if (canonicalProvider is null)
        {
            console.MarkupLine(
                $"[red]Error:[/] Unknown provider '{Markup.Escape(settings.Provider)}'. " +
                $"Available: {Markup.Escape(string.Join(", ", availableProviders))}");
            return 1;
        }

        var directory = settings.Directory ?? Environment.CurrentDirectory;

        IntegrationResult result;
        try
        {
            result = await integrateUseCase
                .RunAsync(canonicalProvider, directory, settings.Force, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // IntegratorHelpers.MergeJsonSettingsAsync throws this on a malformed/non-object
            // settings.json; surface it as a friendly exit-1 error instead of letting it escape
            // to Spectre's default handler (which would exit 255).
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        foreach (var file in result.CreatedFiles)
        {
            console.MarkupLine($"[green]created[/]  {Markup.Escape(RelativePath(directory, file))}");
        }

        foreach (var file in result.UpdatedFiles)
        {
            console.MarkupLine($"[yellow]updated[/]  {Markup.Escape(RelativePath(directory, file))}");
        }

        foreach (var file in result.SkippedFiles)
        {
            // Once --force was already passed, telling the user to "use --force" is never true:
            // WriteFileAsync/WriteSectionBasedFileAsync always honor force, so any remaining skip
            // (e.g. a hook already registered) is a force-independent no-op, not something a
            // repeated --force would change. Force *merges/appends* the managed section and
            // preserves user content — it never overwrites — so the hint must not say "overwrite".
            var hint = settings.Force ? string.Empty : " [grey](use --force to integrate into existing files)[/]";
            console.MarkupLine($"[grey]skipped[/]  {Markup.Escape(RelativePath(directory, file))}{hint}");
        }

        foreach (var note in result.Notes)
        {
            console.MarkupLine($"[cyan]note[/]     {Markup.Escape(note)}");
        }

        PrintSummary(result, settings.Force, canonicalProvider, console);

        return 0;
    }

    /// <summary>
    /// Prints the final one-line summary, gated on truth rather than merely on which result sets
    /// are non-empty. Under skip-unless-force, a skipped file (e.g. a pre-existing
    /// <c>.aider.conf.yml</c> without the dtk <c>read:</c> key) might never have been functionally
    /// integrated — the CLI cannot tell that apart from a file that already carries dtk's exact
    /// managed content, so without <c>--force</c> it must never claim "Done" or "Already
    /// integrated". Once <c>--force</c> is passed, <see cref="IntegratorHelpers.ShouldSkipWrite"/>
    /// -based skips can no longer occur (they require <c>!force</c>), so any remaining skip must
    /// come from content-based detection (e.g. the hook command is already registered) — "Already
    /// integrated" is honest in that case.
    /// </summary>
    /// <param name="result">The integration result whose created/updated/skipped sets drive the summary.</param>
    /// <param name="force">Whether the integration ran with the force flag.</param>
    /// <param name="provider">The canonical provider name to echo in the "Done" message.</param>
    /// <param name="console">The Spectre.Console output sink.</param>
    private static void PrintSummary(IntegrationResult result, bool force, string provider, IAnsiConsole console)
    {
        if (result.SkippedFiles.Count > 0 && !force)
        {
            console.MarkupLine(
                $"[yellow]{result.SkippedFiles.Count} existing file(s) were left untouched.[/] " +
                "Use [bold]--force[/] to integrate into them.");
            return;
        }

        if (result.CreatedFiles.Count > 0 || result.UpdatedFiles.Count > 0)
        {
            console.MarkupLine($"[green]Done.[/] dtk is now integrated with [bold]{provider}[/].");
            return;
        }

        console.MarkupLine("[grey]Already integrated. Nothing to do.[/]");
    }

    private static string RelativePath(string baseDir, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(baseDir))
        {
            return fullPath;
        }

        try
        {
            var normalizedBaseDir = Path.GetFullPath(
                baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var normalizedFullPath = Path.GetFullPath(fullPath);

            var relative = Path.GetRelativePath(normalizedBaseDir, normalizedFullPath);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch
        {
            // If paths cannot be normalized or related, fall back to the full path.
            return fullPath;
        }
    }
}
