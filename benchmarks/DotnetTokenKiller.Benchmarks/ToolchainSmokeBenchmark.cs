using BenchmarkDotNet.Attributes;

namespace DotnetTokenKiller.Benchmarks;

[MemoryDiagnoser]
public class ToolchainSmokeBenchmark
{
    [Params(16, 256)]
    public int Length { get; set; }

    [Benchmark]
    public string AllocateString() => new('x', Length);
}
