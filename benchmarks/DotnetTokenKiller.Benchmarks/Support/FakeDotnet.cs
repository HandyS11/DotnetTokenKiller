using System.Globalization;
using System.Runtime.Versioning;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// A stand-in <c>dotnet</c> for the wrapped cold-start scenarios: a POSIX shell script that
/// optionally sleeps, prints captured output and exits with a fixed code, ignoring its arguments.
/// </summary>
/// <remarks>
/// dtk launches the bare name <c>dotnet</c> through <c>PATH</c>, so putting
/// <see cref="DirectoryPath"/> first on a child's <c>PATH</c> makes dtk run this script instead of
/// the SDK. A real build cannot be the child: build-spawning runs are unreliable on a developer
/// machine, and a build's own variance would swamp a sub-second overhead. The script itself costs
/// a shell start and a <c>cat</c>, one to two milliseconds.
/// </remarks>
internal sealed class FakeDotnet
{
    private FakeDotnet(string directoryPath)
    {
        DirectoryPath = directoryPath;
        ScriptPath = Path.Combine(directoryPath, "dotnet");
    }

    /// <summary>The directory holding the script, to put first on a child's <c>PATH</c>.</summary>
    internal string DirectoryPath { get; }

    /// <summary>The script itself, spawnable directly to time the child alone.</summary>
    internal string ScriptPath { get; }

    /// <summary>Writes the script and the output it prints into <paramref name="directoryPath"/>.</summary>
    /// <param name="directoryPath">A directory to create and hold both files; must not contain a
    /// single quote, which the script uses to quote the output path.</param>
    /// <param name="output">The text the script prints to stdout.</param>
    /// <param name="exitCode">The code the script exits with.</param>
    /// <param name="delay">How long the script sleeps before printing; <see cref="TimeSpan.Zero"/>
    /// for none.</param>
    [UnsupportedOSPlatform("windows")]
    internal static FakeDotnet Create(string directoryPath, string output, int exitCode, TimeSpan delay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(output);

        Directory.CreateDirectory(directoryPath);
        var fake = new FakeDotnet(directoryPath);

        var outputPath = Path.Combine(directoryPath, "output.txt");
        File.WriteAllText(outputPath, output);

        var sleep = delay > TimeSpan.Zero
            ? string.Create(CultureInfo.InvariantCulture, $"sleep {delay.TotalSeconds:0.###}\n")
            : string.Empty;
        var script = string.Create(
            CultureInfo.InvariantCulture,
            $"#!/bin/sh\n{sleep}cat '{outputPath}'\nexit {exitCode}\n");

        File.WriteAllText(fake.ScriptPath, script);
        File.SetUnixFileMode(
            fake.ScriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return fake;
    }
}
