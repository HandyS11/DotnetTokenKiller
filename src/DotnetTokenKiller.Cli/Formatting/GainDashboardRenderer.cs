using System.Globalization;
using DotnetTokenKiller.Domain.Tracking;
using Spectre.Console;

namespace DotnetTokenKiller.Cli.Formatting;

/// <summary>Renders the rtk-style token-savings dashboard for <c>dtk gain</c>.</summary>
internal static class GainDashboardRenderer
{
    private const int MeterWidth = 24;
    private const int ImpactWidth = 10;
    private const int RuleWidth = 60;

    /// <summary>Writes the dashboard (header, recap, efficiency meter, per-command table).</summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="summary">The summary to render; must contain at least one command.</param>
    /// <param name="scope">Human-readable scope description (e.g. "Global Scope, last 7 days").</param>
    public static void Render(IAnsiConsole console, GainSummary summary, string scope)
    {
        RenderHeader(console, scope);
        RenderRecap(console, summary);
        RenderTable(console, summary);
    }

    private static void RenderHeader(IAnsiConsole console, string scope)
    {
        console.MarkupLine($"[bold cyan]DTK Token Savings ({scope.EscapeMarkup()})[/]");
        console.MarkupLine($"[grey]{new string('═', RuleWidth)}[/]");
        console.WriteLine();
    }

    private static void RenderRecap(IAnsiConsole console, GainSummary summary)
    {
        var pct = summary.AverageSavingsPercentage.ToString("F1", CultureInfo.InvariantCulture);
        var avgTime = summary.TotalCommands > 0
            ? TimeSpan.FromTicks(summary.TotalExecutionTime.Ticks / summary.TotalCommands)
            : TimeSpan.Zero;

        console.MarkupLine($"Total commands:    {summary.TotalCommands.ToString(CultureInfo.InvariantCulture)}");
        console.MarkupLine($"Without tool:      {TokenFormat.Tokens(summary.TotalInputTokens)}");
        console.MarkupLine($"Used by tool:      {TokenFormat.Tokens(summary.TotalOutputTokens)}");
        console.MarkupLine($"[green]Tokens saved:      {TokenFormat.Tokens(summary.TotalSavedTokens)} ({pct}%)[/]");
        console.MarkupLine(
            $"Total exec time:   {TokenFormat.Duration(summary.TotalExecutionTime)} (avg {TokenFormat.Duration(avgTime)})");

        var filled = (int)Math.Clamp(Math.Round(summary.AverageSavingsPercentage / 100.0 * MeterWidth), 0, MeterWidth);
        console.MarkupLine(
            $"Efficiency meter: [green]{new string('█', filled)}[/][grey]{new string('░', MeterWidth - filled)}[/] {pct}%");
        console.WriteLine();
    }

    private static void RenderTable(IAnsiConsole console, GainSummary summary)
    {
        var rows = new List<(string Label, string Color, CommandGainDetail Detail)>();
        foreach (var (cmd, detail) in summary.CommandDetails)
        {
            if (detail.SuccessDetail is { } sd)
            {
                rows.Add(($"{cmd} (ok)", "green", sd));
            }

            if (detail.FailureDetail is { } fd)
            {
                rows.Add(($"{cmd} (fail)", "red", fd));
            }
        }

        var maxSaved = rows.Count > 0 ? rows.Max(r => r.Detail.TotalSavedTokens) : 0;

        console.MarkupLine("[bold]By Command[/]");

        var table = new Table()
            .Border(TableBorder.Horizontal)
            .AddColumn("Command")
            .AddColumn(new TableColumn("Runs").RightAligned())
            .AddColumn(new TableColumn("Without Tool").RightAligned())
            .AddColumn(new TableColumn("Used by Tool").RightAligned())
            .AddColumn(new TableColumn("Saved").RightAligned())
            .AddColumn(new TableColumn("Avg%").RightAligned())
            .AddColumn("Impact");

        foreach (var (label, color, d) in rows)
        {
            table.AddRow(
                new Markup($"[{color}]{label.EscapeMarkup()}[/]"),
                new Text(d.RunCount.ToString(CultureInfo.InvariantCulture)),
                new Text(TokenFormat.Tokens(d.TotalInputTokens)),
                new Text(TokenFormat.Tokens(d.TotalOutputTokens)),
                new Markup(
                    $"[{SavedColor(d.TotalSavedTokens)}]{TokenFormat.Tokens(d.TotalSavedTokens)}[/]"),
                new Markup(
                    $"[{PctColor(d.AverageSavingsPercentage)}]{d.AverageSavingsPercentage.ToString("F1", CultureInfo.InvariantCulture)}%[/]"),
                new Markup(ImpactBar(d.TotalSavedTokens, maxSaved)));
        }

        console.Write(table);
    }

    /// <summary>Maps a saved-token delta to a color (switch expression: Sonar S3358 bans nested ternaries).</summary>
    /// <remarks>Internal (rather than private) so tests can assert the color-bucket thresholds directly;
    /// <see cref="Spectre.Console.Testing.TestConsole"/> strips ANSI sequences from its captured output,
    /// so threshold mutations would otherwise survive rendering-level tests undetected.</remarks>
    /// <param name="saved">The saved-token delta.</param>
    internal static string SavedColor(long saved) => saved switch
    {
        > 0 => "green",
        < 0 => "red",
        _ => "grey"
    };

    /// <summary>Maps a savings percentage to a color (switch expression: Sonar S3358 bans nested ternaries).</summary>
    /// <remarks>Internal (rather than private) so tests can assert the color-bucket thresholds directly;
    /// <see cref="Spectre.Console.Testing.TestConsole"/> strips ANSI sequences from its captured output,
    /// so threshold mutations would otherwise survive rendering-level tests undetected.</remarks>
    /// <param name="pct">The savings percentage.</param>
    internal static string PctColor(double pct) => pct switch
    {
        >= 80 => "green",
        >= 40 => "yellow",
        _ => "red"
    };

    private static string ImpactBar(long saved, long maxSaved)
    {
        // Positive savings always get at least one block so small rows stay visible.
        var filled = saved > 0 && maxSaved > 0
            ? (int)Math.Clamp(Math.Round((double)saved / maxSaved * ImpactWidth), 1, ImpactWidth)
            : 0;
        return $"[green]{new string('█', filled)}[/][grey]{new string('░', ImpactWidth - filled)}[/]";
    }
}
