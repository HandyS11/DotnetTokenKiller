using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the plugin <c>dtk init opencode</c> generates under Node, as OpenCode's plugin host would call it. Test
/// bodies live in <see cref="ExportedPluginTestsBase"/>; this class supplies OpenCode's layout: the plugin under
/// <c>.opencode/plugins/</c>, beside the <c>package.json</c> OpenCode writes with no <c>"type"</c>, so Node loads
/// the plugin as ESM by syntax detection (warning on stderr), as users get it.
/// </summary>
public sealed class OpenCodePluginTests : ExportedPluginTestsBase
{
    protected override string PluginBody => OpenCodePlugin.Body;

    protected override string PluginRelativePath => ".opencode/plugins/dtk.js";

    protected override void WriteExtraFiles(string dir) =>
        File.WriteAllText(Path.Combine(dir, ".opencode", "package.json"), """{"dependencies":{"@opencode-ai/plugin":"1.18.31"}}""");
}
