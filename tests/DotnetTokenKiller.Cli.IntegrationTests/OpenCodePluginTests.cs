using System.Diagnostics;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the plugin <c>dtk init opencode</c> generates under Node, as OpenCode's plugin host would call it: the exported
/// factory, then <c>tool.execute.before</c> with an args object, against a real or fake <c>dtk</c> on <c>PATH</c>.
/// </summary>
public sealed class OpenCodePluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-ocplugin-{Guid.NewGuid()}");
    private readonly NodeDriver _node;

    public OpenCodePluginTests()
    {
        _node = new NodeDriver(_dir);
        // The layout OpenCode produces: the plugin under .opencode/plugins/, beside the package.json OpenCode writes with
        // no "type", so Node loads the plugin as ESM by syntax detection (warning on stderr), as users get it.
        var plugins = Directory.CreateDirectory(Path.Combine(_dir, ".opencode", "plugins")).FullName;
        File.WriteAllText(Path.Combine(plugins, "dtk.js"), ArtifactStamping.Apply(OpenCodePlugin.Body, StampStyle.SlashComment));
        File.WriteAllText(Path.Combine(_dir, ".opencode", "package.json"), """{"dependencies":{"@opencode-ai/plugin":"1.18.31"}}""");
        File.WriteAllText(Path.Combine(_dir, "driver.mjs"), """
            import { DtkPlugin } from "./.opencode/plugins/dtk.js";
            const [tool, command] = process.argv.slice(2);
            const hooks = await DtkPlugin({});
            const output = { args: { command, workdir: "w" } };
            const args = output.args;
            await hooks["tool.execute.before"]({ tool, sessionID: "s", callID: "c" }, output);
            process.stdout.write(JSON.stringify({ command: output.args.command, sameObject: output.args === args, workdir: output.args.workdir }));
            """);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>The directory holding the dtk under test: the installed binary's, or the JIT build's apphost beside the tests.</summary>
    private static string RealDtkDirectory
    {
        get
        {
            var binary = Environment.GetEnvironmentVariable(DtkLauncher.TestBinaryVariable);
            return string.IsNullOrWhiteSpace(binary) ? AppContext.BaseDirectory : Path.GetDirectoryName(binary.Trim())!;
        }
    }

    [NodeFact]
    public async Task Bash_DotnetCommand_IsRewrittenInPlaceByTheRealDtkAsync()
    {
        var result = await _node.RunAsync("bash", "dotnet build # répertoire", RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire");
        result["sameObject"]!.GetValue<bool>().Should().BeTrue("OpenCode passes the same args object on to the tool");
        result["workdir"]!.GetValue<string>().Should().Be("w");
    }

    [NodeFact]
    public async Task DtkMissingFromPath_LeavesTheCommandAndDoesNotThrowAsync()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await _node.RunAsync("bash", "dotnet build", empty);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
    }

    [NodeFact]
    public async Task DtkOnlyInTheWorkingDirectory_IsNeverRunAsync()
    {
        // OpenCode's working directory is the project, and the plugin runs before OpenCode's permission check, so a
        // dtk committed to a repository must not run. Given a bare name, spawn on Windows searches the current
        // directory before PATH, and on Unix an empty PATH entry means the current directory; the plugin must look
        // on PATH alone. The working directory here holds a runnable dtk that would rewrite the command.
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await _node.RunAsync("bash", "dotnet build", string.Join(Path.PathSeparator, empty, string.Empty), RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
    }

    [NodeUnixFact]
    public async Task OtherToolOrNoDotnet_NeverStartsDtkAsync()
    {
        var bin = _node.FakeDtk("touch \"$(dirname \"$0\")/started\"; cat >/dev/null");

        (await _node.RunAsync("read", "dotnet build", bin))["command"]!.GetValue<string>().Should().Be("dotnet build");
        (await _node.RunAsync("bash", "ls -la", bin))["command"]!.GetValue<string>().Should().Be("ls -la");
        File.Exists(Path.Combine(bin, "started")).Should().BeFalse();
    }

    [NodeUnixFact]
    public async Task MalformedReply_LeavesTheCommandAsync()
    {
        var bin = _node.FakeDtk("cat >/dev/null; echo 'not json'");

        (await _node.RunAsync("bash", "dotnet test", bin))["command"]!.GetValue<string>().Should().Be("dotnet test");
    }

    [NodeUnixFact]
    public async Task HangingDtk_TimesOutAndLeavesTheCommandAsync()
    {
        var bin = _node.FakeDtk("exec sleep 60");
        var stopwatch = Stopwatch.StartNew();

        var result = await _node.RunAsync("bash", "dotnet test", bin);

        result["command"]!.GetValue<string>().Should().Be("dotnet test");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the plugin gives dtk 5 seconds");
    }
}
