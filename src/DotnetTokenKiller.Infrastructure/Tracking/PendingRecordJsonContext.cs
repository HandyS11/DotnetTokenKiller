using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Infrastructure.Tracking;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PendingRecord))]
internal sealed partial class PendingRecordJsonContext : JsonSerializerContext;
