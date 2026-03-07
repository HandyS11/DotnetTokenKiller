using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class GainCommand : AsyncCommand<GainCommandSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, GainCommandSettings settings, CancellationToken cancellationToken)
        => Task.FromResult(0);
}
