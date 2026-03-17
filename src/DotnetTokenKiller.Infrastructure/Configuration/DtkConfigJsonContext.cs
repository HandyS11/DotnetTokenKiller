using DotnetTokenKiller.Domain.Configuration;
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Infrastructure.Configuration;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true,
    Converters = [typeof(JsonStringEnumConverter<TeeMode>)])]
[JsonSerializable(typeof(DtkConfig))]
internal sealed partial class DtkConfigJsonContext : JsonSerializerContext;
