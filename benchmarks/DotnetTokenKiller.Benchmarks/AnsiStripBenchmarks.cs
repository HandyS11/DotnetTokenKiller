using BenchmarkDotNet.Attributes;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// ANSI stripping, with and without escape sequences present. The clean case is the interesting
/// one: piped dotnet output usually has no escapes at all, and it runs through three regex
/// replacements regardless. If the clean case costs the same as the decorated one, a fast path is
/// available for free on the common input.
/// </summary>
[MemoryDiagnoser]
public class AnsiStripBenchmarks
{
    private const char Escape = '';

    private string _clean = string.Empty;
    private string _decorated = string.Empty;

    [Params(CorpusTier.Small, CorpusTier.Medium, CorpusTier.Large)]
    public CorpusTier Tier { get; set; } = CorpusTier.Small;

    [GlobalSetup]
    public void Setup()
    {
        _clean = LogCorpusGenerator.Generate(FilterKeys.Build, Tier);
        _decorated = Decorate(_clean);
    }

    [Benchmark(Baseline = true)]
    public string StripCleanInput() => AnsiStrip.Strip(_clean);

    [Benchmark]
    public string StripDecoratedInput() => AnsiStrip.Strip(_decorated);

    /// <summary>Wraps every line in a colour sequence, as a TTY-attached dotnet run would.</summary>
    /// <param name="text">The lines to decorate.</param>
    private static string Decorate(string text) => string.Join(
        '\n',
        text.Split('\n').Select(line => $"{Escape}[32m{line}{Escape}[0m"));
}
