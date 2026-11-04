using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet test with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The test output filter.</param>
internal sealed class DotnetTestCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.Test)] IOutputFilter filter) : AsyncCommand<DotnetTestCommandSettings>
{
    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(CommandContext context, DotnetTestCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var args = settings.PositionalArgs.Prepend("test").Concat(context.Remaining.Raw).ToArray();
        return filteredRun.RunAsync(filter, "dotnet", args, settings.ToOutputOptions(),
            settings.ToTestOptions(), cancellationToken);
    }
}
