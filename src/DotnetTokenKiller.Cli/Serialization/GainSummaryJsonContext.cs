using System.Text.Json.Serialization;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Cli.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(GainSummary))]
[JsonSerializable(typeof(CommandGainDetail))]
[JsonSerializable(typeof(CoverageSummary))]
[JsonSerializable(typeof(CoverageDetail))]
internal sealed partial class GainSummaryJsonContext : JsonSerializerContext;
