using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Detects an rtk PreToolUse hook and reconciles rtk's config so that dtk — not rtk — owns
/// <c>dotnet build/test/restore/clean/format</c> once dtk is integrated with Claude Code.
/// </summary>
internal sealed partial class RtkHookCoexistence
{
    private const string ExcludedNote =
        "Detected an rtk hook: excluded `dotnet` in rtk's config so dtk owns dotnet commands. " +
        "`rtk dotnet …` still works for manual use.";

    private readonly string _userClaudeDir;
    private readonly string _rtkConfigPath;

    /// <summary>Creates an instance rooted at the real user Claude dir and rtk config path.</summary>
    public RtkHookCoexistence()
        : this(DefaultUserClaudeDir(), DefaultRtkConfigPath())
    {
    }

    /// <summary>Test seam: inject isolated paths so tests never touch the real machine.</summary>
    /// <param name="userClaudeDir">The user-level <c>~/.claude</c> directory to scan.</param>
    /// <param name="rtkConfigPath">The rtk <c>config.toml</c> path used by the reconcile step.</param>
    internal RtkHookCoexistence(string userClaudeDir, string rtkConfigPath)
    {
        _userClaudeDir = userClaudeDir;
        _rtkConfigPath = rtkConfigPath;
    }

    /// <summary>
    /// True when any of the user or project Claude settings files registers a PreToolUse hook whose
    /// command invokes <c>rtk … hook</c>.
    /// </summary>
    /// <param name="projectDirectory">The project root whose <c>.claude</c> settings are scanned.</param>
    internal bool IsRtkHookPresent(string projectDirectory)
    {
        string[] candidates =
        [
            Path.Combine(_userClaudeDir, "settings.json"),
            Path.Combine(_userClaudeDir, "settings.local.json"),
            Path.Combine(projectDirectory, ".claude", "settings.json"),
            Path.Combine(projectDirectory, ".claude", "settings.local.json"),
        ];

        return candidates.Any(FileRegistersRtkHook);
    }

