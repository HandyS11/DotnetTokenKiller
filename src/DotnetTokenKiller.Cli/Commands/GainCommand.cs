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
            console.MarkupLine(
                "[grey]No data yet. Run some [bold]dtk dotnet[/] commands to start tracking savings.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("Command")
            .AddColumn(new TableColumn("Runs").RightAligned())
            .AddColumn(new TableColumn("Without Tool").RightAligned())
            .AddColumn(new TableColumn("Used by Tool").RightAligned())
            .AddColumn(new TableColumn("Saved").RightAligned())
            .AddColumn(new TableColumn("Avg Savings").RightAligned());

        foreach (var (cmd, detail) in summary.CommandDetails)
        {
            table.AddRow(
                new Text(cmd),
                new Text(detail.RunCount.ToString(CultureInfo.InvariantCulture)),
                new Text(detail.TotalInputTokens.ToString(CultureInfo.InvariantCulture)),
                new Text(detail.TotalOutputTokens.ToString(CultureInfo.InvariantCulture)),
                new Text(detail.TotalSavedTokens.ToString(CultureInfo.InvariantCulture)),
                new Text(detail.AverageSavingsPercentage.ToString("F1", CultureInfo.InvariantCulture) + "%"));
        }

        table.AddEmptyRow();

        table.AddRow(
            new Markup("[bold]TOTAL[/]"),
            new Markup($"[bold]{summary.TotalCommands.ToString(CultureInfo.InvariantCulture)}[/]"),
            new Markup($"[bold]{summary.TotalInputTokens.ToString(CultureInfo.InvariantCulture)}[/]"),
            new Markup($"[bold]{summary.TotalOutputTokens.ToString(CultureInfo.InvariantCulture)}[/]"),
            new Markup($"[bold]{summary.TotalSavedTokens.ToString(CultureInfo.InvariantCulture)}[/]"),
            new Markup($"[bold]{summary.AverageSavingsPercentage.ToString("F1", CultureInfo.InvariantCulture)}%[/]"));

        console.Write(table);
        return 0;
    }
}
