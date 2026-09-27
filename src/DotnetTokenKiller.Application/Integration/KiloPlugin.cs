namespace DotnetTokenKiller.Application.Integration;

/// <summary>The plugin <c>dtk init kilo</c> generates.</summary>
/// <remarks>
/// Kilo Code is an OpenCode fork whose loader still accepts OpenCode's named function exports and fires
/// <c>tool.execute.before</c> for its <c>bash</c> tool before the tool's own permission check, so the plugin is
/// <see cref="OpenCodePlugin"/>'s body with Kilo's provider name; see there for why each line is there.
/// </remarks>
internal static class KiloPlugin
{
    /// <summary>The call a plugin still runs dtk through, even after local edits.</summary>
    internal static readonly string InvocationSignature = PluginRuntime.InvocationSignature("kilo");

    /// <summary>The plugin source, before stamping.</summary>
    internal static readonly string Body = OpenCodePlugin.BodyFor("kilo", "Kilo Code");

    /// <summary>The plugin as a generated artifact at <paramref name="path"/>.</summary>
    /// <param name="path">Where the plugin is written.</param>
    internal static GeneratedArtifact Artifact(string path) =>
        // No dtk ever wrote an unstamped copy; the stamp prefix as legacy signature keeps the legacy branch
        // unreachable, exactly as for OpenCodePlugin.Artifact.
        new(path, Body, StampStyle.SlashComment, ArtifactStamping.StampPrefix);
}
