using DotnetTokenKiller.Application.Integration;

namespace DotnetTokenKiller.Application.Tests.Integration;

/// <summary>Writes the Python-era hook installs that <c>dtk init</c> must migrate.</summary>
internal static class LegacyHookFixtures
{
    /// <summary>A stand-in body: every generation of the Python hook carried the legacy signature.</summary>
    private const string Body = "#!/usr/bin/env python3\n_DTK_SUBCOMMANDS = (\"build\", \"test\")\n";

    /// <summary>A script exactly as a stamped dtk generation wrote it.</summary>
    /// <param name="path">Where to write the script.</param>
    internal static void WriteStampedScript(string path) =>
        Write(path, ArtifactStamping.Apply(Body, StampStyle.HashComment));

    /// <summary>A stamped script someone edited afterwards, so its stamp no longer verifies.</summary>
    /// <param name="path">Where to write the script.</param>
    internal static void WriteEditedScript(string path) =>
        Write(path, ArtifactStamping.Apply(Body, StampStyle.HashComment).Replace("\"test\"", "\"publish\"", StringComparison.Ordinal));

    /// <summary>A script from dtk 0.6.0 or earlier, before stamping.</summary>
    /// <param name="path">Where to write the script.</param>
    internal static void WriteUnstampedScript(string path) => Write(path, Body);

    /// <summary>The command dtk registered for a Python hook at <paramref name="scriptPath"/>.</summary>
    /// <param name="scriptPath">Path to the Python hook script.</param>
    internal static string PythonCommand(string scriptPath) => $"python3 \"{scriptPath}\"";

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, creating its directory first.</summary>
    /// <param name="path">Where to write the file.</param>
    /// <param name="content">The content to write.</param>
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
