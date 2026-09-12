using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Benchmarks.Corpus;

/// <summary>
/// Produces deterministic, realistically shaped <c>dotnet</c> output at three size tiers.
/// </summary>
/// <remarks>
/// The captured fixtures are real but small — none exceeds 8 KB, while a large solution's build
/// output runs to megabytes. Every filter applies regexes per line across the whole log, so a
/// quadratic cannot be observed at fixture scale. Generating the large tier rather than committing
/// a captured multi-megabyte log keeps the repository small and lets the tier be re-scaled later.
/// </remarks>
public static class LogCorpusGenerator
{
    /// <summary>
    /// Fixed so that a tier is byte-identical on every machine and every run. A benchmark whose
    /// input varies between runs reports its own noise as a change.
    /// </summary>
    private const int Seed = 20260912;

    private const string Root = "/repo";

    private static readonly FrozenDictionary<string, string[]> Templates =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [FilterKeys.Build] =
            [
                "  Determining projects to restore...",
                "  All projects are up-to-date for restore.",
                Root + "/src/Project{0}/Service{1}.cs({2},{3}): warning CA1822: Member 'Handle{1}' does not access instance data and can be marked as static [" + Root + "/src/Project{0}/Project{0}.csproj]",
                Root + "/src/Project{0}/Model{1}.cs({2},{3}): warning CS8618: Non-nullable property 'Name{1}' must contain a non-null value when exiting constructor [" + Root + "/src/Project{0}/Project{0}.csproj]",
                Root + "/src/Project{0}/Repository{1}.cs({2},{3}): warning S1481: Remove the unused local variable 'temp{1}' [" + Root + "/src/Project{0}/Project{0}.csproj]",
                "    Project{0} -> " + Root + "/src/Project{0}/bin/Release/net10.0/Project{0}.dll",
            ],
            [FilterKeys.Test] =
            [
                "  Determining projects to restore...",
                "    Project{0}.Tests -> " + Root + "/tests/Project{0}.Tests/bin/Release/net10.0/Project{0}.Tests.dll",
                "  Passed Project{0}.Tests.Service{1}Tests.Returns_TheExpectedValue [{2} ms]",
                "  Passed Project{0}.Tests.Service{1}Tests.Throws_WhenInputIsNull [{2} ms]",
                "  Skipped Project{0}.Tests.Service{1}Tests.Handles_TheLegacyShape",
            ],
            [FilterKeys.Restore] =
            [
                "  Determining projects to restore...",
                "  Restored " + Root + "/src/Project{0}/Project{0}.csproj (in {2} ms).",
                "  All projects are up-to-date for restore.",
            ],
            [FilterKeys.Clean] =
            [
                "  Determining projects to restore...",
                "  Project{0} -> " + Root + "/src/Project{0}/bin/Release/net10.0/Project{0}.dll",
                "  Cleaning " + Root + "/src/Project{0}/obj/Release/net10.0/Project{0}.assets.cache",
            ],
            [FilterKeys.Format] =
            [
                "  Loading workspace " + Root + "/DotnetTokenKiller.slnx.",
                Root + "/src/Project{0}/Service{1}.cs({2},{3}): error whitespace: Fix whitespace formatting.",
                "  Formatted code file 'src/Project{0}/Service{1}.cs'.",
            ],
            [FilterKeys.ListPackage] =
            [
                "Project 'Project{0}' has the following package references",
                "   [net10.0]: ",
                "   Top-level Package                        Requested   Resolved",
                "   > Package.Number{1}                      1.{2}.0     1.{2}.0",
            ],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns the approximate byte size the given tier aims for.</summary>
    /// <param name="tier">The tier to size.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tier has no known target size.</exception>
    public static int TargetBytes(CorpusTier tier) => tier switch
    {
        CorpusTier.Small => 2 * 1024,
        CorpusTier.Medium => 50 * 1024,
        CorpusTier.Large => 1024 * 1024,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown tier."),
    };

    /// <summary>Generates a log of roughly <see cref="TargetBytes"/> bytes for the given filter.</summary>
    /// <param name="filterKey">A <see cref="FilterKeys"/> constant naming the output shape.</param>
    /// <param name="tier">The size tier to generate.</param>
    /// <exception cref="ArgumentOutOfRangeException">The filter key has no line templates.</exception>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Reproducibility is the requirement. A seeded Random makes each tier "
                        + "byte-identical across machines; a cryptographic source would make "
                        + "every benchmark run measure a different input.")]
    [SuppressMessage("Major Code Smell", "S2245:Using pseudorandom number generators is security-sensitive",
        Justification = "See CA5394: the seeded sequence is the point, and no security decision "
                        + "depends on it.")]
    public static string Generate(string filterKey, CorpusTier tier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterKey);

        if (!Templates.TryGetValue(filterKey, out var templates))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filterKey), filterKey, "No line templates for this filter key.");
        }

        var target = TargetBytes(tier);
        var random = new Random(Seed);
        var builder = new StringBuilder(target + 512);

        // The trailing summary counts toward the tier target too, so the line-by-line body must
        // stop that many bytes short of it — otherwise the summary is pure overshoot on top of an
        // already-at-target body, and that overshoot is a fixed number of bytes regardless of tier,
        // which is negligible at the Large tier but enough to clear the Small tier's 10% band.
        var summary = Summary(filterKey);
        var bodyTarget = target - summary.Length;

        var index = 0;
        do
        {
            var template = templates[index % templates.Length];
            builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    template,
                    random.Next(1, 40),
                    random.Next(1, 400),
                    random.Next(1, 900),
                    random.Next(1, 80))
                .AppendLine();
            index++;
        } while (builder.Length < bodyTarget);

        builder.Append(summary);
        return builder.ToString();
    }

    /// <summary>
    /// The trailing summary block. Filters read the tail for their verdict, so a generated log
    /// without one is not a shape they would ever be handed in practice.
    /// </summary>
    /// <param name="filterKey">A <see cref="FilterKeys"/> constant naming the output shape.</param>
    private static string Summary(string filterKey) => filterKey switch
    {
        FilterKeys.Build =>
            "\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n\nTime Elapsed 00:00:12.3456789\n",
        FilterKeys.Test =>
            "\nPassed!  - Failed:     0, Passed:  1200, Skipped:    40, Total:  1240, Duration: 8 s\n",
        FilterKeys.Restore => "\nRestore succeeded.\n",
        FilterKeys.Clean => "\nBuild succeeded.\n    0 Warning(s)\n    0 Error(s)\n",
        FilterKeys.Format => "\nFormat complete.\n",
        _ => "\n",
    };
}
