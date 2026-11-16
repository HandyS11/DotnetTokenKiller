using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Domain.Filters;

/// <summary>What a contextual filter may know about the run beyond its output.</summary>
public sealed record FilterContext
{
    /// <summary>A context with every value at its default, for callers that know nothing about the run.</summary>
    public static FilterContext Unknown { get; } = new();

    /// <summary>Where the output came from.</summary>
    public RunSource Source { get; init; } = RunSource.Pipe;

    /// <summary>The child's arguments after the subcommand's tokens.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The working directory of the run, when known.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>When the run started, when known.</summary>
    public DateTimeOffset? StartedUtc { get; init; }

    /// <summary>The test display options.</summary>
    public TestOptions Test { get; init; } = TestOptions.None;

    /// <summary>The files the run changed, when computed.</summary>
    public FileChangeSet? Changes { get; init; }

    /// <summary>
    /// The files the work in progress changed, worked out the first time a filter reads it (it asks git); its value
    /// is <see langword="null"/> outside a git work tree, and the property is <see langword="null"/> under
    /// <c>dtk pipe</c>, where the output may come from anywhere.
    /// </summary>
    public Lazy<WorkTreeChanges?>? WorkTreeChanges { get; init; }
}
