using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet test with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The test output filter.</param>
/// <param name="runnerDetector">Tells which runner the run uses, for the argument <c>--names</c> adds.</param>
internal sealed class DotnetTestCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.Test)] IOutputFilter filter,
    ITestRunnerDetector runnerDetector) : AsyncCommand<DotnetTestCommandSettings>
{
    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(CommandContext context, DotnetTestCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<string> args = [.. settings.PositionalArgs.Prepend("test").Concat(context.Remaining.Raw)];

        // Runners print passed tests' names only at a higher verbosity: ask for it, unless the user already
        // chose one or the runner cannot be told (an argument for the wrong runner breaks the run).
        if (settings.Names)
        {
            args = TestArgumentInjector.ForNames(args, runnerDetector.Detect(Environment.CurrentDirectory));
        }

        return filteredRun.RunAsync(filter, "dotnet", args, settings.ToOutputOptions(),
            settings.ToTestOptions(), cancellationToken);
    }
}
