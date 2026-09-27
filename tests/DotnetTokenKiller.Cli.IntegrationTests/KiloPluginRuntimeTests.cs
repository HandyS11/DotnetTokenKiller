using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the plugin <c>dtk init kilo</c> generates under Node, as Kilo Code's plugin host would call it. Test bodies
/// live in <see cref="ExportedPluginTestsBase"/>; this class supplies Kilo's layout: the plugin under
/// <c>.kilo/plugin/</c>, with no <c>package.json</c> written beside it.
/// </summary>
public sealed class KiloPluginRuntimeTests : ExportedPluginTestsBase
{
    protected override string PluginBody => KiloPlugin.Body;

    protected override string PluginRelativePath => ".kilo/plugin/dtk.js";
}
