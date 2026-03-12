using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetPassthroughCommand(
    PassthroughRunUseCase passthroughRun) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Concat(context.Remaining.Raw).ToArray();
        return await passthroughRun.RunAsync("dotnet", args, cancellationToken);
    }
}
