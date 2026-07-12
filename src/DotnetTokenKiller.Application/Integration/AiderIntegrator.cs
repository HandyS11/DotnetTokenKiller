using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Aider.</summary>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.aider-dtk-instructions.md</c> (instructions file read into context)</description></item>
///   <item><description><c>.aider.conf.yml</c> (Aider configuration, section-based merge)</description></item>
/// </list>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the <see langword="internal"/>
/// <see cref="HomePaths"/>: a primary constructor is as accessible as its containing type, and the
/// compiler rejects (CS0051) a public constructor exposing a less-accessible parameter type. See
/// <see cref="GeminiCliIntegrator"/> for the same pattern. Callers still reach it polymorphically
/// through the public <see cref="IProviderIntegrator"/> via DI, and tests reach it directly via
/// <c>InternalsVisibleTo</c>.
/// </remarks>
internal sealed class AiderIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator
{
    private const string SectionMarker = "# dtk";
    private const string SectionEndMarker = "# /dtk";
    private const string ReadKeyPrefix = "read:";
    private const string BlockItemPrefix = "- ";
    private const string InstructionsFileName = ".aider-dtk-instructions.md";

    /// <summary>Builds the dtk-managed conf section declaring a top-level <c>read:</c> key pointing at <paramref name="readTarget"/>.</summary>
    /// <param name="readTarget">
    /// The relative instructions filename for local integration, or the absolute
    /// <see cref="HomePaths.AiderInstructionsPath"/> for global integration (a home-level conf
    /// cannot rely on a cwd-relative filename resolving).
    /// </param>
    private static string BuildConfSection(string readTarget) =>
        $"""
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean/format.
        read:
          - {readTarget}
        # /dtk
        """;

    /// <summary>
    /// Used instead of <see cref="BuildConfSection"/> when the file already declares a top-level
    /// <c>read:</c> key: the instructions file is merged into that existing key (see
    /// <see cref="TryMergeExistingReadKey"/>) rather than declared again here, which would create a
    /// second top-level <c>read:</c> key that shadows the user's entries under YAML's
    /// last-key-wins semantics.
    /// </summary>
    private const string AiderConfSectionWithoutReadKey =
        """
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean/format.
        # (merged into the existing top-level "read:" key above instead of declaring a new one)
        # /dtk
        """;

