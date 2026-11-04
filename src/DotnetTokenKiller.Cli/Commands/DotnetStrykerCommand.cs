using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet stryker with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The Stryker output filter.</param>
internal sealed class DotnetStrykerCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.Stryker)]
    IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var args = settings.PositionalArgs
            .Prepend("stryker")
            .Concat(context.Remaining.Raw)
            .ToArray();

        return filteredRun.RunAsync(filter, "dotnet", args, settings.ToOutputOptions(), cancellationToken: cancellationToken);
    }
}
