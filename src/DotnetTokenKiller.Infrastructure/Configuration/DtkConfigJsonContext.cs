using System.Text.Json.Serialization;
using DotnetTokenKiller.Domain.Configuration;

namespace DotnetTokenKiller.Infrastructure.Configuration;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true,
    Converters = [typeof(JsonStringEnumConverter<TeeMode>), typeof(JsonStringEnumConverter<TokenizerModel>)])]
[JsonSerializable(typeof(DtkConfig))]
internal sealed partial class DtkConfigJsonContext : JsonSerializerContext;
