using System.Runtime.Versioning;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// The private world one side of a parity case runs in: a home directory, a project directory and
/// dtk's config, database and tee logs, all under <see cref="Root"/>.
/// </summary>
internal sealed class ParitySandbox
{
    private const string FakeDotnetDirectoryName = "fake-dotnet";

    internal ParitySandbox(string root)
    {
        Root = root;
        Home = Path.Combine(root, "home");
        Project = Path.Combine(root, "proj");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Project);
    }

    internal string Root { get; }

    internal string Home { get; }

    internal string Project { get; }

    internal string DatabasePath => Path.Combine(Home, "tracking.db");

    /// <summary>The directory holding a fake <c>dotnet</c>, or <see langword="null"/> when none was made.</summary>
    internal string? FakeDotnetDirectory { get; private set; }

    /// <summary>Writes <paramref name="text"/> to <paramref name="relativePath"/> under <see cref="Root"/>.</summary>
    /// <param name="relativePath">A path relative to <see cref="Root"/>, with <c>/</c> separators.</param>
    /// <param name="text">The file's content.</param>
    internal void WriteFile(string relativePath, string text)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>
    /// Writes a POSIX shell script named <c>dotnet</c> that prints the fixture mapped to its first
    /// argument and exits with the mapped code. dtk starts the bare name <c>dotnet</c> through
    /// <c>PATH</c>, so putting the script's directory first on the child's <c>PATH</c> replaces the SDK.
    /// </summary>
    /// <param name="subcommands">The fixture file and exit code for each first argument.</param>
    [UnsupportedOSPlatform("windows")]
    internal void CreateFakeDotnet(IReadOnlyDictionary<string, (string Fixture, int ExitCode)> subcommands)
    {
        var directory = Path.Combine(Root, FakeDotnetDirectoryName);
        Directory.CreateDirectory(directory);

        var script = new System.Text.StringBuilder("#!/bin/sh\ncase \"$1\" in\n");
        foreach (var (subcommand, (fixture, exitCode)) in subcommands)
        {
            File.Copy(ParityRunner.FixturePath(fixture), Path.Combine(directory, fixture));

            // $0 carries the script's absolute path because dtk execs it after resolving PATH.
            script.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"  {subcommand}) cat \"${{0%/*}}/{fixture}\"; exit {exitCode} ;;\n");
        }

        script.Append("  *) echo \"fake dotnet: unexpected arguments: $*\" >&2; exit 99 ;;\nesac\n");

        var scriptPath = Path.Combine(directory, "dotnet");
        File.WriteAllText(scriptPath, script.ToString());
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        FakeDotnetDirectory = directory;
    }

    /// <summary>Whether <paramref name="relativePath"/> is state the comparison ignores.</summary>
    /// <param name="relativePath">A path relative to <see cref="Root"/>, with <c>/</c> separators.</param>
    internal static bool IsIgnored(string relativePath)
    {
        string[] ignoredPrefixes =
        [
            "home/tracking.db", // compared row by row instead; the file itself holds timestamps
            "home/.dotnet/", "home/.nuget/", "home/.local/", "home/.templateengine/", // the SDK's own first-run state
            FakeDotnetDirectoryName + "/",
        ];
        return ignoredPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));
    }
}
