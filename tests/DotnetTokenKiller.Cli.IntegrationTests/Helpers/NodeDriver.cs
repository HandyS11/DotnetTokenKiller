using System.Diagnostics;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Cli.IntegrationTests.Aot;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>
/// Runs a test's <c>driver.mjs</c> under Node with a restricted <c>PATH</c>, and builds a fake <c>dtk</c> shell script for
/// tests that don't need the real binary. Shared by <c>ExportedPluginTestsBase</c> (OpenCode and Kilo),
/// <c>PiExtensionTests</c> and <c>AmpPluginTests</c>, which each write their own <c>driver.mjs</c> into
/// <paramref name="dir"/> and drive a different generated plugin/extension through it.
/// </summary>
/// <param name="dir">The test's temporary directory, holding <c>driver.mjs</c> and used as the default working directory.</param>
public sealed class NodeDriver(string dir)
{
    /// <summary>Writes a POSIX shell script as a fake <c>dtk</c> and returns the directory holding it.</summary>
    /// <param name="script">The script body, run with stdin/stdout attached and no shell quoting applied by the caller.</param>
    /// <exception cref="PlatformNotSupportedException">Running on Windows, which cannot run a POSIX shell script.</exception>
    public string FakeDtk(string script)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake dtk is a POSIX shell script.");
        }

        var bin = Directory.CreateDirectory(Path.Combine(dir, $"bin-{Guid.NewGuid():N}")).FullName;
        var path = Path.Combine(bin, "dtk");
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return bin;
    }

    public Task<JsonNode> RunAsync(string tool, string command, string pathDirectory) =>
        RunAsync(tool, command, pathDirectory, dir);

    public async Task<JsonNode> RunAsync(string tool, string command, string pathDirectory, string workingDirectory)
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
        psi.ArgumentList.Add(Path.Combine(dir, "driver.mjs"));
        psi.ArgumentList.Add(tool);
        psi.ArgumentList.Add(command);

        // ParityProcess drains stdout and stderr concurrently before awaiting either, avoiding the deadlock
        // a naive sequential read risks if the child fills the unread pipe before closing the other.
        var result = await ParityProcess.RunAsync(psi, stdin: null);

        result.ExitCode.Should().Be(0, "the plugin/extension must never throw; stderr: {0}", result.Stderr);
        return JsonNode.Parse(result.Stdout)!;
    }
}
