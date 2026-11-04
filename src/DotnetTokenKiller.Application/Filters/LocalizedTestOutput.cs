using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// Recognises <c>dotnet test</c> output printed in a language other than English, which the test filter cannot
/// parse: piped output from a run with <c>DOTNET_CLI_UI_LANGUAGE</c> (or the system language) set to another
/// culture. A wrapped run always prints English, because dtk sets that variable for the child.
/// </summary>
/// <remarks>
/// It looks for the lines that close a run, in every culture the .NET SDK ships
/// (<see cref="LocalizedTestSummaryStrings"/>, read from the SDK's own resources): VSTest's
/// <c>Passed!</c>/<c>Failed!</c>/<c>Skipped!</c> summary and <c>Test Run …</c> verdict, and the
/// <c>Test run summary:</c> header of a Microsoft.Testing.Platform run. A format's placeholders stand for at least
/// one character each; its literal parts must appear in order, the first one at the start of the line (leading
/// whitespace aside). Any whitespace matches any other, so a no-break space typed as a plain one still matches.
/// </remarks>
public static partial class LocalizedTestOutput
{
    private static readonly SummaryPattern[] Patterns =
        [.. LocalizedTestSummaryStrings.Entries.Select(entry => SummaryPattern.Parse(entry.Culture, entry.Format))];

    /// <summary>
    /// Returns the culture of the first line that is a localized test summary line, or <see langword="null"/>
    /// when there is none. English output never matches.
    /// </summary>
    /// <param name="strippedOutput">The test output, with ANSI escape sequences already stripped.</param>
    /// <returns>
    /// The culture name, such as <c>fr</c>, <c>de</c>, <c>ja</c> or <c>zh-Hans</c>; or <see langword="null"/>.
    /// </returns>
    public static string? Detect(string strippedOutput)
    {
        ArgumentNullException.ThrowIfNull(strippedOutput);

        foreach (var rawLine in strippedOutput.AsSpan().EnumerateLines())
        {
            var line = NormalizeSpaces(rawLine.TrimStart());
            if (line.Length == 0)
            {
                continue;
            }

            foreach (var pattern in Patterns)
            {
                if (pattern.Matches(line))
                {
                    return pattern.Culture;
                }
            }
        }

        return null;
    }

    /// <summary>Turns every whitespace character into a plain space; returns the line itself if it has none.</summary>
    /// <param name="line">The line to normalize.</param>
    private static ReadOnlySpan<char> NormalizeSpaces(ReadOnlySpan<char> line)
    {
        var first = IndexOfOtherWhiteSpace(line);
        if (first < 0)
        {
            return line;
        }

        var copy = line.ToArray();
        for (var i = first; i < copy.Length; i++)
        {
            if (char.IsWhiteSpace(copy[i]))
            {
                copy[i] = ' ';
            }
        }

        return copy;
    }

    private static int IndexOfOtherWhiteSpace(ReadOnlySpan<char> line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] != ' ' && char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A composite format placeholder: <c>{0}</c>, <c>{1,5}</c>, <c>{0:0.0000}</c>.</summary>
    [GeneratedRegex(@"\{\d+(?:,-?\d+)?(?::[^{}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    /// <summary>One culture's summary-line format, as its literal parts in order.</summary>
    /// <param name="Culture">The culture name.</param>
    /// <param name="Literals">
    /// The literal text between placeholders, whitespace normalized, without empty parts. The first one is
    /// anchored at the start of the line unless <paramref name="StartsWithPlaceholder"/> is set.
    /// </param>
    /// <param name="StartsWithPlaceholder">Whether the format opens with a placeholder.</param>
    /// <param name="EndsWithPlaceholder">Whether the format closes with a placeholder.</param>
    private sealed record SummaryPattern(
        string Culture, string[] Literals, bool StartsWithPlaceholder, bool EndsWithPlaceholder)
    {
        /// <summary>Splits a composite format string (<c>{0} - échec : {1}, …</c>) at its placeholders.</summary>
        /// <param name="culture">The culture name.</param>
        /// <param name="format">The raw format string.</param>
        public static SummaryPattern Parse(string culture, string format)
        {
            var parts = PlaceholderPattern().Split(format.TrimStart());
            var literals = parts
                .Where(part => part.Length > 0)
                .Select(part => new string(NormalizeSpaces(part
                    .Replace("{{", "{", StringComparison.Ordinal)
                    .Replace("}}", "}", StringComparison.Ordinal))))
                .ToArray();

            return new SummaryPattern(culture, literals, parts[0].Length == 0, parts[^1].Length == 0);
        }

        /// <summary>
        /// Whether <paramref name="line"/> (trimmed at the start, whitespace normalized) reads as this format. Each
        /// literal is taken at its earliest position, which is enough for literals separated by
        /// at-least-one-character gaps: a later position can only leave less room for the rest.
        /// </summary>
        /// <param name="line">The line to test.</param>
        public bool Matches(ReadOnlySpan<char> line)
        {
            if (Literals.Length == 0)
            {
                return false;
            }

            var position = 0;
            for (var i = 0; i < Literals.Length; i++)
            {
                var literal = Literals[i].AsSpan();
                var anchored = i == 0 && !StartsWithPlaceholder;
                if (anchored)
                {
                    if (!line.StartsWith(literal, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    position = literal.Length;
                    continue;
                }

                // A placeholder stands before this literal: it takes at least one character.
                var from = position + 1;
                if (from > line.Length)
                {
                    return false;
                }

                var found = line[from..].IndexOf(literal, StringComparison.Ordinal);
                if (found < 0)
                {
                    return false;
                }

                position = from + found + literal.Length;
            }

            return !EndsWithPlaceholder || position < line.Length;
        }
    }
}
