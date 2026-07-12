using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.Commands.Settings;
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
        if (!availableProviders.Contains(settings.Provider, StringComparer.OrdinalIgnoreCase))
        {
            console.MarkupLine(
                $"[red]Error:[/] Unknown provider '{Markup.Escape(settings.Provider)}'. " +
                $"Available: {Markup.Escape(string.Join(", ", availableProviders))}");
            return 1;
        }

        var directory = settings.Directory ?? Environment.CurrentDirectory;

        var result = await integrateUseCase
            .RunAsync(settings.Provider, directory, settings.Force, cancellationToken)
            .ConfigureAwait(false);

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
            // Once --force was already passed, telling the user to "use --force to overwrite" is
            // never true: WriteFileAsync/WriteSectionBasedFileAsync always honor force, so any
            // remaining skip (e.g. a hook already registered) is a force-independent no-op, not
            // something a repeated --force would change.
            var hint = settings.Force ? string.Empty : " [grey](use --force to overwrite)[/]";
            console.MarkupLine($"[grey]skipped[/]  {Markup.Escape(RelativePath(directory, file))}{hint}");
        }

        if (result.CreatedFiles.Count == 0 && result.UpdatedFiles.Count == 0 && result.SkippedFiles.Count > 0)
        {
            console.MarkupLine("[grey]Already integrated. Nothing to do.[/]");
        }
        else if (result.CreatedFiles.Count > 0 || result.UpdatedFiles.Count > 0)
        {
            console.MarkupLine($"[green]Done.[/] dtk is now integrated with [bold]{settings.Provider}[/].");
        }

        return 0;
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
