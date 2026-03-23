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

    /// <inheritdoc/>
    public string ProviderName => "aider";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();

        await WriteFileAsync(
            Path.Combine(directory, ".aider-dtk-instructions.md"),
            InstructionsMarkdown,
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        await WriteAiderConfAsync(
            Path.Combine(directory, ".aider.conf.yml"),
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        return new IntegrationResult(created, updated, skipped);
    }

    private static async Task WriteFileAsync(
        string path,
        string content,
        bool force,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists && !force)
        {
            skipped.Add(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        (exists ? updated : created).Add(path);
    }

    private static async Task WriteAiderConfAsync(
        string path,
        bool force,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists)
        {
            var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (current.Contains(SectionMarker, StringComparison.Ordinal))
            {
                if (!force)
                {
                    skipped.Add(path);
                    return;
                }

                var replaced = ReplaceDtkSection(current);
                await File.WriteAllTextAsync(path, replaced, cancellationToken).ConfigureAwait(false);
                updated.Add(path);
                return;
            }

            var trimmed = current.TrimEnd();
            var appended = string.IsNullOrWhiteSpace(trimmed)
                ? AiderConfSection
                : trimmed + Environment.NewLine + AiderConfSection;
            await File.WriteAllTextAsync(path, appended, cancellationToken).ConfigureAwait(false);
            updated.Add(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, AiderConfSection, cancellationToken).ConfigureAwait(false);
            created.Add(path);
        }
    }

    private static string ReplaceDtkSection(string content)
    {
        var start = content.IndexOf(SectionMarker, StringComparison.Ordinal);
        var end = content.IndexOf(SectionEndMarker, start, StringComparison.Ordinal);

        if (end < 0)
            return content[..start] + AiderConfSection;

        return content[..start] + AiderConfSection + content[(end + SectionEndMarker.Length)..];
    }

    private const string AiderConfSection =
        """
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean.
        read:
          - .aider-dtk-instructions.md
        # /dtk
        """;

    private const string InstructionsMarkdown =
        """
        # DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, and clean commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ## Usage

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        """;
}
