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
}
