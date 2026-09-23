using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Detects an rtk PreToolUse hook and reconciles rtk's config so that dtk — not rtk — owns
/// <c>dotnet</c> commands once dtk is integrated with Claude Code. Deliberately not a list of
/// subcommands: the reconciliation excludes <c>dotnet</c> wholesale in rtk's config, so it covers
/// whatever <c>DotnetTokenKiller.Domain.DotnetSubcommands</c> holds without needing to restate it.
/// </summary>
internal sealed partial class RtkHookCoexistence
{
    private const string ExcludedNote =
        "Detected an rtk hook: excluded `dotnet` in rtk's config so dtk owns dotnet commands. " +
        "`rtk dotnet …` still works for manual use.";

    /// <summary>The <c>hooks</c> key/table name used in both rtk's TOML config and Claude's JSON settings.</summary>
    private const string HooksKey = "hooks";

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

    /// <summary>
    /// Detect an rtk rewrite in any of a harness's hook or plugin files and, if one is present, reconcile rtk's config.
    /// </summary>
    /// <param name="harnessFiles">The files where rtk registers itself for the harness being integrated.</param>
    /// <param name="cancellationToken">Token used to cancel the config read/write.</param>
    public async Task<RtkReconcileOutcome> ReconcileFilesAsync(
        IReadOnlyList<string> harnessFiles, CancellationToken cancellationToken)
    {
        return IsRtkRewriteReferencedIn(harnessFiles)
            ? await ReconcileRtkConfigAsync(cancellationToken).ConfigureAwait(false)
            : RtkReconcileOutcome.None;
    }

    /// <summary>
    /// True when any file runs an rtk rewrite: <c>rtk hook …</c> in a hook registration, or <c>rtk rewrite …</c> in a
    /// plugin that shells out to it. A text search, because the files are JSON, TOML or JavaScript depending on the
    /// harness; rtk routes every one of those entry points through the decision that honors <c>exclude_commands</c>.
    /// </summary>
    /// <param name="harnessFiles">The files to search; missing or unreadable ones count as no rtk.</param>
    internal static bool IsRtkRewriteReferencedIn(IEnumerable<string> harnessFiles) =>
        harnessFiles.Any(FileMentionsRtkRewrite);

    private static bool FileMentionsRtkRewrite(string path)
    {
        try
        {
            return File.Exists(path) && RtkRewriteInvocationRegex().IsMatch(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another tool's file we cannot read: we cannot tell, so assume no rtk.
            return false;
        }
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
            if (!TomlSerializer.TryDeserialize(text, TomlTableContext.Default, out TomlTable? model))
            {
                return new RtkReconcileOutcome(null, null, [AdviceNote()]);
            }

            if (ConfigTextExcludesDotnet(text))
            {
                return RtkReconcileOutcome.None; // already excluded — stay silent
            }

            var hooks = model.TryGetValue(HooksKey, out var hooksNode) ? hooksNode as TomlTable : null;
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
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Never fail integration over rtk's config. An unreadable/unwritable file (IOException,
            // UnauthorizedAccessException) or an invalid config path — e.g. a malformed XDG_CONFIG_HOME
            // producing bad path characters (ArgumentException, NotSupportedException) — degrades to
            // an advisory note instead of throwing.
            return new RtkReconcileOutcome(null, null, [AdviceNote()]);
        }
    }

    /// <summary>
    /// After an uninstall removed something, notes that rtk's config still excludes <c>dotnet</c>, which the install
    /// may have added. The exclusion is left in place: rtk's config is another tool's file, and dtk cannot tell whether
    /// it added the entry or the user did.
    /// </summary>
    /// <param name="context">The uninstall context.</param>
    internal void NoteRemainingExclusion(IntegrationContext context)
    {
        if (context.Removed.Count == 0 && context.Updated.Count == 0)
        {
            return;
        }

        try
        {
            if (File.Exists(_rtkConfigPath) && ConfigTextExcludesDotnet(File.ReadAllText(_rtkConfigPath)))
            {
                context.Notes.Add(
                    $"rtk's config at {_rtkConfigPath} still excludes dotnet (an earlier 'dtk init' may have added it) "
                    + "and was left as it is. Remove \"dotnet\" from [hooks].exclude_commands if rtk should handle "
                    + "dotnet commands again.");
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Another tool's file dtk cannot read: nothing to say about it.
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
        if (!TomlSerializer.TryDeserialize(text, TomlTableContext.Default, out TomlTable? model))
        {
            return false;
        }

        var hooks = model.TryGetValue(HooksKey, out var hooksNode) ? hooksNode as TomlTable : null;
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
            if (root?[HooksKey] is not JsonObject hooksObject || hooksObject["PreToolUse"] is not JsonArray preToolUse)
            {
                return false;
            }

            foreach (var entry in preToolUse)
            {
                if (entry?[HooksKey] is not JsonArray hooks)
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
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Tolerate settings that are malformed (JsonException), unreadable (IOException) or
            // permission-denied (UnauthorizedAccessException) — never fail integration over another
            // tool's file. All three mean the same thing here: we cannot tell, so assume no hook.
        }

        return false;
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string DefaultUserClaudeDir() =>
        Path.Combine(Home(), ".claude");

    private static string DefaultRtkConfigPath()
    {
        // Per the XDG Base Directory spec, XDG_CONFIG_HOME must be an absolute path; a relative (or
        // empty) value is invalid and is ignored in favor of the ~/.config default. Honoring a
        // relative value would resolve the write against the current working directory — surprising
        // and unsafe for a global config.
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrEmpty(xdg) || !Path.IsPathRooted(xdg)
            ? Path.Combine(Home(), ".config")
            : xdg;
        return Path.Combine(configHome, "rtk", "config.toml");
    }

    [GeneratedRegex(@"(?:^|[\s/\\""'])rtk(?:\.exe)?\s+hook\b")]
    private static partial Regex RtkHookCommandRegex();

    [GeneratedRegex(@"(?:^|[\s/\\""'`])rtk(?:\.exe)?\s+(?:hook|rewrite)\b")]
    private static partial Regex RtkRewriteInvocationRegex();

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

    /// <summary>Records what the reconciliation changed in an integration run's result.</summary>
    /// <param name="context">The run's context.</param>
    internal void ApplyTo(IntegrationContext context)
    {
        if (CreatedConfigPath is not null)
        {
            context.Created.Add(CreatedConfigPath);
        }

        if (UpdatedConfigPath is not null)
        {
            context.Updated.Add(UpdatedConfigPath);
        }

        context.Notes.AddRange(Notes);
    }
}
