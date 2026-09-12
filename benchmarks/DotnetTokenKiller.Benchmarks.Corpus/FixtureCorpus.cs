using System.Collections.Immutable;
using System.Reflection;

namespace DotnetTokenKiller.Benchmarks.Corpus;

/// <summary>
/// Reads the real captured <c>dotnet</c> outputs that the Application filter tests assert against.
/// These drive the savings baseline: the published savings percentages have to come from output
/// dotnet actually produced, not from generated approximations of it.
/// </summary>
public static class FixtureCorpus
{
    private const string ResourceMarker = ".Fixtures.";

    private static readonly Assembly OwningAssembly = typeof(FixtureCorpus).Assembly;

    private static readonly ImmutableDictionary<string, string> ResourcesByName =
        OwningAssembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal))
            .ToImmutableDictionary(
                name => name[(name.IndexOf(ResourceMarker, StringComparison.Ordinal)
                              + ResourceMarker.Length)..],
                name => name,
                StringComparer.Ordinal);

    /// <summary>Every embedded fixture's file name, ordinal-sorted for stable iteration.</summary>
    public static ImmutableArray<string> Names { get; } =
        [.. ResourcesByName.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Returns the full text of the named fixture.</summary>
    /// <param name="name">The fixture file name, e.g. <c>dotnet_build_errors.txt</c>.</param>
    /// <exception cref="InvalidOperationException">No fixture with that name is embedded.</exception>
    public static string Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ResourcesByName.TryGetValue(name, out var resource))
        {
            throw new InvalidOperationException(
                $"No embedded fixture named '{name}'. Known fixtures: {string.Join(", ", Names)}.");
        }

        using var stream = OwningAssembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException(
                               $"Embedded fixture '{resource}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
