using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure;
using DotnetTokenKiller.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Fixed per-invocation cost paid before any output is filtered: building the service graph, and
/// reading, deserializing, merging and validating the config file. Both grow quietly as
/// registrations and settings are added, and neither is attributable to any one feature.
/// </summary>
/// <remarks>
/// <see cref="SetupAsync"/> writes a populated config before measuring. Without one,
/// <c>JsonConfigProvider.LoadAsync</c> takes its <c>!File.Exists</c> early return and this
/// benchmark measures a stat call — which would stay flat forever no matter how many settings the
/// config grew, the exact regression the class claims to watch for.
/// </remarks>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    /// <summary>
    /// Every section populated and nothing left at its default, so the parse, the merge and the
    /// validation all do their full work. The two paths are fixed literals rather than the
    /// hermetic temp paths: nothing here opens them, and a per-run path length would change the
    /// size of the JSON being deserialized from machine to machine.
    /// </summary>
    private static readonly DtkConfig RepresentativeConfig = new(
        new TrackingConfig(
            Enabled: true,
            RetentionDays: 30,
            DbPath: "/home/user/.local/share/dtk/tracking.db",
            Tokenizer: TokenizerModel.O200kBase),
        new DisplayConfig(Emoji: false),
        new TeeConfig(
            Mode: TeeMode.Always,
            Directory: "/home/user/.local/share/dtk/logs",
            MaxFiles: 50,
            MaxFileSizeBytes: 2_097_152L));

    private HermeticState? _state;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _state = HermeticState.Enter();

        // Written through the provider itself rather than as a JSON literal, so the file can never
        // drift out of the shape the provider actually parses.
        await new JsonConfigProvider(_state.ConfigPath)
            .SaveAsync(RepresentativeConfig).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();

    [Benchmark]
    public ServiceProvider BuildServiceProvider() =>
        new ServiceCollection().AddApplication().AddInfrastructure().BuildServiceProvider();

    [Benchmark]
    public async Task<DtkConfig> LoadConfigAsync() =>
        await new JsonConfigProvider(_state!.ConfigPath).LoadAsync().ConfigureAwait(false);
}
