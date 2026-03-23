using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Shows token savings analytics.</summary>
/// <param name="gainReport">The gain report use case.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class GainCommand(
    GainReportUseCase gainReport,
    IAnsiConsole console) : AsyncCommand<GainCommandSettings>
{
    internal const string CsvHeader =
        "timestamp,command,project_path,input_tokens,output_tokens,saved_tokens,savings_pct,execution_time_ms";

    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GainCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var projectPath = settings.Project ? Environment.CurrentDirectory : null;
        var commandFilter = settings.Command;

        if (settings.Export is not null)
        {
            if (!string.Equals(settings.Export, "csv", StringComparison.OrdinalIgnoreCase))
            {
                console.MarkupLine($"[red]Unknown export format:[/] {settings.Export.EscapeMarkup()}. Supported: csv");
                return 1;
            }

            var records = await gainReport.GetHistoryAsync(settings.Days, projectPath, commandFilter, cancellationToken).ConfigureAwait(false);
            var sb = new StringBuilder();
            sb.AppendLine(CsvHeader);
            foreach (var r in records)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{r.Timestamp:O},{EscapeCsv(r.Command)},{EscapeCsv(r.ProjectPath)},{r.InputTokens},{r.OutputTokens},{r.SavedTokens},{r.SavingsPercentage.ToString("F4", CultureInfo.InvariantCulture)},{r.ExecutionTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}");
            }

            console.Write(sb.ToString());
            return 0;
        }

        var summary = await gainReport.GetSummaryAsync(settings.Days, projectPath, commandFilter, cancellationToken).ConfigureAwait(false);

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

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',', StringComparison.Ordinal) ||
            value.Contains('"', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}
