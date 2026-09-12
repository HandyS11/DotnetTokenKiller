using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Tracking setup started when a run starts, so it runs while the child process (or the stdin read)
/// does instead of after it.
/// </summary>
/// <remarks>
/// This only moves work earlier. Every exception is discarded here, and the same failure then
/// resurfaces where the run is recorded: <see cref="TokenEstimator.Estimate"/> rethrows a failed
/// load and <see cref="ITracker.RecordAsync"/> retries a failed setup, both inside the catch that
/// already keeps tracking failures away from the user.
/// </remarks>
public sealed class TrackingWarmUp
{
    private readonly Task _ready;

    private TrackingWarmUp(Task ready)
    {
        _ready = ready;
    }

    /// <summary>A warm-up with nothing to do, for runs that are not tracked.</summary>
    public static TrackingWarmUp None { get; } = new(Task.CompletedTask);

    /// <summary>Starts the tracker's setup and, when requested, the tokenizer load, each on the thread pool.</summary>
    /// <param name="tracker">The tracker to warm up.</param>
    /// <param name="tokenizer">
    /// The tokenizer to load, or <see langword="null"/> when the run will not count tokens.
    /// </param>
    /// <param name="cancellationToken">Passed to the tracker's setup.</param>
    /// <returns>A handle whose <see cref="WhenReadyAsync"/> completes once both have finished.</returns>
    public static TrackingWarmUp Start(ITracker tracker, TokenizerModel? tokenizer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        // Task.Run rather than a direct call: SQLite setup begins with a synchronous native library
        // load that would otherwise run on the caller's thread before the first await. The tasks
        // themselves get CancellationToken.None so a cancelled run yields a completed task, never a
        // cancelled one that WhenReadyAsync would rethrow.
        var trackerSetup = Task.Run(async () =>
        {
            try
            {
                await tracker.WarmUpAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Intentional: RecordAsync retries a failed setup inside the tracking catch
            }
        }, CancellationToken.None);

        var tokenizerLoad = tokenizer is { } model
            ? Task.Run(() =>
            {
                try
                {
                    TokenEstimator.WarmUp(model);
                }
                catch
                {
                    // Intentional: Estimate rethrows a failed load inside the tracking catch
                }
            }, CancellationToken.None)
            : Task.CompletedTask;

        return new TrackingWarmUp(Task.WhenAll(trackerSetup, tokenizerLoad));
    }

    /// <summary>Completes when the started setup has finished, whether or not it succeeded. Never throws.</summary>
    /// <returns>A task that always completes successfully.</returns>
    public Task WhenReadyAsync()
    {
        // _ready is always non-null (both factories above pass a real Task); the `?? Task.CompletedTask`
        // is never actually taken. It exists so the returned expression is not a bare field read, which
        // VSTHRD003 flags as "awaiting a foreign Task" regardless of what value the field holds.
        return _ready ?? Task.CompletedTask;
    }
}
