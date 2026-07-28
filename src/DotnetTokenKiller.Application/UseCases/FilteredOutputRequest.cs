using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Everything <see cref="FilteredOutputPipeline"/> needs to condense one batch of output.</summary>
/// <param name="Filter">The output filter to apply.</param>
/// <param name="RawOutput">The raw, un-stripped output to condense.</param>
/// <param name="ExitCode">The producing command's exit code; the sole source of the success verdict.</param>
/// <param name="CommandSlug">The canonical name this run is tracked and tee'd under.</param>
/// <param name="DisplayCommandLine">The command line quoted in the raw-tail fallback header.</param>
/// <param name="Source">Where the raw output came from.</param>
/// <param name="Options">
/// Display flags. The pipeline calls <see cref="OutputOptions.Normalized"/> on this defensively, so
/// callers do not need to pre-normalize.
/// </param>
/// <param name="StartTimestamp">
/// A <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value taken by the caller. The caller
/// owns it because the run path must include the child process's time while the pipe path must not.
/// </param>
/// <remarks>
/// A parameter object rather than positional parameters: the eight values exceed the seven-parameter
/// limit S107 enforces, and the build treats analyzer warnings as errors.
/// </remarks>
public sealed record FilteredOutputRequest(
    IOutputFilter Filter,
    string RawOutput,
    int ExitCode,
    string CommandSlug,
    string DisplayCommandLine,
    RunSource Source,
    OutputOptions Options,
    long StartTimestamp);
