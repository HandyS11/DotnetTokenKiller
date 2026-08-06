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
        "timestamp,command,project_path,input_tokens,output_tokens,saved_tokens,savings_pct,execution_time_ms,success,outcome,source";

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
            return await ExportAsync(settings, projectPath, commandFilter, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Coverage)
        {
            return await RenderCoverageAsync(settings, projectPath, commandFilter, cancellationToken)
                .ConfigureAwait(false);
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

    /// <summary>Writes the history as CSV to the raw writer.</summary>
    /// <param name="settings">The parsed settings; <c>Export</c> is known to be non-null.</param>
    /// <param name="projectPath">The project to scope to, or <see langword="null"/> for every project.</param>
    /// <param name="commandFilter">The subcommand to scope to, or <see langword="null"/> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>0 on success, 1 when the requested format is not one dtk exports.</returns>
    private async Task<int> ExportAsync(
        GainCommandSettings settings,
        string? projectPath,
        string? commandFilter,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(settings.Export, "csv", StringComparison.OrdinalIgnoreCase))
        {
            console.MarkupLine($"[red]Unknown export format:[/] {settings.Export!.EscapeMarkup()}. Supported: csv");
            return 1;
        }

        var records = await gainReport.GetHistoryAsync(settings.Days, projectPath, commandFilter, cancellationToken)
            .ConfigureAwait(false);
        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        foreach (var r in records)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{r.Timestamp:O},{EscapeCsv(r.Command)},{EscapeCsv(r.ProjectPath)},{r.InputTokens},{r.OutputTokens},{r.SavedTokens},{r.SavingsPercentage.ToString("F4", CultureInfo.InvariantCulture)},{r.ExecutionTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)},{(r.Success ? 1 : 0)},{r.Outcome},{r.Source}");
        }

        await output.WriteAsync(sb.ToString()).ConfigureAwait(false);
        return 0;
    }

    /// <summary>Writes the coverage report, as JSON or as the human-readable dashboard.</summary>
    /// <param name="settings">The parsed settings.</param>
    /// <param name="projectPath">The project to scope to, or <see langword="null"/> for every project.</param>
    /// <param name="commandFilter">The subcommand to scope to, or <see langword="null"/> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Always 0: an empty report is a legitimate answer, not a failure.</returns>
    private async Task<int> RenderCoverageAsync(
        GainCommandSettings settings,
        string? projectPath,
        string? commandFilter,
        CancellationToken cancellationToken)
    {
        var coverage = await gainReport.GetCoverageAsync(settings.Days, projectPath, commandFilter, cancellationToken)
            .ConfigureAwait(false);

        if (settings.Json)
        {
            var coverageJson = JsonSerializer.Serialize(coverage, GainSummaryJsonContext.Default.CoverageSummary);
            await output.WriteLineAsync(coverageJson).ConfigureAwait(false);
            return 0;
        }

        if (coverage.TotalRuns == 0)
        {
            console.MarkupLine(
                "[grey]No data yet. Run some [bold]dtk dotnet[/] commands to start tracking coverage.[/]");
            return 0;
        }

        GainDashboardRenderer.RenderCoverage(console, coverage, BuildScope(settings));
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