    private const string InstructionsMarkdown =
        $"""
        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <inheritdoc/>
    public string ProviderName => "aider";

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, InstructionsFileName),
            Path.Combine(directory, ".aider.conf.yml"),
            InstructionsFileName,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            home.AiderInstructionsPath,
            home.AiderConfPath,
            home.AiderInstructionsPath,
            force,
            cancellationToken);

    private static async Task<IntegrationResult> IntegrateCoreAsync(
        string instructionsPath,
        string confPath,
        string readTarget,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            instructionsPath, InstructionsMarkdown, context, cancellationToken).ConfigureAwait(false);

        var confSection = await PrepareConfSectionAsync(confPath, readTarget, force, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            confPath, SectionMarker, SectionEndMarker, confSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <summary>
    /// Decides which dtk section to write and, when the section write will actually proceed,
    /// merges the resolved <c>read:</c> target (<paramref name="readTarget"/>) into an existing
    /// top-level <c>read:</c> key outside the dtk-managed section (both flow style <c>read: [a, b]</c> and block style
    /// <c>read:\n  - a</c>) instead of letting the dtk section declare a second top-level
    /// <c>read:</c> key that would shadow it.
    /// The skip decision is delegated to <see cref="IntegratorHelpers.ShouldSkipWrite"/> — the same
    /// predicate <see cref="IntegratorHelpers.WriteSectionBasedFileAsync"/> itself consults — so the
    /// two can never drift apart. When it says the write will be skipped (an existing file without
    /// <paramref name="force"/>, regardless of whether the dtk marker is present), this method
    /// leaves the file completely untouched (no merge, no write), honoring the "Skipped means no
    /// changes" contract.
    /// </summary>
    /// <param name="confPath">Path to the Aider configuration file.</param>
    /// <param name="readTarget">
    /// The value to declare/merge under the <c>read:</c> key: the relative instructions filename
    /// for local integration, or the absolute <see cref="HomePaths.AiderInstructionsPath"/> for
    /// global integration.
    /// </param>
    /// <param name="force">Whether the integration is running with the force flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="AiderConfSectionWithoutReadKey"/> when an external <c>read:</c> key exists and
    /// was merged into (so the written section must not contribute a second top-level key);
    /// <see cref="BuildConfSection"/>'s result otherwise.
    /// </returns>
    private static async Task<string> PrepareConfSectionAsync(
        string confPath,
        string readTarget,
        bool force,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(confPath);
        if (!exists || IntegratorHelpers.ShouldSkipWrite(exists, force))
        {
            return BuildConfSection(readTarget);
        }

        var content = await File.ReadAllTextAsync(confPath, cancellationToken).ConfigureAwait(false);
        if (!TryMergeExistingReadKey(ref content, readTarget))
        {
            return BuildConfSection(readTarget);
        }

        await File.WriteAllTextAsync(confPath, content, cancellationToken).ConfigureAwait(false);
        return AiderConfSectionWithoutReadKey;
    }

    private static bool TryMergeExistingReadKey(ref string content, string readTarget)
    {
        var lines = content.Split('\n');
        var readLineIndex = FindExternalReadKeyIndex(lines);

        if (readLineIndex < 0)
        {
            return false;
        }

        var afterColon = lines[readLineIndex][ReadKeyPrefix.Length..].Trim();
        var (value, comment) = SplitTrailingComment(afterColon);
        var updatedLines = value.Length == 0
            ? MergeBlockStyle(lines, readLineIndex, readTarget)
            : MergeFlowStyle(lines, readLineIndex, value, comment, readTarget);

        content = string.Join('\n', updatedLines);
        return true;
    }

    /// <summary>
    /// Splits a trailing YAML comment (a <c>#</c> preceded by whitespace, or at the start, outside
    /// any flow-sequence brackets) off the text following the <c>read:</c> key, so a commented key
    /// line is neither mistaken for a flow value nor spliced into the rebuilt list.
    /// </summary>
    /// <param name="text">The trimmed text following <c>read:</c> on the key line.</param>
    /// <returns>The value with the comment removed (trimmed), and the comment itself (empty when none).</returns>
    private static (string Value, string Comment) SplitTrailingComment(string text)
    {
        var bracketDepth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth--;
                    break;
                case '#' when bracketDepth == 0 && (i == 0 || char.IsWhiteSpace(text[i - 1])):
                    return (text[..i].TrimEnd(), text[i..]);
            }
        }

        return (text, string.Empty);
    }

    /// <summary>Finds the first top-level <c>read:</c> line outside the dtk-managed marker block, if any.</summary>
    /// <param name="lines">The configuration file's content, split into lines.</param>
    /// <returns>The index of the matching line, or -1 if none is found.</returns>
    private static int FindExternalReadKeyIndex(string[] lines)
    {
        var insideManagedSection = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.Contains(SectionMarker, StringComparison.Ordinal))
            {
                insideManagedSection = true;
                continue;
            }

            if (line.Contains(SectionEndMarker, StringComparison.Ordinal))
            {
                insideManagedSection = false;
                continue;
            }

            if (!insideManagedSection && line.StartsWith(ReadKeyPrefix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Merges into a flow-style <c>read: [a, b]</c> (or bare scalar <c>read: a</c>) line.</summary>
    /// <param name="lines">The configuration file's content, split into lines.</param>
    /// <param name="readLineIndex">Index of the line declaring the top-level <c>read:</c> key.</param>
    /// <param name="value">The content following <c>read:</c> on that line, trimmed and with any trailing comment removed.</param>
    /// <param name="comment">The trailing comment removed from that line (empty when none); re-appended to the rebuilt line.</param>
    /// <param name="readTarget">The value to merge into the list (relative filename for local, absolute path for global).</param>
    /// <returns>The updated lines, unchanged if <paramref name="readTarget"/> is already listed.</returns>
    private static string[] MergeFlowStyle(string[] lines, int readLineIndex, string value, string comment, string readTarget)
    {
        var trimmed = value;
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        List<string> items = trimmed.Length == 0
            ? []
            : [.. trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        if (items.Any(item => item.Trim('"', '\'').Equals(readTarget, StringComparison.Ordinal)))
        {
            return lines;
        }

        items.Add(readTarget);

        var commentSuffix = comment.Length == 0 ? string.Empty : "  " + comment;
        var lineEnding = lines[readLineIndex].EndsWith('\r') ? "\r" : string.Empty;
        var updated = (string[])lines.Clone();
        updated[readLineIndex] = $"{ReadKeyPrefix} [{string.Join(", ", items)}]{commentSuffix}{lineEnding}";
        return updated;
    }

    /// <summary>Merges into a block-style <c>read:</c> key whose items are indented <c>- item</c> lines below it.</summary>
    /// <param name="lines">The configuration file's content, split into lines.</param>
    /// <param name="readLineIndex">Index of the line declaring the top-level <c>read:</c> key.</param>
    /// <param name="readTarget">The value to insert into the list (relative filename for local, absolute path for global).</param>
    /// <returns>The updated lines, unchanged if <paramref name="readTarget"/> is already listed.</returns>
    private static string[] MergeBlockStyle(string[] lines, int readLineIndex, string readTarget)
    {
        var indent = "  ";
        var insertIndex = readLineIndex + 1;

        while (insertIndex < lines.Length)
        {
            var line = lines[insertIndex];
            var trimmedStart = line.TrimStart();
            var indentLength = line.Length - trimmedStart.Length;

            if (indentLength == 0 || !trimmedStart.StartsWith(BlockItemPrefix, StringComparison.Ordinal))
            {
                break;
            }

            indent = line[..indentLength];
            var itemValue = trimmedStart[BlockItemPrefix.Length..].Trim().Trim('"', '\'');
            if (itemValue.Equals(readTarget, StringComparison.Ordinal))
            {
                return lines;
            }

            insertIndex++;
        }

        var lineEnding = lines[readLineIndex].EndsWith('\r') ? "\r" : string.Empty;
        var updated = new List<string>(lines);
        updated.Insert(insertIndex, indent + BlockItemPrefix + readTarget + lineEnding);
        return [.. updated];
    }
}
