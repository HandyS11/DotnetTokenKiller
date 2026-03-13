using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Globalization;
using System.Text.Json;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class GainCommand(
    GainReportUseCase gainReport,
    IAnsiConsole console) : AsyncCommand<GainCommandSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GainCommandSettings settings,
        CancellationToken cancellationToken)
    {
        var projectPath = settings.Project ? Environment.CurrentDirectory : null;
        var summary = await gainReport.GetSummaryAsync(settings.Days, projectPath, cancellationToken);

        if (settings.Json)
        {
            var json = JsonSerializer.Serialize(summary, GainSummaryJsonContext.Default.GainSummary);
            console.WriteLine(json);
            return 0;
        }

        if (summary.TotalCommands == 0)
        {
            console.MarkupLine("[grey]No data yet. Run some [bold]dtk dotnet[/] commands to start tracking savings.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("Command")
            .AddColumn(new TableColumn("Tokens Saved").RightAligned());

        foreach (var (cmd, saved) in summary.SavedByCommand)
        {
            table.AddRow(
                new Text(cmd),
                new Text(saved.ToString(CultureInfo.InvariantCulture)));
        }

        table.AddEmptyRow();

        table.AddRow(
            new Markup($"[bold]TOTAL ({summary.TotalCommands.ToString(CultureInfo.InvariantCulture)} runs)[/]"),
            new Text($"{summary.TotalSavedTokens.ToString(CultureInfo.InvariantCulture)} ({summary.AverageSavingsPercentage:F1}% avg)"));

        console.Write(table);
        return 0;
    }
}
