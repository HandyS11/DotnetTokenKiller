using DotnetTokenKiller.Domain.Tracking;
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Cli.Serialization;

[JsonSerializable(typeof(GainSummary))]
internal sealed partial class GainSummaryJsonContext : JsonSerializerContext;
