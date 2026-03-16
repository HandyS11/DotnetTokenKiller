using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Clears all saved tracking data.</summary>
/// <param name="resetTracking">The reset tracking use case.</param>
/// <param name="console">The Spectre.Console output sink.</param>
public sealed class ResetCommand(
    ResetTrackingUseCase resetTracking,
    IAnsiConsole console) : AsyncCommand<ResetCommandSettings>
{
    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ResetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Force && !await console.ConfirmAsync("[yellow]This will delete all tracking data. Continue?[/]", defaultValue: false, cancellationToken).ConfigureAwait(false))
        {
            console.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        await resetTracking.ResetAsync(cancellationToken).ConfigureAwait(false);
        console.MarkupLine("[green]Tracking data cleared.[/]");
        return 0;
    }
}
