using System.Text.Json.Serialization;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Cli.Serialization;

[JsonSerializable(typeof(GainSummary))]
[JsonSerializable(typeof(CommandGainDetail))]
internal sealed partial class GainSummaryJsonContext : JsonSerializerContext;
