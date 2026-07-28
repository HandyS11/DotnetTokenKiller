using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet list package with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The list package output filter.</param>
internal sealed class DotnetListPackageCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.ListPackage)]
    IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        // Both tokens are prepended: the child process must receive `dotnet list package ...`, and
        // FilteredRunUseCase derives the tracking slug by matching this same argument list.
        var args = settings.PositionalArgs
            .Prepend("package")
            .Prepend("list")
            .Concat(context.Remaining.Raw)
            .ToArray();

        return await filteredRun.RunAsync(filter, "dotnet", args, settings.VerbosityLevel, settings.ShowLog,
            settings.Quiet, cancellationToken).ConfigureAwait(false);
    }
}
