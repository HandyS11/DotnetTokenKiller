using System.Globalization;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Tee;
using Spectre.Console;

namespace DotnetTokenKiller.Cli.Formatting;

/// <summary>Renders tee log listings and views.</summary>
internal static class TeeLogRenderer
{
    /// <summary>Renders the index of matching logs.</summary>
    /// <param name="console">The console to render the table to.</param>
    /// <param name="selection">The matches to list.</param>
    /// <param name="showProject">Whether to include the originating project column.</param>
    public static void RenderList(IAnsiConsole console, LogSelection selection, bool showProject)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("#");
        table.AddColumn("When (UTC)");
        table.AddColumn("Command");
        table.AddColumn("Exit");
        table.AddColumn("Size");
        if (showProject)
        {
            table.AddColumn("Project");
        }

        for (var i = 0; i < selection.Matches.Count; i++)
        {
            var entry = selection.Matches[i];
            var cells = new List<string>
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                (entry.Header?.CommandLine ?? $"dotnet {entry.Slug}").EscapeMarkup(),
                entry.Header is null
                    ? "?"
                    : entry.Header.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "incomplete",
                FormatSize(entry.SizeBytes)
            };

            if (showProject)
            {
                cells.Add((entry.Header?.ProjectPath ?? "unknown").EscapeMarkup());
            }

            table.AddRow([.. cells]);
        }

        console.Write(table);
    }

    /// <summary>Renders one log's header and windowed body.</summary>
    /// <param name="output">
    /// The raw writer the view goes to. Deliberately not the Spectre console: build output must not
    /// be re-wrapped to the console width, which would corrupt the very detail being retrieved.
    /// </param>
    /// <param name="view">The view to render.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RenderViewAsync(
        TextWriter output,
        LogView view,
        CancellationToken cancellationToken = default)
    {
        var entry = view.Entry;
        var command = entry.Header?.CommandLine ?? $"dotnet {entry.Slug}";
        var exit = entry.Header switch
        {
            null => "exit unknown",
            { ExitCode: { } code } => $"exit {code.ToString(CultureInfo.InvariantCulture)}",
            _ => "incomplete"
        };
        var when = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture);

        await output.WriteLineAsync($"{command} — {exit} — {when}".AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"{entry.FilePath} ({FormatSize(entry.SizeBytes)}, {view.TotalLines.ToString(CultureInfo.InvariantCulture)} lines)"
                    .AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        if (entry.Header?.Status == TeeLogStatus.Running)
        {
            await output.WriteLineAsync(
                    "run did not finish — output ends where dtk was killed".AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        var summary = view.ShownLines >= view.TotalLines
            ? $"showing all {view.TotalLines.ToString(CultureInfo.InvariantCulture)} lines"
            : $"showing last {view.ShownLines.ToString(CultureInfo.InvariantCulture)} of "
              + $"{view.TotalLines.ToString(CultureInfo.InvariantCulture)} lines — --lines N or --full for more";
        await output.WriteLineAsync(summary.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync("---".AsMemory(), cancellationToken).ConfigureAwait(false);

        if (view.Body.Length > 0)
        {
            await output.WriteLineAsync(view.Body.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : $"{(bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} KB";
}
