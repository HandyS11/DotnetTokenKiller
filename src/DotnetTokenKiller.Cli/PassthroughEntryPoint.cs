using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Infrastructure.Configuration;
using DotnetTokenKiller.Infrastructure.Execution;
using DotnetTokenKiller.Infrastructure.Tee;
using DotnetTokenKiller.Infrastructure.Tracking;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Runs a dotnet subcommand dtk does not filter, without building the DI container.
/// </summary>
/// <remarks>
/// This path deliberately skips Spectre and the service container, which together account for
/// most of dtk's startup cost. When tee is enabled the config file is read twice — once here and
/// again inside <see cref="FileTeeService.BeginAsync"/>, which does not cache — and the log file is opened
/// before the child starts, so a killed dtk still leaves a log behind. Tracking, when enabled, sets up
/// its SQLite connection (and, for a measured run, loads the tokenizer) on the thread pool while the
/// child runs, and records once the child has exited.
/// </remarks>
internal static class PassthroughEntryPoint
{
    /// <summary>Runs the command and records the run.</summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="dotnetArgs">The arguments to pass to it, starting at the subcommand.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C or SIGTERM; the child's tree is then killed.</param>
    /// <returns>The child process exit code, or <see cref="ExitCodes.Cancelled"/> when the run was cancelled.</returns>
    internal static async Task<int> RunAsync(
        string command, IReadOnlyList<string> dotnetArgs, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunCoreAsync(command, dotnetArgs, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Same code Spectre returns for a cancelled filtered command.
            return ExitCodes.Cancelled;
        }
    }

    private static async Task<int> RunCoreAsync(
        string command, IReadOnlyList<string> dotnetArgs, CancellationToken cancellationToken)
    {
        var configProvider = new JsonConfigProvider();
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
        var runner = new ProcessCommandRunner();

        if (!config.Tracking.Enabled && config.Tee.Mode == TeeMode.Never)
        {
            // Nothing to record and nothing to log, so open neither the database nor a log file.
            return await runner.RunPassthroughAsync(command, dotnetArgs, cancellationToken).ConfigureAwait(false);
        }

        var teeService = new FileTeeService(configProvider);

        if (!config.Tracking.Enabled)
        {
            // Tee on, tracking off: write the log, but still never open the database.
            var teeOnly = new PassthroughRunUseCase(
                runner, tracker: null, teeService, Console.Out, Console.Error);
            return await teeOnly.RunAsync(config, command, dotnetArgs, cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable CA2007 // await using disposal does not support ConfigureAwait
        await using var tracker = TrackerFactory.Create(config);
#pragma warning restore CA2007
        var useCase = new PassthroughRunUseCase(runner, tracker, teeService, Console.Out, Console.Error);
        return await useCase.RunAsync(config, command, dotnetArgs, cancellationToken).ConfigureAwait(false);
    }
}
