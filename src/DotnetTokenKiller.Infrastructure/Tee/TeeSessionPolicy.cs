namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>
/// The size and retention rules a <see cref="FileTeeSession"/> applies to the log it is writing.
/// </summary>
/// <remarks>
/// Grouped into one type rather than passed as five separate arguments: every one of them is a
/// policy decision read from configuration, and none of them is meaningful without the others —
/// <see cref="MaxFiles"/> alone decides whether <see cref="TeeDir"/> is even consulted.
/// </remarks>
/// <param name="MaxBodyBytes">The body's byte budget; appends stop once it is reached.</param>
/// <param name="MinBodyBytes">Bodies smaller than this are discarded when the run completes.</param>
/// <param name="KeepOnlyOnFailure">Whether a successful run's log is discarded.</param>
/// <param name="TeeDir">
/// The tee directory, rotated once the log's fate (kept or discarded) is decided. Unused (and safe
/// to leave default) when <paramref name="MaxFiles"/> disables rotation.
/// </param>
/// <param name="MaxFiles">
/// Maximum number of tee files to retain once this one is kept or discarded; non-positive disables
/// rotation.
/// </param>
public sealed record TeeSessionPolicy(
    long MaxBodyBytes,
    long MinBodyBytes,
    bool KeepOnlyOnFailure,
    string TeeDir = "",
    int MaxFiles = 0);
