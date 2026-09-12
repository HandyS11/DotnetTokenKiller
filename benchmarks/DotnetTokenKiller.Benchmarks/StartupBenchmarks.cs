using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Benchmarks.Support;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure;
using DotnetTokenKiller.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Fixed per-invocation cost paid before any output is filtered: building the service graph and
/// reading the config file. Both grow quietly as registrations and settings are added, and neither
/// is attributable to any one feature.
/// </summary>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    private HermeticState? _state;

    [GlobalSetup]
    public void Setup() => _state = HermeticState.Enter();

    [GlobalCleanup]
    public void Cleanup() => _state?.Dispose();

    [Benchmark]
    public ServiceProvider BuildServiceProvider() =>
        new ServiceCollection().AddApplication().AddInfrastructure().BuildServiceProvider();

    [Benchmark]
    public async Task<DtkConfig> LoadConfigAsync() =>
        await new JsonConfigProvider(_state!.ConfigPath).LoadAsync().ConfigureAwait(false);
}
