using System.Runtime.InteropServices;

namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>
/// Turns Ctrl+C (SIGINT) and SIGTERM into cancellation of the dotnet command dtk is wrapping, so dtk
/// outlives the signal long enough to stop the child, finalize its log and report an exit code.
/// </summary>
/// <remarks>
/// <para>
/// Only the first signal is absorbed; a second one of either kind takes its default action, so dtk
/// dies at once. SIGTERM cancels at once. Ctrl+C cancels only after <see cref="InterruptGracePeriod"/>,
/// because a terminal delivers Ctrl+C to the whole foreground process group (and Windows to every
/// process on the console), so the child usually receives it too: dotnet then stops on its own, dtk
/// drains the rest of its output and records the run with the child's own exit code. The grace period
/// is the backstop for a signal sent to dtk alone, or a child that ignores it: when it elapses, the
/// token cancels and <c>ProcessCommandRunner</c> kills the child's process tree.
/// </para>
/// <para>
/// <see cref="PosixSignalRegistration"/> rather than <see cref="Console.CancelKeyPress"/> for SIGINT
/// too: it maps to Ctrl+C on Windows as well, and it does not initialize the console, which
/// <see cref="Console.CancelKeyPress"/> does on Unix.
/// </para>
/// </remarks>
internal sealed class RunCancellation : IDisposable
{
    /// <summary>How long after Ctrl+C the child has to exit on its own before its tree is killed.</summary>
    internal static readonly TimeSpan InterruptGracePeriod = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cancellation = new();
    private readonly TimeSpan _interruptGrace;
    private readonly List<PosixSignalRegistration> _registrations = [];
    private int _signalCount;

    /// <summary>Creates a cancellation source that no signal reaches until <see cref="Register"/> wires one.</summary>
    /// <param name="interruptGrace">How long after the first Ctrl+C the token cancels.</param>
    internal RunCancellation(TimeSpan interruptGrace)
    {
        _interruptGrace = interruptGrace;
    }

    /// <summary>Cancelled once a signal asks the wrapped command to stop.</summary>
    internal CancellationToken Token => _cancellation.Token;

    /// <summary>Creates a cancellation source wired to SIGINT and SIGTERM for the life of the run.</summary>
    /// <returns>The source; disposing it restores the default signal handling.</returns>
    internal static RunCancellation Register()
    {
        var cancellation = new RunCancellation(InterruptGracePeriod);
        cancellation.TryAdd(PosixSignal.SIGINT);
        cancellation.TryAdd(PosixSignal.SIGTERM);
        return cancellation;
    }

    /// <summary>Records a signal and schedules or triggers cancellation for the first one.</summary>
    /// <param name="signal">The signal received.</param>
    /// <returns>
    /// <see langword="true"/> when the signal was absorbed (the first one); <see langword="false"/>
    /// when its default action, terminating dtk, should run.
    /// </returns>
    internal bool OnSignal(PosixSignal signal)
    {
        if (Interlocked.Increment(ref _signalCount) > 1)
        {
            return false;
        }

        try
        {
            if (signal == PosixSignal.SIGINT)
            {
                _cancellation.CancelAfter(_interruptGrace);
            }
            else
            {
                // Not Cancel(): its callbacks (the tree kill among them) would run on the signal
                // handler's thread, holding up delivery of a second signal until they finished.
                _ = _cancellation.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // The run already finished and disposed the source; the signal arrived during teardown.
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _cancellation.Dispose();
    }

    /// <summary>Installs the handler for <paramref name="signal"/>, or leaves its default in place if that fails.</summary>
    /// <param name="signal">The signal to handle.</param>
    private void TryAdd(PosixSignal signal)
    {
        try
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, Handle));
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            // The signal keeps its default action, as it had before dtk handled any; failing to take
            // it over is no reason to refuse to run the command.
        }
    }

    private void Handle(PosixSignalContext context) => context.Cancel = OnSignal(context.Signal);
}
