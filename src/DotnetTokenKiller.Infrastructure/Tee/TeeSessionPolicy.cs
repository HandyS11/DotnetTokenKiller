namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>
/// The size and retention rules a <see cref="FileTeeSession"/> applies to the log it is writing.
/// </summary>
/// <remarks>
/// Grouped into one type rather than passed as separate arguments: every one of them is a policy
/// decision read from configuration, and none of them is meaningful without the others —
/// <see cref="MaxFiles"/> alone decides whether <see cref="TeeDir"/> is even consulted for rotation.
/// The session's own log is always kept: the most recent run of each kind must be readable with
/// <c>dtk log</c>, whatever its exit code or size. The retention rules below decide which
/// <em>older</em> logs of the same kind it supersedes.
/// </remarks>
/// <param name="MaxBodyBytes">The body's byte budget; appends stop once it is reached.</param>
/// <param name="MinBodyBytes">
/// Older logs of the same kind whose bodies are smaller than this (and were not truncated) are
/// discarded once this one is kept.
/// </param>
/// <param name="KeepOnlyOnFailure">
/// Whether older passing logs of the same kind are discarded once this one is kept, leaving at most
/// one passing log per kind and project: the latest run's.
/// </param>
/// <param name="TeeDir">
/// The tee directory, pruned and rotated once this log is kept. Unused (and safe to leave default)
/// when <paramref name="MaxFiles"/> disables rotation and <paramref name="Slug"/> is empty.
/// </param>
/// <param name="MaxFiles">
/// Maximum number of tee files to retain once this one is kept; non-positive disables rotation.
/// </param>
/// <param name="Slug">
/// The sanitised subcommand slug in this log's file name (its kind, as <c>dtk log build</c> selects
/// it). Empty disables pruning.
/// </param>
/// <param name="ProjectPath">
/// The working directory in this log's header. Only older logs of the same project are pruned,
/// because <c>dtk log</c> is scoped to it. Empty disables pruning.
/// </param>
public sealed record TeeSessionPolicy(
    long MaxBodyBytes,
    long MinBodyBytes,
    bool KeepOnlyOnFailure,
    string TeeDir = "",
    int MaxFiles = 0,
    string Slug = "",
    string ProjectPath = "");
