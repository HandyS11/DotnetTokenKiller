using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet clean with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The clean output filter.</param>
internal sealed class DotnetCleanCommand(
    FilteredRunUseCase filteredRun,
    DotnetCleanFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var args = settings.PositionalArgs.Prepend("clean").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, settings.ShowLog, settings.Quiet, cancellationToken).ConfigureAwait(false);
    }
}
