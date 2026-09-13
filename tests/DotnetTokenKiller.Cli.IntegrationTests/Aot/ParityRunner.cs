using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>What one side of a parity case produced, with everything run-specific normalized away.</summary>
/// <param name="Steps">Each step's exit code and combined output, in order.</param>
/// <param name="Files">Every file the steps left under the sandbox, by normalized relative path.</param>
/// <param name="TrackingRows">The tracking database's rows, in insertion order.</param>
internal sealed record ParityResult(
    IReadOnlyList<string> Steps,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> TrackingRows);

/// <summary>Runs a parity case through the JIT build and through the binary in <c>DTK_AOT_BINARY</c>.</summary>
internal static partial class ParityRunner
{
    /// <summary>
    /// The JIT build's apphost, not <c>dotnet dtk.dll</c>: on Unix, <c>Process.Start</c> looks for a bare
    /// file name beside the running executable before searching <c>PATH</c>, so under the <c>dotnet</c>
    /// muxer dtk would find the real SDK before a fake <c>dotnet</c>. The apphost is also what the
    /// framework-dependent tool package runs.
    /// </summary>
    private static readonly string JitAppHost =
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "dtk.exe" : "dtk");

    internal static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);

    /// <summary>Runs <paramref name="parityCase"/> once per build, each in a fresh sandbox.</summary>
    /// <param name="parityCase">The case to run.</param>
    internal static async Task<(ParityResult Jit, ParityResult Aot)> RunBothAsync(ParityCase parityCase)
    {
        var aotBinary = AotParitySkip.ReadRequired(AotParitySkip.AotBinaryVariable);
        var caseRoot = Path.Combine(Path.GetTempPath(), $"dtk-parity-{Guid.NewGuid():N}");
        var sandboxRoot = Path.Combine(caseRoot, "sandbox");
        try
        {
            // Both sides run at the same path, one after the other, so paths dtk prints (including
            // those a table wraps across lines, which no normalization can rejoin) are identical.
            var jit = await RunAsync(JitAppHost, new ParitySandbox(sandboxRoot), parityCase);
            Directory.Move(sandboxRoot, Path.Combine(caseRoot, "jit"));
            var aot = await RunAsync(aotBinary, new ParitySandbox(sandboxRoot), parityCase);
            return (jit, aot);
        }
        finally
        {
            try
            {
                Directory.Delete(caseRoot, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing the comparison over.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }

    private static async Task<ParityResult> RunAsync(string executable, ParitySandbox sandbox, ParityCase parityCase)
    {
        parityCase.Arrange?.Invoke(sandbox);

        var steps = new List<string>();
        foreach (var step in parityCase.Steps)
        {
            var (output, exitCode) = await RunStepAsync(executable, sandbox, step);
            steps.Add($"[{string.Join(' ', step.Arguments)}] exit {exitCode}\n{Normalize(output, sandbox)}");
        }

        return new ParityResult(steps, ReadFiles(sandbox), await ReadTrackingRowsAsync(sandbox));
    }

    private static async Task<(string Output, int ExitCode)> RunStepAsync(
        string executable, ParitySandbox sandbox, ParityStep step)
    {
        var stdin = step.StdinFixture is null ? null : await File.ReadAllTextAsync(FixturePath(step.StdinFixture));
        var output = await ParityProcess.RunAsync(CreateStartInfo(executable, sandbox, step.Arguments), stdin);
        return (output.Stdout + output.Stderr, output.ExitCode);
    }

    /// <summary>
    /// A start for <paramref name="executable"/> confined to <paramref name="sandbox"/>: dtk's config, database
    /// and tee logs, the home directories and the fake <c>dotnet</c>, if any, all point into it.
    /// </summary>
    /// <param name="executable">The dtk binary to run.</param>
    /// <param name="sandbox">The sandbox to run in.</param>
    /// <param name="arguments">Arguments for dtk; <c>{project}</c> is replaced with the sandbox's project directory.</param>
    internal static ProcessStartInfo CreateStartInfo(string executable, ParitySandbox sandbox, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = sandbox.Project };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument.Replace("{project}", sandbox.Project, StringComparison.Ordinal));
        }

        psi.Environment["DTK_CONFIG_PATH"] = Path.Combine(sandbox.Home, "config.json");
        psi.Environment["DTK_DB_PATH"] = sandbox.DatabasePath;
        psi.Environment["DTK_TEE_DIR"] = Path.Combine(sandbox.Home, "logs");
        psi.Environment["HOME"] = sandbox.Home;
        psi.Environment["USERPROFILE"] = sandbox.Home;
        psi.Environment["XDG_CONFIG_HOME"] = Path.Combine(sandbox.Home, ".config");
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        if (sandbox.FakeDotnetDirectory is not null)
        {
            psi.Environment["PATH"] = sandbox.FakeDotnetDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        return psi;
    }

    private static Dictionary<string, string> ReadFiles(ParitySandbox sandbox)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(sandbox.Root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sandbox.Root, path).Replace('\\', '/');
            if (ParitySandbox.IsIgnored(relative))
            {
                continue;
            }

            files[Normalize(relative, sandbox)] = Normalize(File.ReadAllText(path), sandbox);
        }

        return files;
    }

    private static async Task<List<string>> ReadTrackingRowsAsync(ParitySandbox sandbox)
    {
        if (!File.Exists(sandbox.DatabasePath))
        {
            return [];
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sandbox.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        // Everything but id, timestamp and execution_time_ms, which differ between any two runs.
        command.CommandText = """
                              SELECT command, project_path, input_tokens, output_tokens, saved_tokens,
                                     savings_percentage, success, outcome, source
                              FROM commands ORDER BY id
                              """;

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var fields = Enumerable.Range(0, reader.FieldCount)
                .Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
            rows.Add(Normalize(string.Join('|', fields), sandbox));
        }

        return rows;
    }

    /// <summary>Replaces the sandbox root, timestamps, durations and log-file stamps with fixed tokens.</summary>
    /// <param name="text">Output, a file's content or a relative path.</param>
    /// <param name="sandbox">The sandbox whose root to replace.</param>
    internal static string Normalize(string text, ParitySandbox sandbox)
    {
        var root = sandbox.Root;
        string[] roots = OperatingSystem.IsMacOS()
            ? ["/private" + root, root] // macOS temp paths can surface through the /private/var symlink
            : [root.Replace("\\", @"\\", StringComparison.Ordinal), root.Replace('\\', '/'), root];

        foreach (var form in roots)
        {
            text = text.Replace(form, "<ROOT>", StringComparison.Ordinal);
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = TimestampRegex().Replace(text, "<TIMESTAMP>");
        text = TimeSpanRegex().Replace(text, "<TIMESPAN>");
        text = DurationRegex().Replace(text, "<DURATION>");
        text = ExportElapsedRegex().Replace(text, "<ELAPSED>,");
        return LogStampRegex().Replace(text, "<LOG>_");
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\b\d{2}:\d{2}:\d{2}(\.\d+)?\b")]
    private static partial Regex TimeSpanRegex();

    [GeneratedRegex(@"\b\d+(\.\d+)?\s?(ms|s)\b")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"\b\d+\.\d{2},(?=[01],)")]
    private static partial Regex ExportElapsedRegex();

    [GeneratedRegex(@"\b\d{10,}_[0-9a-f]{32}_")]
    private static partial Regex LogStampRegex();
}
