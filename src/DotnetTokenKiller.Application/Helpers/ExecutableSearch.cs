namespace DotnetTokenKiller.Application.Helpers;

/// <summary>Finds a command on <c>PATH</c> the way a shell does, without starting a process.</summary>
/// <remarks>
/// A bare name handed to <c>Process.Start</c> is looked up in the running executable's own directory
/// before <c>PATH</c> (and, on Windows, in the current and system directories too), so a dtk that starts
/// <c>dtk</c> always finds itself. A harness runs <c>dtk</c> through a shell, which consults <c>PATH</c>
/// alone; checking what the harness will run means resolving the name here and starting the absolute path.
/// </remarks>
internal static class ExecutableSearch
{
    /// <summary>The extensions Windows tries when <c>PATHEXT</c> is unset, in its default order.</summary>
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    /// <summary>The extension a .NET tool's shim carries on Windows, tried before any <c>PATHEXT</c> entry.</summary>
    private const string ExeExtension = ".exe";

    /// <summary>
    /// Returns the first file on <paramref name="path"/> that runs as <paramref name="command"/>, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// Directories are searched in <c>PATH</c> order, and every candidate name is tried in one directory before
    /// the next, as cmd and a POSIX shell both do. Empty entries are skipped rather than read as the current
    /// directory, matching .NET's own <c>PATH</c> search. On Windows the candidates are
    /// <c><paramref name="command"/>.exe</c> followed by the <c>PATHEXT</c> extensions, and entries may be
    /// quoted; elsewhere the only candidate is <paramref name="command"/> itself.
    /// </remarks>
    /// <param name="command">The bare command name, e.g. <c>dtk</c>.</param>
    /// <param name="path">The <c>PATH</c> value to search.</param>
    /// <param name="pathExt">The <c>PATHEXT</c> value; ignored unless <paramref name="isWindows"/>.</param>
    /// <param name="isWindows">Whether to search with Windows' separators and extensions.</param>
    /// <param name="isExecutableFile">Whether a candidate path is a file the shell would run.</param>
    internal static string? Find(
        string command, string? path, string? pathExt, bool isWindows, Func<string, bool> isExecutableFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        ArgumentNullException.ThrowIfNull(isExecutableFile);

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var names = isWindows ? WindowsCandidateNames(command, pathExt) : [command];
        var separator = isWindows ? '\\' : '/';

        foreach (var entry in path.Split(isWindows ? ';' : ':'))
        {
            var directory = isWindows ? entry.Trim().Trim('"') : entry;
            if (directory.Length == 0)
            {
                continue;
            }

            var prefix = EndsWithSeparator(directory, isWindows) ? directory : directory + separator;
            var found = names.Select(name => prefix + name).FirstOrDefault(isExecutableFile);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves <paramref name="command"/> from this process's <c>PATH</c> (and <c>PATHEXT</c> on Windows) to
    /// an absolute path, or <see langword="null"/> when it is not there.
    /// </summary>
    /// <param name="command">The bare command name, e.g. <c>dtk</c>.</param>
    internal static string? FindOnProcessPath(string command)
    {
        var found = Find(
            command,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"),
            OperatingSystem.IsWindows(),
            IsExecutableFile);

        return found is null ? null : Path.GetFullPath(found);
    }

    /// <summary>Whether <paramref name="candidate"/> is a file, and on Unix one that someone may execute.</summary>
    /// <param name="candidate">The path to test.</param>
    private static bool IsExecutableFile(string candidate)
    {
        if (!File.Exists(candidate))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(candidate) & anyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary><c>command.exe</c>, then <paramref name="command"/> with each <c>PATHEXT</c> extension.</summary>
    /// <param name="command">The bare command name.</param>
    /// <param name="pathExt">The <c>PATHEXT</c> value, or <see langword="null"/> for Windows' default.</param>
    private static string[] WindowsCandidateNames(string command, string? pathExt)
    {
        var extensions = (string.IsNullOrWhiteSpace(pathExt) ? DefaultPathExt : pathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => !string.Equals(extension, ExeExtension, StringComparison.OrdinalIgnoreCase));

        return [command + ExeExtension, .. extensions.Select(extension => command + extension)];
    }

    /// <summary>Whether <paramref name="directory"/> already ends in a directory separator.</summary>
    /// <param name="directory">A <c>PATH</c> entry.</param>
    /// <param name="isWindows">Whether <c>\</c> also counts as a separator.</param>
    private static bool EndsWithSeparator(string directory, bool isWindows)
        => directory.EndsWith('/') || (isWindows && directory.EndsWith('\\'));
}
