using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Base command for all integrate subcommands.</summary>
/// <param name="integrateUseCase">The integration use case.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal abstract class IntegrateCommandBase(
    IntegrateUseCase integrateUseCase,
    IAnsiConsole console) : AsyncCommand<IntegrateCommandSettings>
{
    /// <summary>Gets the provider name handled by this command.</summary>
    protected abstract string ProviderName { get; }

    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        IntegrateCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = settings.Directory ?? Environment.CurrentDirectory;
        var result = await integrateUseCase
            .RunAsync(ProviderName, directory, settings.Force, cancellationToken)
            .ConfigureAwait(false);

        foreach (var file in result.CreatedFiles)
            console.MarkupLine($"[green]created[/]  {Markup.Escape(RelativePath(directory, file))}");

        foreach (var file in result.UpdatedFiles)
            console.MarkupLine($"[yellow]updated[/]  {Markup.Escape(RelativePath(directory, file))}");

        foreach (var file in result.SkippedFiles)
            console.MarkupLine($"[grey]skipped[/]  {Markup.Escape(RelativePath(directory, file))} [grey](use --force to overwrite)[/]");

        if (result.CreatedFiles.Count == 0 && result.UpdatedFiles.Count == 0 && result.SkippedFiles.Count > 0)
        {
            console.MarkupLine("[grey]Already integrated. Nothing to do.[/]");
        }
        else if (result.CreatedFiles.Count > 0 || result.UpdatedFiles.Count > 0)
        {
            console.MarkupLine($"[green]Done.[/] dtk is now integrated with [bold]{ProviderName}[/].");
        }

        return 0;
    }

    private static string RelativePath(string baseDir, string fullPath)
    {
        if (fullPath.StartsWith(baseDir, StringComparison.Ordinal))
            return fullPath[(baseDir.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');

        return fullPath;
    }
}
