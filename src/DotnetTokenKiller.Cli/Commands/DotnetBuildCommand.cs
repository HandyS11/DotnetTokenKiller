using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetBuildCommand(
    FilteredRunUseCase filteredRun,
    DotnetBuildFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("build").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
