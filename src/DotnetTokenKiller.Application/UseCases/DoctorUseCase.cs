using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Result of a single doctor diagnostic check.</summary>
/// <param name="Name">Short display name for the check.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Message">Human-readable detail message.</param>
/// <param name="IsWarning">Whether a passing check still deserves the user's attention; a warning never fails doctor.</param>
public sealed record DiagnosticCheck(string Name, bool Passed, string Message, bool IsWarning = false)
{
    /// <summary>Creates a passing check marked as a warning.</summary>
    /// <param name="name">Short display name for the check.</param>
    /// <param name="message">Human-readable detail message.</param>
    public static DiagnosticCheck Warning(string name, string message) => new(name, true, message, IsWarning: true);
}

/// <summary>Runs a series of self-diagnostic checks to verify dtk is set up correctly.</summary>
/// <remarks>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the internal <see cref="HookHealthChecker"/>:
/// a primary constructor is as accessible as its containing type, and the compiler rejects (CS0051)
/// a public constructor exposing a less-accessible parameter type. Its only consumer,
/// <c>DoctorCommand</c>, is internal and resolves it through DI.
/// </remarks>
/// <param name="runner">The command runner used to probe the dotnet SDK.</param>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="hookHealth">Checks installed rewrite hooks.</param>
/// <param name="integrators">All provider integrators; the hook-installing ones are inspected.</param>
/// <param name="rtk">Detects an rtk hook and knows where rtk reads its config.</param>
/// <param name="locateRtk">
/// Returns the absolute path of the <c>rtk</c> a harness would run, or <see langword="null"/> when <c>PATH</c> has none.
/// </param>
internal sealed class DoctorUseCase(
    ICommandRunner runner,
    IConfigProvider configProvider,
    HookHealthChecker hookHealth,
    IEnumerable<IProviderIntegrator> integrators,
    RtkHookCoexistence rtk,
    Func<string?> locateRtk)
{
    /// <summary>Creates a use case that resolves <c>rtk</c> from this process's <c>PATH</c>.</summary>
    /// <param name="runner">The command runner used to probe the dotnet SDK and rtk.</param>
    /// <param name="configProvider">The configuration provider.</param>
    /// <param name="hookHealth">Checks installed rewrite hooks.</param>
    /// <param name="integrators">All provider integrators; the hook-installing ones are inspected.</param>
    /// <param name="rtk">Detects an rtk hook and knows where rtk reads its config.</param>
    public DoctorUseCase(
        ICommandRunner runner,
        IConfigProvider configProvider,
        HookHealthChecker hookHealth,
        IEnumerable<IProviderIntegrator> integrators,
        RtkHookCoexistence rtk)
        : this(
            runner,
            configProvider,
            hookHealth,
            integrators,
            rtk,
            static () => ExecutableSearch.FindOnProcessPath(RtkConfigLocator.RtkCommand))
    {
    }

    /// <summary>
    /// Runs all diagnostic checks and returns the results.
    /// </summary>
    /// <param name="dbPath">Resolved path to the tracking database file.</param>
    /// <param name="teeDirectory">Resolved path to the tee output directory.</param>
    /// <param name="projectDirectory">The directory to treat as the project root for hook checks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(
        string dbPath,
        string teeDirectory,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DiagnosticCheck>
        {
            await CheckDotnetSdkAsync(cancellationToken).ConfigureAwait(false),
            await CheckConfigAsync(cancellationToken).ConfigureAwait(false),
            CheckDbAccessible(dbPath),
            CheckTeeWritable(teeDirectory)
        };

        checks.AddRange(await hookHealth
            .RunAsync([.. integrators.OfType<IHookIntegrator>()], projectDirectory, cancellationToken)
            .ConfigureAwait(false));

        if (await CheckRtkCoexistenceAsync(projectDirectory, cancellationToken).ConfigureAwait(false) is { } rtkCheck)
        {
            checks.Add(rtkCheck);
        }

        return checks;
    }

    /// <summary>
    /// When rtk's hook is registered, asks rtk whether it would still rewrite <c>dotnet build</c> — it does unless
    /// <c>dotnet</c> is in its <c>[hooks].exclude_commands</c>, in which case both hooks rewrite the command. Doctor only
    /// reads: it never edits rtk's config.
    /// </summary>
    /// <remarks>
    /// The registration is read from a file the project controls, so the probe starts the <c>rtk</c> that <c>PATH</c>
    /// names, by its absolute path: a bare name would be found beside the running dtk first and, on Windows, in the
    /// current directory, which a cloned repository could fill. Real rtk exits 1 and prints nothing when it leaves a
    /// command alone, and prints the rewrite (exiting 3) when it does not; any other outcome is not an answer.
    /// </remarks>
    /// <param name="projectDirectory">The directory to treat as the project root for the hook check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The check, or <see langword="null"/> when no rtk hook is registered, rtk is not on <c>PATH</c>, or rtk gave no
    /// answer (could not run, too slow, or empty output with an exit code other than 1): there is then nothing to
    /// report, and the checks above are unchanged.
    /// </returns>
    private async Task<DiagnosticCheck?> CheckRtkCoexistenceAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        const string name = "rtk coexistence";

        if (!rtk.IsRtkHookPresent(projectDirectory))
        {
            return null;
        }

        if (locateRtk() is not { } rtkPath)
        {
            return null;
        }

        var rewrite = await RtkConfigLocator
            .TryRunAsync(runner, rtkPath, ["rewrite", "dotnet build"], RtkConfigLocator.ProbeTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (rewrite is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rewrite.StdOut))
        {
            return rewrite.ExitCode == 1
                ? new DiagnosticCheck(name, true, "rtk leaves dotnet commands to dtk")
                : null;
        }

        var configPath = await rtk.ResolveConfigPathAsync(cancellationToken).ConfigureAwait(false);
        return DiagnosticCheck.Warning(
            name,
            $"rtk also rewrites dotnet commands. Add \"dotnet\" to [hooks].exclude_commands in {configPath}, "
            + "or run 'dtk init claude' again.");
    }

    private async Task<DiagnosticCheck> CheckDotnetSdkAsync(CancellationToken cancellationToken)
    {
        const string name = "dotnet SDK";
        try
        {
            var result = await runner
                .RunCapturedAsync("dotnet", ["--version"], cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                var version = result.StdOut.Trim();
                return new DiagnosticCheck(name, true, $"Found dotnet {version}");
            }

            return new DiagnosticCheck(name, false, $"dotnet exited with code {result.ExitCode}");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(name, false, $"Could not run dotnet: {ex.Message}");
        }
    }

    private async Task<DiagnosticCheck> CheckConfigAsync(CancellationToken cancellationToken)
    {
        const string name = "config file";
        try
        {
            await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);
            return new DiagnosticCheck(name, true, "Loaded successfully (or using defaults)");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(name, false, $"Failed to load config: {ex.Message}");
        }
    }

    private static DiagnosticCheck CheckDbAccessible(string dbPath)
    {
        const string name = "tracking database";

        // No try/catch here, unlike the tee check below: File.Exists reports false for anything it
        // cannot resolve — a malformed path, a permission-denied parent — rather than throwing, so
        // there is no failure path to catch.
        if (File.Exists(dbPath))
        {
            return new DiagnosticCheck(name, true, $"Found at {dbPath}");
        }

        // A missing database (or its parent directory) is normal on a fresh install: the tracker
        // creates both on first write. Report it as pending, not a failure — consistent with the
        // tee-directory check below.
        return new DiagnosticCheck(name, true, $"No data yet — will be created at {dbPath}");
    }

    private static DiagnosticCheck CheckTeeWritable(string teeDirectory)
    {
        const string name = "tee directory";
        try
        {
            if (!Directory.Exists(teeDirectory))
            {
                return new DiagnosticCheck(name, true,
                    $"Does not exist yet — will be created at {teeDirectory}");
            }

            var probe = Path.Combine(teeDirectory, $".dtk-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new DiagnosticCheck(name, true, $"Writable at {teeDirectory}");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(name, false, $"Not writable: {ex.Message}");
        }
    }
}
