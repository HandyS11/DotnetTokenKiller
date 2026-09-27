using System.Diagnostics;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>
/// Runs an OpenCode-shaped plugin under Node, as that harness's plugin host would call it: the exported factory,
/// then <c>tool.execute.before</c> with an args object, against a real or fake <c>dtk</c> on <c>PATH</c>. Shared by
/// <c>OpenCodePluginTests</c> and <c>KiloPluginRuntimeTests</c>, which each supply the plugin body under test, the
/// project-relative path its harness loads it from, and any extra files that harness writes beside it.
/// </summary>
public abstract class ExportedPluginTestsBase : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-exportedplugin-{Guid.NewGuid()}");
    private readonly NodeDriver _node;

    /// <summary>Creates the Node driver bound to this test's temporary directory; setup itself waits for <see cref="InitializeAsync"/>.</summary>
    protected ExportedPluginTestsBase() => _node = new NodeDriver(_dir);

    /// <summary>Writes the plugin, any extra harness files, and the driver script that loads and calls it.</summary>
    /// <remarks>
    /// Runs after construction (xunit's <see cref="IAsyncLifetime"/> contract) rather than in the constructor, so
    /// that calling the overridable <see cref="PluginBody"/>/<see cref="PluginRelativePath"/>/<see cref="WriteExtraFiles"/>
    /// members here does not run them against a not-yet-initialized subclass (CA2214).
    /// </remarks>
    public async Task InitializeAsync()
    {
        var pluginPath = Path.Combine(_dir, PluginRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(pluginPath)!);
        await File.WriteAllTextAsync(pluginPath, ArtifactStamping.Apply(PluginBody, StampStyle.SlashComment));
        WriteExtraFiles(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "driver.mjs"), $$"""
            import { DtkPlugin } from "./{{PluginRelativePath}}";
            const [tool, command] = process.argv.slice(2);
            const hooks = await DtkPlugin({});
            const output = { args: { command, workdir: "w" } };
            const args = output.args;
            await hooks["tool.execute.before"]({ tool, sessionID: "s", callID: "c" }, output);
            process.stdout.write(JSON.stringify({ command: output.args.command, sameObject: output.args === args, workdir: output.args.workdir }));
            """);
    }

    /// <summary>The plugin source under test, before stamping.</summary>
    protected abstract string PluginBody { get; }

    /// <summary>Where the harness loads the plugin from, relative to the project root, forward-slashed.</summary>
    protected abstract string PluginRelativePath { get; }

    /// <summary>Writes any files the harness expects to sit beside the plugin. The default writes nothing.</summary>
    /// <param name="dir">The test's temporary project directory.</param>
    protected virtual void WriteExtraFiles(string dir)
    {
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_dir, true);
        return Task.CompletedTask;
    }

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
        result["sameObject"]!.GetValue<bool>().Should().BeTrue("the harness passes the same args object on to the tool");
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
        // The harness's working directory is the project, and the plugin runs before its permission check, so a
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
