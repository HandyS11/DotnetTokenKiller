using System.Runtime.InteropServices;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Finds the <c>config.toml</c> rtk actually reads. rtk is a Rust program that places it with
/// <c>dirs::config_dir</c>, so the location differs by operating system: <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> on
/// Linux, <c>~/Library/Application Support</c> on macOS and <c>%APPDATA%</c> on Windows. Writing the exclusion anywhere
/// else leaves rtk's hook rewriting <c>dotnet</c> alongside dtk's.
/// </summary>
internal static class RtkConfigLocator
{
    /// <summary>The command name rtk is installed as.</summary>
    internal const string RtkCommand = "rtk";

    /// <summary>The label of the line <c>rtk config</c> prints its config file path on.</summary>
    private const string ConfigLinePrefix = "Config:";

    /// <summary>How long an rtk probe may run before dtk gives up on it; rtk answers these in milliseconds.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The default location of rtk's config file on <paramref name="os"/>, which is where rtk reads it when nothing
    /// overrides that.
    /// </summary>
    /// <remarks>
    /// The Windows location is inferred from <c>dirs::config_dir</c>, not verified on a Windows machine.
    /// </remarks>
    /// <param name="os">The operating system.</param>
    /// <param name="home">The user's home directory.</param>
    /// <param name="xdgConfigHome">
    /// <c>$XDG_CONFIG_HOME</c>. Linux only, and only when absolute: the XDG specification says a relative or empty value
    /// is invalid, and honoring one would resolve a global config against the directory dtk was launched from.
    /// </param>
    /// <param name="appData">The user's roaming application data directory (<c>%APPDATA%</c>). Windows only.</param>
    internal static string PlatformDefault(OSPlatform os, string home, string? xdgConfigHome, string? appData) =>
        Path.Combine(ConfigHome(os, home, xdgConfigHome, appData), "rtk", "config.toml");

    private static string ConfigHome(OSPlatform os, string home, string? xdgConfigHome, string? appData)
    {
        if (os == OSPlatform.OSX)
        {
            return Path.Combine(home, "Library", "Application Support");
        }

        if (os == OSPlatform.Windows)
        {
            return string.IsNullOrEmpty(appData) ? Path.Combine(home, "AppData", "Roaming") : appData;
        }

        return string.IsNullOrEmpty(xdgConfigHome) || !Path.IsPathRooted(xdgConfigHome)
            ? Path.Combine(home, ".config")
            : xdgConfigHome;
    }

    /// <summary>
    /// The path <c>rtk config</c> printed: whatever follows <c>Config:</c> on the line that starts with it, trimmed.
    /// </summary>
    /// <param name="stdout">What <c>rtk config</c> wrote to standard output.</param>
    /// <returns>The path, or <see langword="null"/> when there is no such line or nothing follows the label.</returns>
    internal static string? ParseConfigCommandOutput(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            if (!line.StartsWith(ConfigLinePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var path = line[ConfigLinePrefix.Length..].Trim();
            return path.Length == 0 ? null : path;
        }

        return null;
    }

    /// <summary>
    /// Resolves the path of the config file rtk reads: the one <c>rtk config</c> prints when rtk is on <c>PATH</c>,
    /// otherwise the platform default. Never throws apart from cancellation.
    /// </summary>
    /// <param name="runner">Runs <c>rtk config</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static Task<string> ResolveAsync(ICommandRunner runner, CancellationToken cancellationToken) =>
        ResolveAsync(
            runner,
            static () => ExecutableSearch.FindOnProcessPath(RtkCommand),
            CurrentPlatformDefault(),
            ProbeTimeout,
            cancellationToken);

    /// <summary>The same resolution with the <c>PATH</c> lookup, the fallback and the timeout supplied.</summary>
    /// <param name="runner">Runs <c>rtk config</c>.</param>
    /// <param name="locateRtk">Returns the absolute path of the rtk on <c>PATH</c>, or <see langword="null"/> when there is none.</param>
    /// <param name="fallback">The path to use when rtk cannot say.</param>
    /// <param name="timeout">How long <c>rtk config</c> may run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<string> ResolveAsync(
        ICommandRunner runner,
        Func<string?> locateRtk,
        string fallback,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (locateRtk() is not { } rtk)
        {
            return fallback;
        }

        var result = await TryRunAsync(runner, rtk, ["config"], timeout, cancellationToken).ConfigureAwait(false);

        // A relative path is as unsafe here as a relative XDG_CONFIG_HOME: it would be written against the working directory.
        return result is { ExitCode: 0 } && ParseConfigCommandOutput(result.StdOut) is { } printed && Path.IsPathRooted(printed)
            ? printed
            : fallback;
    }

    /// <summary>
    /// Runs a command to ask it a question: bounded by <paramref name="timeout"/>, and never throwing, so a missing,
    /// wedged or crashing rtk costs the caller its answer and nothing more.
    /// </summary>
    /// <param name="runner">Runs the command.</param>
    /// <param name="command">The command to start.</param>
    /// <param name="args">Its arguments.</param>
    /// <param name="timeout">How long it may run.</param>
    /// <param name="cancellationToken">Cancellation token; cancelling it is the one thing that does throw.</param>
    /// <returns>The captured result, or <see langword="null"/> when the command could not be run to completion.</returns>
    internal static async Task<CommandResult?> TryRunAsync(
        ICommandRunner runner,
        string command,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await runner.RunCapturedAsync(command, args, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null; // the timeout, not the caller
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // not installed, not executable, crashed: the caller carries on without an answer
        }
    }

    /// <summary>The default for the operating system this process runs on, from its own environment.</summary>
    private static string CurrentPlatformDefault() =>
        PlatformDefault(
            CurrentPlatform(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    private static OSPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return OSPlatform.OSX;
        }

        return OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.Linux;
    }
}
