using System.Diagnostics;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Runs the plugin <c>dtk init amp</c> generates under Node, as Amp's plugin host would call it: the default export
/// with a fake <c>amp</c> object that records the <c>tool.call</c> handler and stubs
/// <c>amp.helpers.shellCommandFromToolCall</c> as documented, then that handler with a tool-call event, against a
/// real or fake <c>dtk</c>. Amp's contract differs from OpenCode/Kilo's <c>tool.execute.before</c> (a request event
/// must return a result rather than mutate its input in place), so this does not derive from
/// <see cref="ExportedPluginTestsBase"/>; it reuses <see cref="NodeDriver"/> the same way
/// <c>PiExtensionTests</c> does for another non-OpenCode-shaped contract.
/// </summary>
/// <remarks>
/// <see cref="NodeDriver.RunAsync(string,string,string)"/> passes only two positional arguments (tool, command) to
/// <c>driver.mjs</c>, but the driver also needs which input field (<c>cmd</c> or <c>command</c>) carries the
/// command. The tool argument is packed as <c>"&lt;tool&gt;:&lt;field&gt;"</c> and split back apart in
/// <c>driver.mjs</c>.
/// </remarks>
public sealed class AmpPluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-ampplugin-{Guid.NewGuid()}");
    private readonly NodeDriver _node;

    public AmpPluginTests()
    {
        _node = new NodeDriver(_dir);
        // Amp's layout: the plugin under .amp/plugins/ with no package.json, so Node detects ESM by syntax.
        var plugins = Directory.CreateDirectory(Path.Combine(_dir, ".amp", "plugins")).FullName;
        File.WriteAllText(Path.Combine(plugins, "dtk.js"), ArtifactStamping.Apply(AmpPlugin.Body, StampStyle.SlashComment));
        File.WriteAllText(Path.Combine(_dir, "driver.mjs"), """
            import plugin from "./.amp/plugins/dtk.js";
            const [toolAndField, command] = process.argv.slice(2);
            const [tool, field] = toolAndField.split(":");
            let handler;
            const amp = {
              on(event, fn) { if (event === "tool.call") handler = fn; return { dispose() {} }; },
              helpers: {
                shellCommandFromToolCall(call) {
                  if (call.tool !== "Bash" && call.tool !== "shell_command") return null;
                  const value = call.input.cmd ?? call.input.command;
                  return typeof value === "string" ? { command: value } : null;
                },
              },
            };
            await plugin(amp);
            const input = { [field]: command, other: "o" };
            const result = await handler({ toolUseID: "t", tool, input, thread: { id: "T" } }, {});
            process.stdout.write(JSON.stringify({ result, input }));
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
    public async Task Bash_DotnetCommandInCmd_IsModifiedByTheRealDtkAsync()
    {
        var result = await _node.RunAsync("Bash:cmd", "dotnet build", RealDtkDirectory);

        result["result"]!["action"]!.GetValue<string>().Should().Be("modify");
        result["result"]!["input"]!["cmd"]!.GetValue<string>().Should().Be("dtk dotnet build");
        result["result"]!["input"]!["other"]!.GetValue<string>().Should().Be("o");
        result["input"]!["cmd"]!.GetValue<string>().Should().Be("dotnet build", "the original input object must not be mutated");
    }

    [NodeFact]
    public async Task ShellCommandInCommandField_IsModifiedInThatFieldAsync()
    {
        var result = await _node.RunAsync("shell_command:command", "dotnet test", RealDtkDirectory);

        result["result"]!["action"]!.GetValue<string>().Should().Be("modify");
        result["result"]!["input"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test");
        result["result"]!["input"]!.AsObject().ContainsKey("cmd").Should().BeFalse();
    }

    [NodeFact]
    public async Task DtkMissingFromPath_AllowsAsync()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await _node.RunAsync("Bash:cmd", "dotnet build", empty);

        result["result"]!["action"]!.GetValue<string>().Should().Be("allow");
    }

    [NodeUnixFact]
    public async Task OtherToolOrNoDotnet_AllowsWithoutStartingDtkAsync()
    {
        var bin = _node.FakeDtk("touch \"$(dirname \"$0\")/started\"; cat >/dev/null");

        (await _node.RunAsync("Read:cmd", "dotnet build", bin))["result"]!["action"]!.GetValue<string>().Should().Be("allow");
        (await _node.RunAsync("Bash:cmd", "ls", bin))["result"]!["action"]!.GetValue<string>().Should().Be("allow");
        File.Exists(Path.Combine(bin, "started")).Should().BeFalse();
    }

    [NodeUnixFact]
    public async Task MalformedReply_AllowsAsync()
    {
        var bin = _node.FakeDtk("cat >/dev/null; echo 'not json'");

        (await _node.RunAsync("Bash:cmd", "dotnet test", bin))["result"]!["action"]!.GetValue<string>().Should().Be("allow");
    }

    [NodeUnixFact]
    public async Task HangingDtk_TimesOutAndAllowsAsync()
    {
        var bin = _node.FakeDtk("exec sleep 60");
        var stopwatch = Stopwatch.StartNew();

        var result = await _node.RunAsync("Bash:cmd", "dotnet test", bin);

        result["result"]!["action"]!.GetValue<string>().Should().Be("allow");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the plugin gives dtk 5 seconds");
    }

    [NodeFact]
    public async Task DtkOnlyInTheWorkingDirectory_IsNeverRunAsync()
    {
        // Amp's working directory is the project, and the plugin runs before any permission check, so a dtk
        // committed to a repository must not run. Given a bare name, spawn on Windows searches the current
        // directory before PATH, and on Unix an empty PATH entry means the current directory; the plugin must look
        // on PATH alone. The working directory here holds a runnable dtk that would rewrite the command.
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await _node.RunAsync("Bash:cmd", "dotnet build", string.Join(Path.PathSeparator, empty, string.Empty), RealDtkDirectory);

        result["result"]!["action"]!.GetValue<string>().Should().Be("allow");
    }

    [NodeUnixFact]
    public async Task UnchangedReply_AllowsAsync()
    {
        var bin = _node.FakeDtk("""cat >/dev/null; echo '{"command":"dotnet build"}'""");

        var result = await _node.RunAsync("Bash:cmd", "dotnet build", bin);

        result["result"]!["action"]!.GetValue<string>().Should().Be("allow");
    }
}
