using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Clears all saved tracking data.</summary>
/// <param name="resetTracking">The reset tracking use case.</param>
/// <param name="fullReset">The full reset use case (tracking data, tee logs, config file).</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class ResetCommand(
    ResetTrackingUseCase resetTracking,
    FullResetUseCase fullReset,
    IAnsiConsole console) : AsyncCommand<ResetCommandSettings>
{
    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ResetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var prompt = settings.All
            ? "[yellow]This will delete all tracking data, tee logs, and the configuration file. Continue?[/]"
            : "[yellow]This will delete all tracking data. Continue?[/]";

        if (!settings.Force && !await console.ConfirmAsync(prompt, false, cancellationToken).ConfigureAwait(false))
        {
            console.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        if (settings.All)
        {
            await fullReset.ResetAsync(cancellationToken).ConfigureAwait(false);
            console.MarkupLine("[green]All dtk state removed (tracking data, tee logs, configuration).[/]");
        }
        else
        {
            await resetTracking.ResetAsync(cancellationToken).ConfigureAwait(false);
            console.MarkupLine("[green]Tracking data cleared.[/]");
        }

        return 0;
    }
}
