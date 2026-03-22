using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for GitHub Copilot.</summary>
/// <remarks>
/// Appends (or creates) a dtk section in <c>.github/copilot-instructions.md</c>.
/// </remarks>
public sealed class GitHubCopilotIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";

    /// <inheritdoc/>
    public string ProviderName => "copilot";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();

        var path = Path.Combine(directory, ".github", "copilot-instructions.md");
        var exists = File.Exists(path);

        if (exists)
        {
            var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (current.Contains(SectionMarker, StringComparison.Ordinal))
            {
                if (!force)
                {
                    skipped.Add(path);
                    return new IntegrationResult(created, updated, skipped);
                }

                // Replace the dtk section in-place.
                var replaced = ReplaceDtkSection(current);
                await File.WriteAllTextAsync(path, replaced, cancellationToken).ConfigureAwait(false);
                updated.Add(path);
                return new IntegrationResult(created, updated, skipped);
            }

            // File exists but has no dtk section yet — append.
            var trimmed = current.TrimEnd();
            var appended = string.IsNullOrWhiteSpace(trimmed)
                ? CopilotSection
                : trimmed + Environment.NewLine + CopilotSection;
            await File.WriteAllTextAsync(path, appended, cancellationToken).ConfigureAwait(false);
            updated.Add(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, CopilotSection, cancellationToken).ConfigureAwait(false);
            created.Add(path);
        }

        return new IntegrationResult(created, updated, skipped);
    }

    private static string ReplaceDtkSection(string content)
    {
        const string endMarker = "<!-- /dtk -->";
        var start = content.IndexOf(SectionMarker, StringComparison.Ordinal);
        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
            return content[..start] + CopilotSection;

        return content[..start] + CopilotSection + content[(end + endMarker.Length)..];
    }

    private const string CopilotSection =
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
