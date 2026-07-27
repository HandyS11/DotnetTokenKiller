using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Infrastructure.Configuration;
using DotnetTokenKiller.Infrastructure.Execution;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Runs a dotnet subcommand dtk does not filter, without building the DI container.
/// </summary>
/// <remarks>
/// This path deliberately skips Spectre and the service container, which together account for
/// most of dtk's startup cost. It pays only for one config file read and, when tracking is on,
/// one SQLite connection opened after the child has already exited.
/// </remarks>
internal static class PassthroughEntryPoint
{
    /// <summary>Runs the command and records the run.</summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="dotnetArgs">The arguments to pass to it, starting at the subcommand.</param>
    /// <returns>The child process exit code.</returns>
    internal static async Task<int> RunAsync(string command, IReadOnlyList<string> dotnetArgs)
    {
        var config = await new JsonConfigProvider().LoadAsync().ConfigureAwait(false);
        var runner = new ProcessCommandRunner();

        if (!config.Tracking.Enabled)
        {
            // Nothing to record, so never open the database at all.
            return await runner.RunPassthroughAsync(command, dotnetArgs).ConfigureAwait(false);
        }

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var tracker = TrackerFactory.Create(config);
#pragma warning restore CA2007
        var useCase = new PassthroughRunUseCase(runner, tracker, Console.Out, Console.Error);
        return await useCase.RunAsync(config, command, dotnetArgs).ConfigureAwait(false);
    }
}
