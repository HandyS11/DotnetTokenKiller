using System.Globalization;
using System.Text;
using System.Text.Json;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Formatting;
using DotnetTokenKiller.Cli.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Shows token savings analytics.</summary>
/// <param name="gainReport">The gain report use case.</param>
/// <param name="console">The Spectre.Console output sink for the human-readable table.</param>
/// <param name="output">The raw text writer for machine-readable output (JSON/CSV), bypassing console width wrapping.</param>
internal sealed class GainCommand(
    GainReportUseCase gainReport,
    IAnsiConsole console,
    TextWriter output) : AsyncCommand<GainCommandSettings>
{
    internal const string CsvHeader =
        "timestamp,command,project_path,input_tokens,output_tokens,saved_tokens,savings_pct,execution_time_ms,success";

    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        GainCommandSettings settings,
        CancellationToken cancellationToken)
        => RunAsync(settings, cancellationToken);

    internal async Task<int> RunAsync(GainCommandSettings settings, CancellationToken cancellationToken)
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

            var records = await gainReport.GetHistoryAsync(settings.Days, projectPath, commandFilter, cancellationToken)
                .ConfigureAwait(false);
            var sb = new StringBuilder();
            sb.AppendLine(CsvHeader);
            foreach (var r in records)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{r.Timestamp:O},{EscapeCsv(r.Command)},{EscapeCsv(r.ProjectPath)},{r.InputTokens},{r.OutputTokens},{r.SavedTokens},{r.SavingsPercentage.ToString("F4", CultureInfo.InvariantCulture)},{r.ExecutionTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)},{(r.Success ? 1 : 0)}");
            }

            await output.WriteAsync(sb.ToString()).ConfigureAwait(false);
            return 0;
        }

        var summary = await gainReport.GetSummaryAsync(settings.Days, projectPath, commandFilter, cancellationToken)
            .ConfigureAwait(false);

        if (settings.Json)
        {
            var json = JsonSerializer.Serialize(summary, GainSummaryJsonContext.Default.GainSummary);
            await output.WriteLineAsync(json).ConfigureAwait(false);
            return 0;
        }

        if (summary.TotalCommands == 0)
        {
            console.MarkupLine(
                "[grey]No data yet. Run some [bold]dtk dotnet[/] commands to start tracking savings.[/]");
            return 0;
        }

        GainDashboardRenderer.Render(console, summary, BuildScope(settings));
        return 0;
    }

    private static string BuildScope(GainCommandSettings settings)
    {
        var scope = settings.Project ? "Project Scope" : "Global Scope";
        if (settings.Days != 30)
        {
            scope += string.Create(CultureInfo.InvariantCulture, $", last {settings.Days} days");
        }

        if (settings.Command is not null)
        {
            scope += $", command: {settings.Command}";
        }

        return scope;
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
