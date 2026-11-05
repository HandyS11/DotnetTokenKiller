namespace DotnetTokenKiller.Domain.Files;

/// <summary>The files a working tree changed, or the reason that could not be computed.</summary>
/// <param name="ChangedPaths">The changed paths; empty when nothing changed or when not computed.</param>
/// <param name="NotComputedReason">Why the set could not be computed, or <see langword="null"/> when it was.</param>
public sealed record FileChangeSet(IReadOnlyList<string> ChangedPaths, string? NotComputedReason = null)
{
    /// <summary>Whether <see cref="ChangedPaths"/> is a real answer.</summary>
    public bool Computed => NotComputedReason is null;

    /// <summary>Creates a set that records why the changes could not be computed.</summary>
    /// <param name="reason">The reason.</param>
    public static FileChangeSet NotComputed(string reason) => new([], reason);
}