    /// <summary>Detect the rtk hook and, if present, reconcile rtk's config.</summary>
    /// <param name="projectDirectory">The project root whose <c>.claude</c> settings are scanned.</param>
    /// <param name="cancellationToken">Token used to cancel the config read/write.</param>
    public async Task<RtkReconcileOutcome> ReconcileAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        return IsRtkHookPresent(projectDirectory)
            ? await ReconcileRtkConfigAsync(cancellationToken).ConfigureAwait(false)
            : RtkReconcileOutcome.None;
    }

    /// <summary>Ensure rtk's config excludes <c>dotnet</c>, preserving all other content.</summary>
    /// <param name="cancellationToken">Token used to cancel the config read/write.</param>
    internal async Task<RtkReconcileOutcome> ReconcileRtkConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_rtkConfigPath))
            {
                await WriteConfigAsync("[hooks]\nexclude_commands = [\"dotnet\"]\n", cancellationToken)
                    .ConfigureAwait(false);
                return new RtkReconcileOutcome(_rtkConfigPath, null, [ExcludedNote]);
            }

            var text = await File.ReadAllTextAsync(_rtkConfigPath, cancellationToken).ConfigureAwait(false);
            if (!TomlSerializer.TryDeserialize<TomlTable>(text, out var model))
            {
                return new RtkReconcileOutcome(null, null, [AdviceNote()]);
            }

            if (ConfigTextExcludesDotnet(text))
            {
                return RtkReconcileOutcome.None; // already excluded — stay silent
            }

            var hooks = model.TryGetValue("hooks", out var hooksNode) ? hooksNode as TomlTable : null;
            var excludes = hooks is not null && hooks.TryGetValue("exclude_commands", out var arrayNode)
                ? arrayNode as TomlArray
                : null;

            var updated = BuildUpdatedConfigText(text, hooks, excludes);

            // The regex-based edit targets common TOML shapes but can miss unusual ones (spaced/dotted
            // headers, inline tables, a same-named array in a different table, a non-array value). Re-parse
            // the candidate text and confirm it actually achieved the goal before writing — otherwise we'd
            // report false success, or worse, write a file that clobbers unrelated content.
            if (!ConfigTextExcludesDotnet(updated))
            {
                return new RtkReconcileOutcome(null, null, [AdviceNote()]);
            }

            await WriteConfigAsync(updated, cancellationToken).ConfigureAwait(false);
            return new RtkReconcileOutcome(null, _rtkConfigPath, [ExcludedNote]);
        }
        catch (IOException)
        {
            return new RtkReconcileOutcome(null, null, [AdviceNote()]);
        }
        catch (UnauthorizedAccessException)
        {
            return new RtkReconcileOutcome(null, null, [AdviceNote()]);
        }
    }

    private async Task WriteConfigAsync(string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rtkConfigPath)!);
        await File.WriteAllTextAsync(_rtkConfigPath, content, cancellationToken).ConfigureAwait(false);
    }

    private string AdviceNote() =>
        $"Could not update rtk's config at {_rtkConfigPath}. To let dtk own dotnet commands, add " +
        "under [hooks]: exclude_commands = [\"dotnet\"].";

    /// <summary>
    /// True when <paramref name="text"/> parses as TOML and <c>[hooks].exclude_commands</c> is an
    /// array containing <c>"dotnet"</c>. Used both to detect an already-reconciled config and to
    /// verify, after a candidate edit, that the edit actually achieved that outcome.
    /// </summary>
    /// <param name="text">The candidate rtk config TOML text to check.</param>
    private static bool ConfigTextExcludesDotnet(string text)
    {
        if (!TomlSerializer.TryDeserialize<TomlTable>(text, out var model))
        {
            return false;
        }

        var hooks = model.TryGetValue("hooks", out var hooksNode) ? hooksNode as TomlTable : null;
        return hooks is not null
            && hooks.TryGetValue("exclude_commands", out var arrayNode)
            && arrayNode is TomlArray array
            && array.OfType<string>().Contains("dotnet");
    }

    private static string BuildUpdatedConfigText(string text, TomlTable? hooks, TomlArray? excludes)
    {
        if (excludes is not null)
        {
            return ReplaceExcludeArray(text, [.. excludes.OfType<string>(), "dotnet"]);
        }

        return hooks is not null
            ? InsertKeyUnderHooksHeader(text)
            : text.TrimEnd('\n') + "\n\n[hooks]\nexclude_commands = [\"dotnet\"]\n";
    }

    private static string FormatStringArray(IEnumerable<string> items) =>
        "[" + string.Join(", ", items.Select(item => $"\"{item}\"")) + "]";

    private static string ReplaceExcludeArray(string text, IEnumerable<string> items)
    {
        var array = FormatStringArray(items);
        return ExcludeArrayRegex().Replace(
            text,
            match => match.Groups["prefix"].Value + array,
            1);
    }

    private static string InsertKeyUnderHooksHeader(string text) =>
        HooksHeaderRegex().Replace(text, match => match.Value + "\nexclude_commands = [\"dotnet\"]", 1);

    private static bool FileRegistersRtkHook(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            if (root?["hooks"] is not JsonObject hooksObject || hooksObject["PreToolUse"] is not JsonArray preToolUse)
            {
                return false;
            }

            foreach (var entry in preToolUse)
            {
                if (entry?["hooks"] is not JsonArray hooks)
                {
                    continue;
                }

                foreach (var hook in hooks)
                {
                    if (hook?["command"] is JsonValue value
                        && value.TryGetValue<string>(out var command)
                        && RtkHookCommandRegex().IsMatch(command))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Tolerate malformed settings — never fail integration over another tool's file.
        }
        catch (IOException)
        {
            // Tolerate a settings file we cannot read — never fail integration over another tool's file.
        }
        catch (UnauthorizedAccessException)
        {
            // Tolerate a permission-denied settings file — never fail integration over another tool's file.
        }

        return false;
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string DefaultUserClaudeDir() =>
        Path.Combine(Home(), ".claude");

    private static string DefaultRtkConfigPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrEmpty(xdg) ? Path.Combine(Home(), ".config") : xdg;
        return Path.Combine(configHome, "rtk", "config.toml");
    }

    [GeneratedRegex(@"(?:^|[\s/\\""'])rtk(?:\.exe)?\s+hook\b")]
    private static partial Regex RtkHookCommandRegex();

    [GeneratedRegex(@"(?m)^(?<prefix>\s*exclude_commands\s*=\s*)\[[^\]]*\]")]
    private static partial Regex ExcludeArrayRegex();

    [GeneratedRegex(@"(?m)^\[hooks\][^\S\n]*$")]
    private static partial Regex HooksHeaderRegex();
}

/// <summary>What <see cref="RtkHookCoexistence"/> did to rtk's config, for the integration result.</summary>
/// <param name="CreatedConfigPath">Path of a newly created config file, or <see langword="null"/> if none was created.</param>
/// <param name="UpdatedConfigPath">Path of an existing config file that was updated, or <see langword="null"/> if none was updated.</param>
/// <param name="Notes">Advisory notes describing what changed or what the caller should do manually.</param>
internal sealed record RtkReconcileOutcome(
    string? CreatedConfigPath,
    string? UpdatedConfigPath,
    IReadOnlyList<string> Notes)
{
    /// <summary>Nothing was detected or changed.</summary>
    internal static RtkReconcileOutcome None { get; } = new(null, null, []);
}
