using DotnetTokenKiller.Domain.Configuration;
using System.Text.Json.Serialization;

namespace DotnetTokenKiller.Infrastructure.Configuration;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DtkConfig))]
internal sealed partial class DtkConfigJsonContext : JsonSerializerContext;
