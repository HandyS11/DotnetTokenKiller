namespace DotnetTokenKiller.Benchmarks.Corpus;

/// <summary>Generated-log size tiers. Three points are the minimum that can show a curve.</summary>
public enum CorpusTier
{
    /// <summary>About 2 KB — the size of today's fixtures, and of an interactive single-project run.</summary>
    Small = 0,

    /// <summary>About 50 KB — a realistic multi-project solution build.</summary>
    Medium = 1,

    /// <summary>About 1 MB — the scaling probe, where non-linear behaviour becomes visible.</summary>
    Large = 2
}
