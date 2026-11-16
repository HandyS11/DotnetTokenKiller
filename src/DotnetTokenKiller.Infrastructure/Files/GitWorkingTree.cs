using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DotnetTokenKiller.Domain.Files;

namespace DotnetTokenKiller.Infrastructure.Files;

/// <summary>Asks the <c>git</c> on <c>PATH</c> about a work tree, giving up on any failure.</summary>
public sealed class GitWorkingTree : IGitWorkingTree
{
    /// <summary>How long one git command may take before it is killed and the answer given up.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>The git executable's full path, or <see langword="null"/> when there is none; found on first use.</summary>
    private readonly Lazy<string?> _gitPath;

    /// <summary>Uses the first <c>git</c> on <c>PATH</c>, looked up the first time git is asked something.</summary>
    public GitWorkingTree()
    {
        _gitPath = new Lazy<string?>(FindOnPath);
    }

    /// <summary>Uses the given git.</summary>
    /// <param name="gitPath">The git executable's full path, or <see langword="null"/> when there is none.</param>
    public GitWorkingTree(string? gitPath)
    {
        _gitPath = new Lazy<string?>(gitPath);
    }

    /// <summary>
    /// The first <c>git</c> (<c>git.exe</c> on Windows) in a <c>PATH</c> directory, as a shell finds it. A bare name
    /// handed to <c>Process.Start</c> would be looked up in dtk's own directory first, and on Windows in the current
    /// one, which is the repository dtk runs in: a <c>git.exe</c> committed there would run.
    /// </summary>
    internal static string? FindOnPath()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var entries = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        return entries
            .Select(entry => entry.Trim().Trim('"'))
            .Where(Path.IsPathFullyQualified)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(IsExecutableFile);
    }

    private static bool IsExecutableFile(string candidate)
    {
        try
        {
            if (!File.Exists(candidate))
            {
                return false;
            }

            const UnixFileMode anyExecute =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return OperatingSystem.IsWindows() || (File.GetUnixFileMode(candidate) & anyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? UnignoredFiles(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return Paths(Run(directory, cancellationToken, "ls-files", "-z", "--cached", "--others", "--exclude-standard"));
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? ChangedFiles(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);

        // Paths from the top of the work tree, whatever the directory, and whatever diff.relative says.
        var top = Run(directory, cancellationToken, "rev-parse", "--show-toplevel")?.Trim();
        if (string.IsNullOrEmpty(top))
        {
            return null;
        }

        var mergeBase = Run(top, cancellationToken, "merge-base", "HEAD", "refs/remotes/origin/HEAD")?.Trim();
        var changed = Paths(Run(top, cancellationToken, "diff", "--name-only", "-z", "--no-renames",
                          string.IsNullOrEmpty(mergeBase) ? "HEAD" : mergeBase, "--"))
                      // No HEAD yet: every tracked file is new.
                      ?? Paths(Run(top, cancellationToken, "ls-files", "-z", "--cached"));
        var untracked = Paths(Run(top, cancellationToken, "ls-files", "-z", "--others", "--exclude-standard"));
        if (changed is null || untracked is null)
        {
            return null;
        }

        return [.. changed.Concat(untracked).Distinct(StringComparer.Ordinal).Select(path => Path.GetFullPath(path, top))];
    }

    private static List<string>? Paths(string? output) =>
        output?.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Runs git in <paramref name="directory"/> and returns what it printed, or <see langword="null"/>.</summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="cancellationToken">Kills git.</param>
    /// <param name="arguments">git's arguments.</param>
    private string? Run(string directory, CancellationToken cancellationToken, params string[] arguments)
    {
        if (_gitPath.Value is not { } gitPath)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false)
        };
        // Never a prompt, and no lock a concurrent git command would trip on.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(Timeout);
            using var kill = deadline.Token.Register(() => Kill(process));
            process.StandardInput.Close();
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode == 0 && !deadline.IsCancellationRequested ? output : null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }
}
