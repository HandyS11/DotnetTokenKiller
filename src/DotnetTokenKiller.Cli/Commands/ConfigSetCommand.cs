using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Sets a dtk configuration value.</summary>
/// <param name="configSet">The config-set use case.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class ConfigSetCommand(
    ConfigSetUseCase configSet,
    IAnsiConsole console) : AsyncCommand<ConfigSetCommandSettings>
{
    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ConfigSetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            await configSet.ExecuteAsync(settings.Key, settings.Value, cancellationToken).ConfigureAwait(false);
            console.MarkupLine($"[green]Set[/] [bold]{Markup.Escape(settings.Key)}[/] = [bold]{Markup.Escape(settings.Value)}[/]");
            return 0;
        }
        catch (ArgumentException ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (FormatException ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
