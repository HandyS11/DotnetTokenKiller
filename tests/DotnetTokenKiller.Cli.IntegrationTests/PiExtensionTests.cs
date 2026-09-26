using System.Diagnostics;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the extension <c>dtk init pi</c> generates under Node, as pi and oh-my-pi call it: the default export with an
/// API that records the <c>tool_call</c> handler, then that handler with a bash event, against a real or fake <c>dtk</c>.
/// </summary>
public sealed class PiExtensionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-piext-{Guid.NewGuid()}");
    private readonly NodeDriver _node;

    public PiExtensionTests()
    {
        _node = new NodeDriver(_dir);
        // pi's layout: the extension under .pi/extensions/ with no package.json, so Node detects ESM by syntax.
        var extensions = Directory.CreateDirectory(Path.Combine(_dir, ".pi", "extensions")).FullName;
        File.WriteAllText(Path.Combine(extensions, "dtk.js"), ArtifactStamping.Apply(PiExtension.Body("pi", "pi"), StampStyle.SlashComment));
        File.WriteAllText(Path.Combine(_dir, "driver.mjs"), """
            import factory from "./.pi/extensions/dtk.js";
            const [toolName, command] = process.argv.slice(2);
            let handler;
            factory({ on: (name, fn) => { if (name === "tool_call") handler = fn; } });
            const input = command === "<none>" ? { timeout: 7, cwd: "w" }
              : command === "<number>" ? { command: 42, timeout: 7, cwd: "w" }
              : { command, timeout: 7, cwd: "w" };
            const result = await handler({ type: "tool_call", toolName, toolCallId: "c", input });
            process.stdout.write(JSON.stringify({ command: input.command ?? null, result: result ?? null }));
            """);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static string RealDtkDirectory
    {
        get
        {
            var binary = Environment.GetEnvironmentVariable(DtkLauncher.TestBinaryVariable);
            return string.IsNullOrWhiteSpace(binary) ? AppContext.BaseDirectory : Path.GetDirectoryName(binary.Trim())!;
        }
    }

    [NodeFact]
    public async Task Bash_DotnetCommand_IsRewrittenBothWaysByTheRealDtkAsync()
    {
        var result = await _node.RunAsync("bash", "dotnet build # répertoire", RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire", "pi runs the mutated args");
        var input = result["result"]!["input"]!;
        input["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire", "oh-my-pi runs the returned input");
        input["timeout"]!.GetValue<int>().Should().Be(7);
        input["cwd"]!.GetValue<string>().Should().Be("w");
    }

    [NodeFact]
    public async Task DtkMissingFromPath_LeavesTheCommandAndReturnsNothingAsync()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await _node.RunAsync("bash", "dotnet build", empty);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
        result["result"].Should().BeNull();
    }

    [NodeFact]
    public async Task MissingOrNonStringCommand_PassesThroughAsync()
    {
        var result = await _node.RunAsync("bash", "<none>", RealDtkDirectory);

        result["command"].Should().BeNull();
        result["result"].Should().BeNull();

        var number = await _node.RunAsync("bash", "<number>", RealDtkDirectory);

        number["command"]!.GetValue<int>().Should().Be(42);
        number["result"].Should().BeNull();
    }

    [NodeFact]
    public async Task DtkOnlyInTheWorkingDirectory_IsNeverRunAsync()
    {
        // pi's working directory is the project, and the extension runs before any permission check, so a dtk
        // committed to a repository must not run. Given a bare name, spawn on Windows searches the current
        // directory before PATH, and on Unix an empty PATH entry means the current directory; the extension must
        // look on PATH alone. The working directory here holds a runnable dtk that would rewrite the command.
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await _node.RunAsync("bash", "dotnet build", string.Join(Path.PathSeparator, empty, string.Empty), RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
        result["result"].Should().BeNull();
    }

    [NodeUnixFact]
    public async Task OtherToolOrNoDotnet_NeverStartsDtkAsync()
    {
        var bin = _node.FakeDtk("touch \"$(dirname \"$0\")/started\"; cat >/dev/null");

        (await _node.RunAsync("read", "dotnet build", bin))["result"].Should().BeNull();
        (await _node.RunAsync("bash", "ls -la", bin))["result"].Should().BeNull();
        File.Exists(Path.Combine(bin, "started")).Should().BeFalse();
    }

    [NodeUnixFact]
    public async Task FailingOrMalformedDtk_LeavesTheCommandAsync()
    {
        foreach (var script in new[] { "cat >/dev/null; echo 'not json'", "cat >/dev/null; exit 3" })
        {
            var result = await _node.RunAsync("bash", "dotnet test", _node.FakeDtk(script));

            result["command"]!.GetValue<string>().Should().Be("dotnet test");
            result["result"].Should().BeNull();
        }
    }

    [NodeUnixFact]
    public async Task HangingDtk_TimesOutAndLeavesTheCommandAsync()
    {
        var bin = _node.FakeDtk("exec sleep 60");
        var stopwatch = Stopwatch.StartNew();

        var result = await _node.RunAsync("bash", "dotnet test", bin);

        result["command"]!.GetValue<string>().Should().Be("dotnet test");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the extension gives dtk 5 seconds, under oh-my-pi's 30 s handler limit");
    }
}
