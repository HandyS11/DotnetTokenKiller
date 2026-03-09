using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Execution;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetPublishCommand(ICommandRunner commandRunner) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("publish").Concat(context.Remaining.Raw).ToArray();
        return await commandRunner.RunPassthroughAsync("dotnet", args, cancellationToken);
    }
}
