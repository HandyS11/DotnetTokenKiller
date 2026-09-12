using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace DotnetTokenKiller.Benchmarks;

internal static class Program
{
    private const string Usage = """
        Usage:
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks
              Runs the BenchmarkDotNet suite (add -- --filter '*Filter*' to narrow).
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start
              Times the built dtk binary end to end, out of process: piped, and wrapping a fake dotnet.
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- tokenizer-load
              Times the one-time tiktoken vocabulary load, one fresh process per sample.
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline
              Regenerates the committed savings baseline.
        """;

    /// <summary>
    /// BenchmarkDotNet switches that legitimately produce no summaries because they were never
    /// asked to run anything. Everything else that produces none ran nothing by accident — most
    /// likely a filter that matched no benchmark — and must not exit 0.
    /// </summary>
    private static readonly string[] InformationalSwitches =
        ["--list", "--help", "-h", "--version", "--info", "-i"];

    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case ["cold-start", ..]:
                return await ColdStartCommand.RunAsync(args).ConfigureAwait(false);

            case ["tokenizer-load", ..]:
                return await TokenizerLoadCommand.RunAsync().ConfigureAwait(false);

            // Internal: one child process per sample, spawned by the verb above. Times a single
            // vocabulary load in this fresh process and prints the milliseconds it took.
            case [TokenizerLoadCommand.ProbeVerb, ..]:
                return TokenizerLoadCommand.RunProbe(args);

            case ["update-baseline", ..]:
                return UpdateBaselineCommand.Run();

            default:
                // Anything else, including no arguments, belongs to BenchmarkDotNet: it owns
                // --filter, --list, --job and the rest of its own command line.
                if (args is [var first, ..] && !first.StartsWith('-'))
                {
                    await Console.Error.WriteLineAsync($"Unknown verb '{first}'.").ConfigureAwait(false);
                    await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
                    return 1;
                }

                var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToList();
                return await ExitCodeForAsync(args, summaries).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Turns a BenchmarkDotNet run into an exit code. <c>BenchmarkSwitcher.Run</c> reports "no
    /// benchmarks found" and validation failures by returning and printing, never by throwing or
    /// by exiting non-zero, so a typo in the Benchmarks workflow's <c>filter</c> input would
    /// otherwise be a green run whose artifact measured nothing at all.
    /// </summary>
    /// <param name="args">The arguments handed to BenchmarkDotNet.</param>
    /// <param name="summaries">The summaries it returned, one per benchmark class that ran.</param>
    private static async Task<int> ExitCodeForAsync(string[] args, List<Summary> summaries)
    {
        if (summaries.Count == 0)
        {
            if (args.Any(IsInformational))
            {
                return 0;
            }

            await Console.Error.WriteLineAsync(
                "No benchmark ran. Check the --filter glob against `-- --list flat`.").ConfigureAwait(false);
            return 1;
        }

        var invalid = summaries.Count(summary => summary.HasCriticalValidationErrors);
        if (invalid > 0)
        {
            await Console.Error.WriteLineAsync(
                $"{invalid} benchmark(s) failed validation; their results are not usable.")
                .ConfigureAwait(false);
            return 1;
        }

        return 0;
    }

    /// <summary>Whether an argument is one of the switches that asks BenchmarkDotNet for
    /// information rather than for a run.</summary>
    /// <param name="arg">A single command-line argument.</param>
    private static bool IsInformational(string arg) => InformationalSwitches.Any(
        option => arg.Equals(option, StringComparison.Ordinal)
                  || arg.StartsWith(option + "=", StringComparison.Ordinal));
}
