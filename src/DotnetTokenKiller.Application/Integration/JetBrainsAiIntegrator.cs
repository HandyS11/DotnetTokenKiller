using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for JetBrains AI (Junie).</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.junie/guidelines.md</c> (JetBrains AI project guidelines)</description></item>
/// </list>
/// </remarks>
public sealed class JetBrainsAiIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";

    /// <inheritdoc/>
    public string ProviderName => "jetbrains";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();

        await WriteGuidelinesAsync(
            Path.Combine(directory, ".junie", "guidelines.md"),
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        return new IntegrationResult(created, updated, skipped);
    }

    private static async Task WriteGuidelinesAsync(
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
                ? GuidelinesSection
                : trimmed + Environment.NewLine + GuidelinesSection;
            await File.WriteAllTextAsync(path, appended, cancellationToken).ConfigureAwait(false);
            updated.Add(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, GuidelinesSection, cancellationToken).ConfigureAwait(false);
            created.Add(path);
        }
    }

    private static string ReplaceDtkSection(string content)
    {
        var start = content.IndexOf(SectionMarker, StringComparison.Ordinal);
        var end = content.IndexOf(SectionEndMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            return content[..start] + GuidelinesSection;
        }

        return content[..start] + GuidelinesSection + content[(end + SectionEndMarker.Length)..];
    }

    private const string GuidelinesSection =
        """
        <!-- dtk -->
        ## DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, and clean commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        <!-- /dtk -->
        """;
}
