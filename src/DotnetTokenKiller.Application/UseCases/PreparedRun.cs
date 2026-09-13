using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>What <see cref="FilteredOutputPipeline.BeginAsync"/> set up when a run started.</summary>
/// <param name="Config">The configuration loaded for this run and used for the rest of it.</param>
/// <param name="WarmUp">The tracking setup started for this run, or <see cref="TrackingWarmUp.None"/>.</param>
public sealed record PreparedRun(DtkConfig Config, TrackingWarmUp WarmUp);
