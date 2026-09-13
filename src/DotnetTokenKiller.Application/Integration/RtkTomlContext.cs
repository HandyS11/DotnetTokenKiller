using Tomlyn.Model;
using Tomlyn.Serialization;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Source-generated Tomlyn metadata for reading rtk's config as an untyped <see cref="TomlTable"/>, so
/// parsing needs no reflection and stays safe under trimming and Native AOT.
/// </summary>
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class RtkTomlContext : TomlSerializerContext;
