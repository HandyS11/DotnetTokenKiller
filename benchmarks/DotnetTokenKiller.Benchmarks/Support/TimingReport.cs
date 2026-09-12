using System.Globalization;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// Shared reporting for the hand-written out-of-process harnesses (<c>cold-start</c> and
/// <c>tokenizer-load</c>). Both discard warmup samples and report the same four statistics over
/// what is left, and two copies of a percentile would be free to disagree.
/// </summary>
internal static class TimingReport
{
    /// <summary>Prints median, p95, min and max of one sample set, in milliseconds.</summary>
    /// <param name="samples">The measured samples; sorted in place.</param>
    /// <exception cref="ArgumentException"><paramref name="samples"/> is empty.</exception>
    internal static async Task WriteAsync(List<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        samples.Sort();
        await WriteLineAsync("median", Percentile(samples, 0.50)).ConfigureAwait(false);
        await WriteLineAsync("p95", Percentile(samples, 0.95)).ConfigureAwait(false);
        await WriteLineAsync("min", samples[0]).ConfigureAwait(false);
        await WriteLineAsync("max", samples[^1]).ConfigureAwait(false);
    }

    /// <summary>Nearest-rank percentile over the sorted samples.</summary>
    /// <param name="sorted">The samples, already sorted ascending.</param>
    /// <param name="fraction">The percentile to compute, in the range [0, 1].</param>
    /// <exception cref="ArgumentException"><paramref name="sorted"/> is empty.</exception>
    internal static double Percentile(List<double> sorted, double fraction)
    {
        ArgumentNullException.ThrowIfNull(sorted);

        if (sorted.Count == 0)
        {
            throw new ArgumentException("Cannot compute a percentile of an empty sample set.", nameof(sorted));
        }

        var rank = (int)Math.Ceiling(fraction * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static Task WriteLineAsync(string label, double milliseconds) => Console.Out.WriteLineAsync(
        string.Create(CultureInfo.InvariantCulture, $"  {label,-8} {milliseconds,8:F1} ms"));
}
