using DotnetTokenKiller.Domain.Tracking;
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Cli.Serialization;

[JsonSerializable(typeof(GainSummary))]
[JsonSerializable(typeof(CommandGainDetail))]
internal sealed partial class GainSummaryJsonContext : JsonSerializerContext;
