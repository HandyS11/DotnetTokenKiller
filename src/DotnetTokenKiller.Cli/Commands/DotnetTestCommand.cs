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
    [FromKeyedServices(FilterKeys.Test)] IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var args = settings.PositionalArgs.Prepend("test").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, settings.ShowLog,
            settings.Quiet, cancellationToken).ConfigureAwait(false);
    }
}
