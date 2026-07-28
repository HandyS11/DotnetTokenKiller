using System.Globalization;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Formatting;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Retrieves a previous run's output from the tee directory.</summary>
/// <param name="logView">The log selection and windowing use case.</param>
/// <param name="configProvider">The configuration provider, read to explain an empty result.</param>
/// <param name="console">The Spectre.Console sink for the listing table and error messages.</param>
/// <param name="output">
/// The raw writer for the retrieved output, bypassing console width wrapping so build output is
/// reproduced as it was captured.
/// </param>
/// <param name="workingDirectory">
/// Supplies the working directory to scope to. Injected rather than read from
/// <see cref="Environment.CurrentDirectory"/> so tests need not mutate process-global state.
/// </param>
internal sealed class LogCommand(
    LogViewUseCase logView,
    IConfigProvider configProvider,
    IAnsiConsole console,
    TextWriter output,
    IWorkingDirectory workingDirectory) : AsyncCommand<LogCommandSettings>
{
    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        LogCommandSettings settings,
        CancellationToken cancellationToken)
        => RunAsync(settings, cancellationToken);

    /// <summary>Runs the command against already-parsed settings, so it is callable from tests.</summary>
    /// <param name="settings">The parsed command settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>0 when something was shown, 1 on a usage error or an empty result.</returns>
    internal async Task<int> RunAsync(LogCommandSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Not enforced via CommandSettings.Validate: a failed Validate throws internally and
        // Spectre exits with -1 (255 on Linux) without ever reaching Program.cs's catch block,
        // which would make this usage error exit differently from the unknown-subcommand one below.
        if (settings.Lines < 1)
        {
            console.MarkupLine("[red]--lines must be 1 or greater.[/]");
            return 1;
        }

        if (settings.Index < 1)
        {
            console.MarkupLine("[red]--index must be 1 or greater.[/]");
            return 1;
        }

        string? subcommand = null;
        if (settings.Subcommand.Length > 0)
        {
            if (!DotnetSubcommands.TryMatch(settings.Subcommand, out var match))
            {
                console.MarkupLine(
                    $"[red]No logs are kept for:[/] {string.Join(' ', settings.Subcommand).EscapeMarkup()}.");
                console.MarkupLine($"Available: {string.Join(", ", DotnetSubcommands.Ordered).EscapeMarkup()}");
                return 1;
            }

            subcommand = match.Name;
        }

        var query = new LogQuery(
            subcommand,
            settings.All ? null : workingDirectory.Current,
            settings.Index,
            settings.Lines,
            settings.Full);

        if (settings.List)
        {
            var selection = await logView.SelectAsync(query, cancellationToken).ConfigureAwait(false);
            if (selection.Matches.Count == 0)
            {
                return await ReportEmptyAsync(selection, settings, cancellationToken).ConfigureAwait(false);
            }

            TeeLogRenderer.RenderList(console, selection, settings.All);
            return 0;
        }

        var result = await logView.ViewAsync(query, cancellationToken).ConfigureAwait(false);
        if (result.View is null)
        {
            if (result.Selection.Matches.Count > 0)
            {
                var available = result.Selection.Matches.Count.ToString(CultureInfo.InvariantCulture);
                console.MarkupLine(
                    $"[red]No log at index {settings.Index.ToString(CultureInfo.InvariantCulture)}.[/] "
                    + $"{available} available — use [bold]--list[/] to see them.");
                return 1;
            }

            return await ReportEmptyAsync(result.Selection, settings, cancellationToken).ConfigureAwait(false);
        }

        await TeeLogRenderer.RenderViewAsync(output, result.View, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Explains why nothing was found. The reason matters more than the fact: with the default
    /// settings a successful or small run leaves no log at all, and a bare "not found" reads as a
    /// broken feature rather than as configuration.
    /// </summary>
    /// <param name="selection">The empty selection, carrying the excluded-legacy count.</param>
    /// <param name="settings">The parsed settings, for tailoring the message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Always 1.</returns>
    private async Task<int> ReportEmptyAsync(
        LogSelection selection,
        LogCommandSettings settings,
        CancellationToken cancellationToken)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var scope = settings.All ? "no logs found" : "no logs for this project";
        console.MarkupLine($"[yellow]{scope}.[/]");

        if (config.Tee.Mode == TeeMode.Never)
        {
            console.MarkupLine(
                "Tee is disabled ([bold]tee.mode[/] = [bold]Never[/]). "
                + "Enable it with [bold]dtk config set tee.mode Failures[/].");
        }
        else if (config.Tee.Mode == TeeMode.Failures)
        {
            console.MarkupLine(
                "[bold]tee.mode[/] is [bold]Failures[/], so only failed runs are saved, and output "
                + "under 500 characters is never saved. Use [bold]dtk config set tee.mode Always[/] "
                + "to keep every run.");
        }
        else
        {
            console.MarkupLine("Output under 500 characters is never saved.");
        }

        if (selection.LegacyExcluded > 0)
        {
            var count = selection.LegacyExcluded.ToString(CultureInfo.InvariantCulture);
            console.MarkupLine(
                $"{count} older log(s) predate project tracking — see [bold]--all[/].");
        }

        if (!settings.All && selection.LegacyExcluded == 0)
        {
            console.MarkupLine("Logs from other projects may exist — see [bold]--all[/].");
        }

        return 1;
    }
}
