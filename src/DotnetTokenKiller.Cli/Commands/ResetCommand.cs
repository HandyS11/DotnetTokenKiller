using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class ResetCommand(
    ResetTrackingUseCase resetTracking,
    IAnsiConsole console) : AsyncCommand<ResetCommandSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ResetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Force && !await console.ConfirmAsync("[yellow]This will delete all tracking data. Continue?[/]", defaultValue: false, cancellationToken))
        {
            console.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        await resetTracking.ResetAsync(cancellationToken);
        console.MarkupLine("[green]Tracking data cleared.[/]");
        return 0;
    }
}
