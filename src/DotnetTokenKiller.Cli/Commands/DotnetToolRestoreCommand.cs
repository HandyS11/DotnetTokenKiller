using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet tool restore with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The tool restore output filter.</param>
internal sealed class DotnetToolRestoreCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.ToolRestore)]
    IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        // Both tokens are prepended: the child process must receive `dotnet tool restore ...`, and
        // FilteredRunUseCase derives the tracking slug by matching this same argument list.
        var args = settings.PositionalArgs
            .Prepend("restore")
            .Prepend("tool")
            .Concat(context.Remaining.Raw)
            .ToArray();

        return filteredRun.RunAsync(filter, "dotnet", args, settings.ToOutputOptions(), cancellationToken: cancellationToken);
    }
}
