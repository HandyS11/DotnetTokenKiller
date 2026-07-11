using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Aider.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.aider-dtk-instructions.md</c> (instructions file read into context)</description></item>
///   <item><description><c>.aider.conf.yml</c> (Aider configuration, section-based merge)</description></item>
/// </list>
/// </remarks>
public sealed class AiderIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "# dtk";
    private const string SectionEndMarker = "# /dtk";
    private const string ReadKeyPrefix = "read:";
    private const string BlockItemPrefix = "- ";
    private const string InstructionsFileName = ".aider-dtk-instructions.md";

    private const string AiderConfSection =
        """
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean/format.
        read:
          - .aider-dtk-instructions.md
        # /dtk
        """;

    /// <summary>
    /// Used instead of <see cref="AiderConfSection"/> when the file already declares a top-level
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
        """
        # DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ## Usage

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        dtk dotnet format
        dtk dotnet format --verify-no-changes
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        """;

    /// <inheritdoc/>
    public string ProviderName => "aider";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(directory, InstructionsFileName),
            InstructionsMarkdown, context, cancellationToken).ConfigureAwait(false);

        var confPath = Path.Combine(directory, ".aider.conf.yml");
        var confSection = await MergeExistingReadKeyAsync(confPath, cancellationToken).ConfigureAwait(false)
            ? AiderConfSectionWithoutReadKey
            : AiderConfSection;

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            confPath, SectionMarker, SectionEndMarker, confSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <summary>
    /// If <paramref name="confPath"/> already declares a top-level <c>read:</c> key outside the
    /// dtk-managed section, merges <see cref="InstructionsFileName"/> into that key's list (both
    /// flow style <c>read: [a, b]</c> and block style <c>read:\n  - a</c> are supported) instead of
    /// letting the dtk section declare a second top-level <c>read:</c> key that would shadow it.
    /// </summary>
    /// <param name="confPath">Path to the Aider configuration file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when an existing top-level <c>read:</c> key was found (whether or not
    /// the file needed a change); <see langword="false"/> when no such key exists, meaning the dtk
    /// section should declare its own.
    /// </returns>
    private static async Task<bool> MergeExistingReadKeyAsync(string confPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(confPath))
        {
            return false;
        }

        var content = await File.ReadAllTextAsync(confPath, cancellationToken).ConfigureAwait(false);
        if (!TryMergeExistingReadKey(ref content))
        {
            return false;
        }

        await File.WriteAllTextAsync(confPath, content, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool TryMergeExistingReadKey(ref string content)
    {
        var lines = content.Split('\n');
        var readLineIndex = FindExternalReadKeyIndex(lines);

        if (readLineIndex < 0)
        {
            return false;
        }

        var afterColon = lines[readLineIndex][ReadKeyPrefix.Length..].Trim();
        var updatedLines = afterColon.Length == 0
            ? MergeBlockStyle(lines, readLineIndex)
            : MergeFlowStyle(lines, readLineIndex, afterColon);

        content = string.Join('\n', updatedLines);
        return true;
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
    /// <param name="afterColon">The trimmed content following <c>read:</c> on that line.</param>
    /// <returns>The updated lines, unchanged if the instructions file is already listed.</returns>
    private static string[] MergeFlowStyle(string[] lines, int readLineIndex, string afterColon)
    {
        var trimmed = afterColon;
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        List<string> items = trimmed.Length == 0
            ? []
            : [.. trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        if (items.Any(item => item.Trim('"', '\'').Equals(InstructionsFileName, StringComparison.Ordinal)))
        {
            return lines;
        }

        items.Add(InstructionsFileName);

        var updated = (string[])lines.Clone();
        updated[readLineIndex] = $"{ReadKeyPrefix} [{string.Join(", ", items)}]";
        return updated;
    }

    /// <summary>Merges into a block-style <c>read:</c> key whose items are indented <c>- item</c> lines below it.</summary>
    /// <param name="lines">The configuration file's content, split into lines.</param>
    /// <param name="readLineIndex">Index of the line declaring the top-level <c>read:</c> key.</param>
    /// <returns>The updated lines, unchanged if the instructions file is already listed.</returns>
    private static string[] MergeBlockStyle(string[] lines, int readLineIndex)
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
            if (itemValue.Equals(InstructionsFileName, StringComparison.Ordinal))
            {
                return lines;
            }

            insertIndex++;
        }

        var updated = new List<string>(lines);
        updated.Insert(insertIndex, indent + BlockItemPrefix + InstructionsFileName);
        return [.. updated];
    }
}
