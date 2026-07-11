using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet build with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The build output filter.</param>
internal sealed class DotnetBuildCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.Build)] IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    protected override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var args = settings.PositionalArgs.Prepend("build").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.VerbosityLevel, settings.ShowLog,
            settings.Quiet, cancellationToken).ConfigureAwait(false);
    }
}
