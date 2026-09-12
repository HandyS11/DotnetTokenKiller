using System.Diagnostics;
using System.Globalization;
using DotnetTokenKiller.Benchmarks.Support;
using Microsoft.ML.Tokenizers;

namespace DotnetTokenKiller.Benchmarks;

/// <summary>
/// Times the one-time tiktoken vocabulary load, out of process, one fresh process per sample.
/// </summary>
/// <remarks>
/// <para>
/// This cannot be a BenchmarkDotNet job. <c>Microsoft.ML.Tokenizers</c> caches the parsed
/// vocabulary in process-static state, so only the first <see cref="TiktokenTokenizer"/>
/// <c>CreateForEncoding</c> call in a process pays for it; every later call is a dictionary hit
/// that returns a new tokenizer over cached data. The cache is internal, so neither
/// <c>[IterationSetup]</c> nor <c>RunStrategy.ColdStart</c> can clear it, and BenchmarkDotNet's own
/// jitting and warmup invocations populate it before the first measured iteration. An in-process
/// benchmark therefore reports the cache hit — microseconds — for a load that really costs on the
/// order of a hundred milliseconds, which is worse than not measuring it at all: it argues the next
/// maintainer out of the largest optimization this suite exists to surface.
/// </para>
/// <para>
/// So the parent spawns its own executable with <see cref="ProbeVerb"/> once per sample. Each
/// child times exactly one <c>CreateForEncoding</c> in a fresh process and prints the milliseconds;
/// the parent discards the warmup samples and reports median, p95, min and max of the rest. A child
/// that fails or prints something unparseable fails the whole run loudly, because a harness that
/// silently measured nothing would report an impressively fast load.
/// </para>
/// </remarks>
internal static class TokenizerLoadCommand
{
    /// <summary>
    /// The child-process verb. Not a user-facing command: <see cref="RunAsync"/> spawns it, one
    /// process per sample, and parses its single line of stdout.
    /// </summary>
    internal const string ProbeVerb = "tokenizer-load-probe";

    /// <summary>
    /// Discarded, and not because the vocabulary cache can be warmed across processes — it cannot.
    /// These absorb the first-touch costs that surround the load and are not part of it: the
    /// runner's own assemblies and the tokenizer data file being paged in from disk for the first
    /// time. What is left is the load a second dtk invocation on a warm machine pays, which is the
    /// figure the cold-start number should be read against.
    /// </summary>
    private const int WarmupRuns = 3;

    private const int MeasuredRuns = 10;

    /// <summary>
    /// Both shipped encodings, reported separately. <c>cl100k_base</c> is what
    /// <c>TrackingConfig</c> ships as its default, so it is the one the headline cold-start figure
    /// has to be read against; <c>o200k_base</c> is configurable and has its own, larger vocabulary.
    /// </summary>
    private static readonly string[] Encodings = ["cl100k_base", "o200k_base"];

    internal static async Task<int> RunAsync()
    {
        var launch = ResolveSelfLaunch();

        if (launch is null)
        {
            await Console.Error.WriteLineAsync(
                "Could not resolve this runner's own executable, so the vocabulary load cannot be "
                + "measured in a fresh process. Run it through:\n"
                + "  dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- tokenizer-load")
                .ConfigureAwait(false);
            return 1;
        }

        await Console.Out.WriteLineAsync($"Tokenizer vocabulary load: {launch.Value.FileName}")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Runs per encoding: {WarmupRuns} warmup + {MeasuredRuns} measured, one fresh process each"))
            .ConfigureAwait(false);

        foreach (var encoding in Encodings)
        {
            var samples = new List<double>(MeasuredRuns);

            for (var run = 0; run < WarmupRuns + MeasuredRuns; run++)
            {
                var elapsed = await ProbeOnceAsync(launch.Value, encoding).ConfigureAwait(false);

                if (run >= WarmupRuns)
                {
                    samples.Add(elapsed);
                }
            }

            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"{encoding} (first CreateForEncoding per process)")
                .ConfigureAwait(false);
            await TimingReport.WriteAsync(samples).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// The child half: times exactly one vocabulary load in this fresh process and writes the
    /// elapsed milliseconds to stdout as a single invariant-culture number, and nothing else.
    /// </summary>
    /// <param name="args">The child's own command line, <c>tokenizer-load-probe &lt;encoding&gt;</c>.</param>
    internal static int RunProbe(string[] args)
    {
        if (args is not [_, var encoding, ..] || string.IsNullOrWhiteSpace(encoding))
        {
            Console.Error.WriteLine($"Usage: {ProbeVerb} <encoding>");
            return 1;
        }

        // Deliberately the very first tokenizer call this process makes. Anything that touched a
        // tokenizer earlier — including a warm-up for the sake of "stability" — would populate the
        // library's static vocabulary cache and turn this measurement into the cache hit the
        // in-process benchmark used to report.
        var started = Stopwatch.GetTimestamp();
        _ = TiktokenTokenizer.CreateForEncoding(encoding);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Console.WriteLine(elapsed.ToString("F3", CultureInfo.InvariantCulture));
        return 0;
    }

    /// <summary>Runs one child process and returns the milliseconds it reported.</summary>
    /// <param name="launch">How to re-launch this runner, from <see cref="ResolveSelfLaunch"/>.</param>
    /// <param name="encoding">The tiktoken encoding name the child should load.</param>
    /// <exception cref="InvalidOperationException">
    /// The child could not be started, exited non-zero, or wrote output that is not a single
    /// number. Every one of those would otherwise be silently dropped from the sample set, or
    /// worse, counted as a very fast load.
    /// </exception>
    private static async Task<double> ProbeOnceAsync(
        (string FileName, string? AssemblyArgument) launch, string encoding)
    {
        var info = new ProcessStartInfo(launch.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (launch.AssemblyArgument is not null)
        {
            info.ArgumentList.Add(launch.AssemblyArgument);
        }

        info.ArgumentList.Add(ProbeVerb);
        info.ArgumentList.Add(encoding);

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start {launch.FileName}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The {ProbeVerb} child for '{encoding}' exited with code {process.ExitCode}. "
                + $"Output:\n{(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}");
        }

        if (!double.TryParse(
                stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
        {
            throw new InvalidOperationException(
                $"The {ProbeVerb} child for '{encoding}' printed '{stdout.Trim()}', which is not a "
                + "millisecond count. Refusing to report a statistic over samples that were never "
                + $"measured.{(string.IsNullOrWhiteSpace(stderr) ? string.Empty : $"\nStderr:\n{stderr}")}");
        }

        return milliseconds;
    }

    /// <summary>
    /// Resolves how to spawn this runner again. <see cref="Environment.ProcessPath"/> is the
    /// apphost under <c>dotnet run</c>, which is directly spawnable; under
    /// <c>dotnet Benchmarks.dll</c> it is the muxer, which needs the assembly path handed back as
    /// its first argument. Returns <see langword="null"/> if neither can be determined, rather than
    /// guessing at a path that would fail one sample at a time.
    /// </summary>
    private static (string FileName, string? AssemblyArgument)? ResolveSelfLaunch()
    {
        if (Environment.ProcessPath is not { Length: > 0 } processPath)
        {
            return null;
        }

        if (!string.Equals(
                Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, null);
        }

        var assembly = typeof(TokenizerLoadCommand).Assembly.Location;
        return string.IsNullOrEmpty(assembly) ? null : (processPath, assembly);
    }
}
