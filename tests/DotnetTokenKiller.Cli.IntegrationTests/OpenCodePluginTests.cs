using System.Diagnostics;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Cli.IntegrationTests.Aot;
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

    public OpenCodePluginTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "dtk.js"), ArtifactStamping.Apply(OpenCodePlugin.Body, StampStyle.SlashComment));
        File.WriteAllText(Path.Combine(_dir, "package.json"), """{"type":"module"}""");
        File.WriteAllText(Path.Combine(_dir, "driver.mjs"), """
            import { DtkPlugin } from "./dtk.js";
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
        var result = await RunAsync("bash", "dotnet build # répertoire", RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dtk dotnet build # répertoire");
        result["sameObject"]!.GetValue<bool>().Should().BeTrue("OpenCode passes the same args object on to the tool");
        result["workdir"]!.GetValue<string>().Should().Be("w");
    }

    [NodeFact]
    public async Task DtkMissingFromPath_LeavesTheCommandAndDoesNotThrowAsync()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;

        var result = await RunAsync("bash", "dotnet build", empty);

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

        var result = await RunAsync("bash", "dotnet build", string.Join(Path.PathSeparator, empty, string.Empty), RealDtkDirectory);

        result["command"]!.GetValue<string>().Should().Be("dotnet build");
    }

    [NodeUnixFact]
    public async Task OtherToolOrNoDotnet_NeverStartsDtkAsync()
    {
        var bin = FakeDtk("touch \"$(dirname \"$0\")/started\"; cat >/dev/null");

        (await RunAsync("read", "dotnet build", bin))["command"]!.GetValue<string>().Should().Be("dotnet build");
        (await RunAsync("bash", "ls -la", bin))["command"]!.GetValue<string>().Should().Be("ls -la");
        File.Exists(Path.Combine(bin, "started")).Should().BeFalse();
    }

    [NodeUnixFact]
    public async Task MalformedReply_LeavesTheCommandAsync()
    {
        var bin = FakeDtk("cat >/dev/null; echo 'not json'");

        (await RunAsync("bash", "dotnet test", bin))["command"]!.GetValue<string>().Should().Be("dotnet test");
    }

    [NodeUnixFact]
    public async Task HangingDtk_TimesOutAndLeavesTheCommandAsync()
    {
        var bin = FakeDtk("exec sleep 60");
        var stopwatch = Stopwatch.StartNew();

        var result = await RunAsync("bash", "dotnet test", bin);

        result["command"]!.GetValue<string>().Should().Be("dotnet test");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the plugin gives dtk 5 seconds");
    }

    private string FakeDtk(string script)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake dtk is a POSIX shell script.");
        }

        var bin = Directory.CreateDirectory(Path.Combine(_dir, $"bin-{Guid.NewGuid():N}")).FullName;
        var path = Path.Combine(bin, "dtk");
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return bin;
    }

    private Task<JsonNode> RunAsync(string tool, string command, string pathDirectory) =>
        RunAsync(tool, command, pathDirectory, _dir);

    private async Task<JsonNode> RunAsync(string tool, string command, string pathDirectory, string workingDirectory)
    {
        var node = ExecutableSearch.FindOnProcessPath("node")
                   ?? throw new InvalidOperationException("node is not on PATH");
        var psi = new ProcessStartInfo(node)
        {
            WorkingDirectory = workingDirectory,
            // Only the dtk under test is reachable, plus the POSIX tools a fake dtk script uses. Node's own
            // directory is deliberately left out: an installed dtk sharing it would defeat
            // DtkMissingFromPath. The apphost finds the .NET runtime through DOTNET_ROOT or the install
            // location, never PATH, and the child inherits this process's environment apart from PATH.
            Environment = { ["PATH"] = string.Join(Path.PathSeparator, pathDirectory, "/bin", "/usr/bin") }
        };
        psi.ArgumentList.Add(Path.Combine(_dir, "driver.mjs"));
        psi.ArgumentList.Add(tool);
        psi.ArgumentList.Add(command);

        // ParityProcess drains stdout and stderr concurrently before awaiting either, avoiding the deadlock
        // a naive sequential read risks if the child fills the unread pipe before closing the other.
        var result = await ParityProcess.RunAsync(psi, stdin: null);

        result.ExitCode.Should().Be(0, "the plugin must never throw; stderr: {0}", result.Stderr);
        return JsonNode.Parse(result.Stdout)!;
    }
}
