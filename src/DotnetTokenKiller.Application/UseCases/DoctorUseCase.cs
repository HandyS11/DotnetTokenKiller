using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Result of a single doctor diagnostic check.</summary>
/// <param name="Name">Short display name for the check.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Message">Human-readable detail message.</param>
public sealed record DiagnosticCheck(string Name, bool Passed, string Message);

/// <summary>Runs a series of self-diagnostic checks to verify dtk is set up correctly.</summary>
/// <param name="runner">The command runner used to probe the dotnet SDK.</param>
/// <param name="configProvider">The configuration provider.</param>
public sealed class DoctorUseCase(ICommandRunner runner, IConfigProvider configProvider)
{
    /// <summary>
    /// Runs all diagnostic checks and returns the results.
    /// </summary>
    /// <param name="dbPath">Resolved path to the tracking database file.</param>
    /// <param name="teeDirectory">Resolved path to the tee output directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(
        string dbPath,
        string teeDirectory,
        CancellationToken cancellationToken = default)
    {
        return
        [
            await CheckDotnetSdkAsync(cancellationToken).ConfigureAwait(false),
            await CheckConfigAsync(cancellationToken).ConfigureAwait(false),
            CheckDbAccessible(dbPath),
            CheckTeeWritable(teeDirectory)
        ];
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
        try
        {
            if (File.Exists(dbPath))
            {
                return new DiagnosticCheck(name, true, $"Found at {dbPath}");
            }

            // A missing database (or its parent directory) is normal on a fresh install: the
            // tracker creates both on first write. Report it as pending, not a failure —
            // consistent with the tee-directory check below.
            return new DiagnosticCheck(name, true,
                $"No data yet — will be created at {dbPath}");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(name, false, $"Cannot access database path: {ex.Message}");
        }
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
