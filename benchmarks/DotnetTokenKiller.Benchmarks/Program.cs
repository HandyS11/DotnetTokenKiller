using BenchmarkDotNet.Running;

namespace DotnetTokenKiller.Benchmarks;

internal static class Program
{
    private const string Usage = """
        Usage:
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks
              Runs the BenchmarkDotNet suite (add -- --filter '*Filter*' to narrow).
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- cold-start
              Times the published dtk binary end to end, out of process.
          dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline
              Regenerates the committed savings baseline.
        """;

    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case ["cold-start", ..]:
                await Console.Error.WriteLineAsync("cold-start is not implemented yet.").ConfigureAwait(false);
                return 1;

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

                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
                await Task.CompletedTask.ConfigureAwait(false);
                return 0;
        }
    }
}
