using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavingsBaseline))]
internal sealed partial class SavingsBaselineJsonContext : JsonSerializerContext;
