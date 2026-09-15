using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>A hook registration merged into a harness's settings file.</summary>
/// <param name="SettingsPath">Path to the settings.json file.</param>
/// <param name="EventKey">Key of the hook event array within the hooks object (e.g. "PreToolUse").</param>
/// <param name="Matcher">The matcher value for the registered hook entry.</param>
/// <param name="Command">The command dtk registers, and the value used to detect an existing registration.</param>
/// <param name="TimeoutSeconds">A <c>timeout</c> written on the handler, or <see langword="null"/> to write none.</param>
internal sealed record HookRegistrationSpec(
    string SettingsPath, string EventKey, string Matcher, string Command, int? TimeoutSeconds = null);

internal static class IntegratorHelpers
{
    private const string HooksKey = "hooks";
    private const string CommandKey = "command";

    /// <summary>
    /// Serializer options for settings files merged by <see cref="MergeJsonSettingsAsync"/>.
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> still escapes what JSON requires
    /// (<c>"</c> as <c>\"</c>, <c>\</c>, control characters), so the output stays valid JSON; it just
    /// stops also escaping <c>&lt; &gt; &amp; ' +</c> and non-ASCII characters to <c>\uXXXX</c>, which
    /// the default encoder does because it assumes the JSON might be embedded in HTML. These settings
    /// files are read directly by the Claude and Gemini CLIs, never rendered as HTML, so the relaxed
    /// escaping is safe and keeps a command's own quotes (and any existing entry's characters) legible
    /// instead of rewriting them on every merge. Cached in a <see langword="static readonly"/> field
    /// because constructing <see cref="JsonSerializerOptions"/> per call is itself flagged (CA1869).
    /// </summary>
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Writes a whole-file artifact, refreshing it whenever there is something to change and
    /// leaving it alone otherwise.
    /// </summary>
    /// <remarks>
    /// When the target already exists and its content (line endings normalized to <c>\n</c> on
    /// both sides) is byte-identical to <paramref name="content"/>, nothing is written and the
    /// path is reported <see cref="IntegrationContext.Unchanged"/> — this branch is
    /// force-independent (there is nothing to write and <c>--force</c> would not change that), so
    /// it must never be reported as <see cref="IntegrationContext.Skipped"/>, which implies
    /// re-running with <c>--force</c> would help. When the existing content differs, the file is
    /// left untouched and reported <see cref="IntegrationContext.Skipped"/> unless
    /// <see cref="IntegrationContext.Force"/> is set: unlike <see cref="WriteGeneratedFileAsync"/>,
    /// this method has no stamp to prove dtk wrote the differing copy, so it cannot tell a user
    /// edit from a stale dtk write and must not overwrite without <c>--force</c>. When the existing
    /// file cannot be read (locked, permission denied), dtk cannot prove it wrote that file, so it
    /// is never reported <see cref="IntegrationContext.Unchanged"/> — it falls back to the same
    /// skip-or-force decision as a content difference.
    /// </remarks>
    /// <param name="path">Path to the target file.</param>
    /// <param name="content">The full content to write.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static Task WriteFileAsync(string path, string content, IntegrationContext context, CancellationToken cancellationToken)
        => WriteOwnedFileAsync(path, content, replaceExisting: false, context, cancellationToken);

    /// <summary>
    /// <see cref="WriteFileAsync"/> for a file dtk owns outright: when <paramref name="replaceExisting"/> is
    /// <see langword="true"/>, a differing existing copy is overwritten without <c>--force</c>.
    /// </summary>
    /// <param name="path">Path to the target file.</param>
    /// <param name="content">The full content to write.</param>
    /// <param name="replaceExisting">Whether the caller has verified the existing copy is dtk's own.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteOwnedFileAsync(
        string path, string content, bool replaceExisting, IntegrationContext context, CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists)
        {
            var existing = await TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false);

            if (existing is not null
                && string.Equals(existing, content.ReplaceLineEndings("\n"), StringComparison.Ordinal))
            {
                context.Unchanged.Add(path);
                return;
            }
        }

        if (ShouldSkipWrite(exists, context.Force || replaceExisting))
        {
            context.Skipped.Add(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        (exists ? context.Updated : context.Created).Add(path);
    }

    /// <summary>
    /// Reads an existing file's content, normalized to <c>\n</c> line endings, or
    /// <see langword="null"/> when the file exists but cannot be read (locked, permission denied).
    /// A read failure must never be mistaken for "file doesn't exist" or "content differs" by the
    /// caller — both <see cref="WriteFileAsync"/> and <see cref="WriteGeneratedFileAsync"/> treat a
    /// <see langword="null"/> result as "cannot prove authorship" and fall back to the skip-or-force
    /// decision, matching the guarded-read pattern in <c>HookHealthChecker</c>.
    /// </summary>
    /// <param name="path">Path to the existing file to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<string?> TryReadExistingAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))
                .ReplaceLineEndings("\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decides whether a write to an existing file should be skipped: <see langword="true"/>
    /// whenever <paramref name="fileExists"/> and <paramref name="force"/> is <see langword="false"/>.
    /// This is the single source of truth for "existing file, no --force ⇒ leave it alone",
    /// consumed by <see cref="WriteFileAsync"/>, <see cref="WriteSectionBasedFileAsync"/>, and
    /// <see cref="AiderIntegrator"/>'s merge into an existing external <c>read:</c> key — so those
    /// decisions can never drift apart.
    /// </summary>
    /// <param name="fileExists">Whether the target file already exists.</param>
    /// <param name="force">Whether the integration is running with the force flag.</param>
    internal static bool ShouldSkipWrite(bool fileExists, bool force) => fileExists && !force;

    /// <summary>
    /// Substring present in every generation of the Python hook dtk installed before <c>dtk hook</c>,
    /// used to prove an unstamped copy is dtk's before deleting it.
    /// </summary>
    internal const string HookLegacySignature = "_DTK_SUBCOMMANDS";

    /// <summary>File name of the Python hook script dtk installed before <c>dtk hook</c> replaced it.</summary>
    internal const string LegacyHookScriptName = "dotnet-to-dtk.py";

    /// <summary>
    /// Writes an artifact dtk generates in full, refreshing it when dtk can prove it wrote the
    /// copy already on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule, in order: a missing file is created; a file already equal to the stamped current
    /// template is reported unchanged; a file whose stamp still verifies came from an older dtk
    /// and is refreshed; an unstamped file carrying
    /// <see cref="GeneratedArtifact.LegacySignature"/> predates stamping and is refreshed with a
    /// note; anything else is left alone unless <c>--force</c> is passed.
    /// </para>
    /// <para>
    /// The legacy branch exists because no artifact installed before stamping carries a stamp, so
    /// without it every existing user would fall through to the skip branch and the refresh would
    /// only begin working one release after the one that adds it. It costs a one-time overwrite
    /// for anyone who hand-edited an unstamped artifact, which is why the overwrite is reported
    /// rather than silent, and it becomes unreachable once one stamped generation is installed.
    /// </para>
    /// <para>
    /// When the existing artifact cannot be read (locked, permission denied), dtk cannot prove it
    /// wrote the copy on disk, so that copy is never treated as up to date or legacy: without
    /// <c>--force</c> it is reported <see cref="IntegrationContext.Skipped"/>, matching an
    /// unrecognized foreign file; with <c>--force</c> it is overwritten like any other differing
    /// copy.
    /// </para>
    /// </remarks>
    /// <param name="artifact">The artifact to write.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteGeneratedFileAsync(
        GeneratedArtifact artifact,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var content = ArtifactStamping.Apply(artifact.Body, artifact.Style);

        if (!File.Exists(artifact.Path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(artifact.Path)!);
            await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
            context.Created.Add(artifact.Path);
            return;
        }

        var existing = await TryReadExistingAsync(artifact.Path, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            if (!context.Force)
            {
                context.Skipped.Add(artifact.Path);
                return;
            }

            await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
            context.Updated.Add(artifact.Path);
            return;
        }

        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            context.Unchanged.Add(artifact.Path);
            return;
        }

        var isLegacy = !ArtifactStamping.HasStamp(existing)
                       && existing.Contains(artifact.LegacySignature, StringComparison.Ordinal);

        if (!ArtifactStamping.IsAuthentic(existing) && !isLegacy && !context.Force)
        {
            context.Skipped.Add(artifact.Path);
            return;
        }

        await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(artifact.Path);

        if (isLegacy)
        {
            context.Notes.Add(
                $"{artifact.Path} was written by an older dtk and carried no provenance stamp; it was "
                + "regenerated. Any local edits to it are recoverable from version control.");
        }
    }

    /// <summary>
    /// Writes a file that uses begin/end section markers to track a dtk-managed block.
    /// If the file already exists — whether or not it contains the section marker — and
    /// <c>context.Force</c> is <see langword="false"/>: skips, leaving the file completely
    /// untouched (matching <see cref="WriteFileAsync"/>'s contract).
    /// If the file already exists and <c>context.Force</c> is <see langword="true"/>: replaces the
    /// dtk-managed span when the marker is present, or appends the section when it is not.
    /// If the file already contains exactly <paramref name="section"/> (line endings normalized): reports it
    /// unchanged and writes nothing, force or not.
    /// If the file does not exist: creates it with the section as the only content.
    /// </summary>
    /// <param name="path">Path to the target file.</param>
    /// <param name="sectionMarker">String that marks the beginning of the dtk-managed block.</param>
    /// <param name="sectionEndMarker">String that marks the end of the dtk-managed block.</param>
    /// <param name="section">Full text of the dtk-managed block to write.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteSectionBasedFileAsync(
        string path,
        string sectionMarker,
        string sectionEndMarker,
        string section,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists
            && await TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false) is { } existing
            && existing.Contains(section.ReplaceLineEndings("\n"), StringComparison.Ordinal))
        {
            // Nothing to write, with or without --force; reporting it skipped would advise a --force that changes nothing.
            context.Unchanged.Add(path);
            return;
        }

        if (ShouldSkipWrite(exists, context.Force))
        {
            context.Skipped.Add(path);
            return;
        }

        if (!exists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, section, cancellationToken).ConfigureAwait(false);
            context.Created.Add(path);
            return;
        }

        var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var updated = current.Contains(sectionMarker, StringComparison.Ordinal)
            ? ReplaceDtkSection(current, sectionMarker, sectionEndMarker, section)
            : AppendSection(current, section);

        await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    private static string AppendSection(string current, string section)
    {
        var trimmed = current.TrimEnd();
        return string.IsNullOrWhiteSpace(trimmed)
            ? section
            // Explicit '\n', not Environment.NewLine, so the written section is byte-identical across
            // platforms and never introduces a CR into an otherwise LF file.
            : trimmed + "\n" + section;
    }

    private static string ReplaceDtkSection(string content, string marker, string endMarker, string section)
    {
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            // The begin marker is present but the end marker is missing (e.g. a user accidentally
            // deleted just the "<!-- /dtk -->" line). The dtk-managed span can no longer be
            // reliably identified, so don't guess how far it extends: insert the fresh section in
            // place of the begin marker and preserve everything that followed it (stale dtk body
            // text and/or real user content) immediately after, rather than deleting it.
            return content[..start] + section + content[(start + marker.Length)..];
        }

        return content[..start] + section + content[(end + endMarker.Length)..];
    }

    /// <summary>Merges a hook registration into the provider's settings JSON. No script is written.</summary>
    /// <param name="spec">Where and what to register.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the merge replaced or removed an entry running <see cref="LegacyHookScriptName"/>.</returns>
    internal static Task<bool> WriteHookRegistrationAsync(
        HookRegistrationSpec spec, IntegrationContext context, CancellationToken cancellationToken)
    {
        var handler = new JsonObject { ["type"] = "command", [CommandKey] = spec.Command };
        if (spec.TimeoutSeconds is { } timeout)
        {
            handler["timeout"] = timeout;
        }

        return MergeJsonSettingsAsync(
            spec.SettingsPath,
            spec.EventKey,
            new JsonObject { ["matcher"] = spec.Matcher, [HooksKey] = new JsonArray(handler) },
            spec.Command,
            context,
            cancellationToken);
    }

    /// <summary>
    /// Deletes the Python hook script dtk installed before <c>dtk hook</c>, when dtk can prove it wrote it.
    /// </summary>
    /// <remarks>
    /// Proof is the same as for refreshing a generated artifact: the provenance stamp verifies, or the file is
    /// unstamped and carries <see cref="HookLegacySignature"/>. Anything else may hold the user's own changes,
    /// so it is kept with a note unless <c>--force</c> is set. The directory is removed when this empties it.
    /// A script the file system refuses to delete is kept with a note rather than aborting the run, whose
    /// registration is already migrated. Integrators call it through <see cref="RetireLegacyHookScriptAsync"/>,
    /// which first establishes that nothing dtk can see still runs the script.
    /// </remarks>
    /// <param name="scriptPath">Where the Python script would be.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RemoveLegacyHookScriptAsync(
        string scriptPath, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            return;
        }

        var existing = await TryReadExistingAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        var writtenByDtk = existing is not null
                           && (ArtifactStamping.IsAuthentic(existing)
                               || (!ArtifactStamping.HasStamp(existing)
                                   && existing.Contains(HookLegacySignature, StringComparison.Ordinal)));

        if (!writtenByDtk && !context.Force)
        {
            // No --force hint: once this run has migrated the registration, a re-run has nothing left to migrate
            // and never reaches this point, so only --force on the migrating run itself would have deleted it.
            context.Notes.Add(
                $"{scriptPath} is no longer used: the hook now runs dtk directly. It differs from what dtk "
                + "wrote, so it was left in place; delete it once you have kept any changes you need.");
            return;
        }

        try
        {
            File.Delete(scriptPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Notes.Add(
                $"{scriptPath} is no longer used: the hook now runs dtk directly. It could not be deleted "
                + $"({ex.Message}), so it was left in place; delete it yourself.");
            return;
        }

        context.Removed.Add(scriptPath);

        var directory = Path.GetDirectoryName(scriptPath);
        try
        {
            if (directory is not null && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The script is gone, which is what mattered. An empty directory that cannot be removed, or one
            // that gained a file since it was listed, is left as it is.
        }
    }

    /// <summary>
    /// Deletes the Python hook script dtk installed before <c>dtk hook</c> only when nothing dtk can see can still
    /// run it; otherwise keeps it with a note saying why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A registration whose script is gone is worse than a stale one: <c>python3</c> on a missing file exits 2,
    /// which Claude Code treats as blocking and Gemini CLI and Copilot CLI as a denied tool call. So the script
    /// goes only when both hold:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     this run replaced a registration that ran it — evidence it was dtk's hook and that the harness has
    ///     moved on — which a script with no registration at all never gives, even under <c>--force</c>;
    ///   </description></item>
    ///   <item><description>
    ///     none of <paramref name="registrationFiles"/>, read after the registration was written, still mentions
    ///     <see cref="LegacyHookScriptName"/>. A file that cannot be read may still run it.
    ///   </description></item>
    /// </list>
    /// <para>Then <see cref="RemoveLegacyHookScriptAsync"/> decides whether dtk can prove it wrote the script.</para>
    /// </remarks>
    /// <param name="scriptPath">Where the Python script would be.</param>
    /// <param name="replacedLegacyRegistration">Whether this run's registration write replaced an entry running the script.</param>
    /// <param name="registrationFiles">
    /// Every file beside the registration that the harness reads hooks from, the registration itself included.
    /// </param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RetireLegacyHookScriptAsync(
        string scriptPath,
        bool replacedLegacyRegistration,
        IReadOnlyList<string> registrationFiles,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registrationFiles);

        if (!File.Exists(scriptPath))
        {
            return;
        }

        foreach (var file in registrationFiles.Where(File.Exists))
        {
            var content = await TryReadExistingAsync(file, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                context.Notes.Add(
                    $"{scriptPath} was left in place: {file} could not be read, so it may still run it. "
                    + "Delete the script once no hook runs it.");
                return;
            }

            if (content.Contains(LegacyHookScriptName, StringComparison.Ordinal))
            {
                context.Notes.Add(
                    $"{scriptPath} was left in place: {file} still runs it. Delete the script once no hook there does.");
                return;
            }
        }

        if (!replacedLegacyRegistration)
        {
            context.Notes.Add(
                $"{scriptPath} was left in place: no registration dtk updated ran it, so dtk cannot tell whether "
                + "something else still does. Delete it if nothing runs it.");
            return;
        }

        await RemoveLegacyHookScriptAsync(scriptPath, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Merges a hook entry into a JSON settings file under
    /// <c>hooks[<paramref name="hookEventKey"/>]</c>.
    /// Existing content is preserved; registration is detected by matching
    /// <paramref name="hookCommand"/> against each entry's <c>"command"</c> field. An entry is dtk's
    /// own when its command equals <paramref name="hookCommand"/> after removing every <c>"</c>
    /// character (see <see cref="AreEquivalentIgnoringQuotes"/>) — so a settings file hand-edited to
    /// quote the whole path instead of just the env-var segment (e.g.
    /// <c>python3 "$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py"</c>) is still recognized as
    /// dtk's own hook — or when its command runs <see cref="LegacyHookScriptName"/> (any interpreter,
    /// any path — including a Windows user's hand edit to run <c>python</c> instead of
    /// <c>python3</c>), the Python hook script that <c>dtk hook</c> replaces.
    /// <list type="bullet">
    ///   <item><description>
    ///     Only the identical current command is registered: nothing is written, and the path is
    ///     reported <see cref="IntegrationContext.Unchanged"/> — this branch is force-independent
    ///     (there is nothing to write and <c>--force</c> would not change that), so it must never be
    ///     reported as <see cref="IntegrationContext.Skipped"/>, which implies re-running with
    ///     <c>--force</c> would help.
    ///   </description></item>
    ///   <item><description>
    ///     An equivalent-but-not-identical variant (quote difference or legacy Python hook) is
    ///     registered: it is upgraded in place to the current command and the file is reported
    ///     <see cref="IntegrationContext.Updated"/>.
    ///   </description></item>
    ///   <item><description>
    ///     The identical current command and one or more equivalent/legacy variants are all
    ///     registered (possible when an in-between build appended the new one alongside the old): the
    ///     variants are removed, leaving exactly one registration, and the file is reported
    ///     <see cref="IntegrationContext.Updated"/>.
    ///   </description></item>
    ///   <item><description>Nothing equivalent is registered: the entry is appended.</description></item>
    /// </list>
    /// Every other hook in the array — one that is not equivalent to <paramref name="hookCommand"/> —
    /// is left untouched.
    /// </summary>
    /// <param name="path">Path to the settings.json file.</param>
    /// <param name="hookEventKey">Key of the hook event array within the hooks object (e.g. "PreToolUse").</param>
    /// <param name="hookEntry">The JSON object to append to the hook event array.</param>
    /// <param name="hookCommand">The command string used to detect whether the hook is already registered.</param>
    /// <param name="context">Integration context carrying the result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Whether an entry running <see cref="LegacyHookScriptName"/> was upgraded in place or removed — the only
    /// evidence a caller has that the Python script this registration ran is no longer needed by it.
    /// </returns>
    /// <exception cref="InvalidOperationException">The settings file contains invalid JSON or an unexpected root type.</exception>
    internal static async Task<bool> MergeJsonSettingsAsync(
        string path,
        string hookEventKey,
        JsonObject hookEntry,
        string hookCommand,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);
        var root = await ReadRootObjectAsync(path, exists, cancellationToken).ConfigureAwait(false);

        root.TryGetPropertyValue(HooksKey, out var hooksNode);
        var hooks = hooksNode switch
        {
            null => [],
            JsonObject hooksObj => hooksObj,
            _ => throw new InvalidOperationException(
                $"The settings file '{path}' has a '{HooksKey}' property of unexpected type '{hooksNode.GetType().Name}'; expected a JSON object.")
        };

        hooks.TryGetPropertyValue(hookEventKey, out var eventNode);
        var hookArray = eventNode switch
        {
            null => [],
            JsonArray arr => arr,
            _ => throw new InvalidOperationException(
                $"The settings file '{path}' has a '{HooksKey}.{hookEventKey}' property of unexpected type '{eventNode.GetType().Name}'; expected a JSON array.")
        };

        var matches = FindEquivalentEntries(hookArray, hookCommand);
        var replacedLegacy = matches.Exists(
            match => match[CommandKey]!.GetValue<string>().Contains(LegacyHookScriptName, StringComparison.Ordinal));

        if (matches.Count == 0)
        {
            // The JsonNode overload: Add<JsonObject> is neither trim- nor AOT-safe.
            hookArray.Add((JsonNode)hookEntry);
        }
        else if (matches.Count == 1 && matches[0][CommandKey]!.GetValue<string>() == hookCommand)
        {
            // The only registration is already the identical current command: nothing to write.
            context.Unchanged.Add(path);
            return false;
        }
        else
        {
            // Prefer an already-identical registration when one exists — keeping it (rather than
            // whichever match happens to be first) preserves any extra properties it carries (e.g.
            // "timeout") and minimizes churn. Otherwise upgrade the first match in place. Every
            // other equivalent/legacy match is then dropped so exactly one registration survives.
            var survivor = matches.Find(match => match[CommandKey]!.GetValue<string>() == hookCommand) ?? matches[0];
            survivor[CommandKey] = hookCommand;

            var toRemove = matches.FindAll(match => !ReferenceEquals(match, survivor));
            if (toRemove.Count > 0)
            {
                RemoveEntries(hookArray, toRemove);
            }
        }

        hooks[hookEventKey] = hookArray;
        root[HooksKey] = hooks;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            // WriteIndented emits '\r\n' on Windows; normalize so the written JSON is identical
            // everywhere, and add the single trailing '\n' ToJsonString never emits, matching
            // .editorconfig's insert_final_newline.
            root.ToJsonString(SettingsJsonOptions).ReplaceLineEndings("\n") + "\n",
            cancellationToken).ConfigureAwait(false);

        (exists ? context.Updated : context.Created).Add(path);
        return replacedLegacy;
    }

    /// <summary>
    /// Reads and parses the settings file into its root <see cref="JsonObject"/>, or returns an empty
    /// object when the file does not exist. Throws <see cref="InvalidOperationException"/> when the file
    /// contains invalid JSON or a non-object root.
    /// </summary>
    /// <param name="path">Path to the settings.json file.</param>
    /// <param name="exists">Whether the file already exists on disk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the file contains invalid JSON or the root is not a JSON object.</exception>
    private static async Task<JsonObject> ReadRootObjectAsync(
        string path,
        bool exists,
        CancellationToken cancellationToken)
    {
        if (!exists)
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse JSON settings file '{path}'. The file must contain a valid JSON object at the root.",
                ex);
        }

        if (parsed is JsonObject obj)
        {
            return obj;
        }

        var actualType = parsed?.GetType().Name ?? "null";
        throw new InvalidOperationException(
            $"The settings file '{path}' must contain a JSON object at the root, but found '{actualType}'.");
    }

    /// <summary>
    /// Finds every registered inner hook object whose <c>"command"</c> is equivalent to
    /// <paramref name="hookCommand"/> — identical, equal after removing every <c>"</c> character (see
    /// <see cref="AreEquivalentIgnoringQuotes"/>), or running <see cref="LegacyHookScriptName"/> (the
    /// Python hook script <c>dtk hook</c> replaces, under any interpreter or path) — in the array's
    /// traversal order.
    /// </summary>
    /// <param name="hookArray">The hook event array (e.g. <c>hooks.PreToolUse</c>) to search.</param>
    /// <param name="hookCommand">The current command dtk registers.</param>
    private static List<JsonObject> FindEquivalentEntries(JsonArray hookArray, string hookCommand)
    {
        var matches = new List<JsonObject>();

        foreach (var item in hookArray)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            entry.TryGetPropertyValue(HooksKey, out var innerHooksNode);
            if (innerHooksNode is not JsonArray innerHooks)
            {
                continue;
            }

            foreach (var inner in innerHooks)
            {
                if (inner is not JsonObject innerEntry)
                {
                    continue;
                }

                var command = innerEntry[CommandKey]?.GetValue<string>();
                if (command is not null
                    && (AreEquivalentIgnoringQuotes(command, hookCommand)
                        || command.Contains(LegacyHookScriptName, StringComparison.Ordinal)))
                {
                    matches.Add(innerEntry);
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// True when two hook commands register the same hook once quoting differences are ignored —
    /// e.g. <c>python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py</c> (the env var quoted)
    /// and <c>python3 "$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py"</c> (the whole path quoted,
    /// as a hand-edit might write it) are the same hook.
    /// </summary>
    /// <param name="first">The first command to compare.</param>
    /// <param name="second">The second command to compare.</param>
    private static bool AreEquivalentIgnoringQuotes(string first, string second) =>
        string.Equals(StripQuotes(first), StripQuotes(second), StringComparison.Ordinal);

    /// <summary>Removes every <c>"</c> character from <paramref name="command"/>.</summary>
    /// <param name="command">The command to strip.</param>
    private static string StripQuotes(string command) => command.Replace("\"", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Removes the given inner hook objects from <paramref name="hookArray"/> by reference, dropping
    /// any outer entry whose inner <c>hooks</c> list becomes empty as a result.
    /// </summary>
    /// <param name="hookArray">The hook event array (e.g. <c>hooks.PreToolUse</c>) to prune.</param>
    /// <param name="entriesToRemove">The specific inner hook objects to remove.</param>
    private static void RemoveEntries(JsonArray hookArray, IReadOnlyCollection<JsonObject> entriesToRemove)
    {
        var toRemove = new HashSet<JsonObject>(entriesToRemove);

        for (var outer = hookArray.Count - 1; outer >= 0; outer--)
        {
            if (hookArray[outer] is not JsonObject entry)
            {
                continue;
            }

            entry.TryGetPropertyValue(HooksKey, out var innerHooksNode);
            if (innerHooksNode is not JsonArray innerHooks)
            {
                continue;
            }

            for (var inner = innerHooks.Count - 1; inner >= 0; inner--)
            {
                if (innerHooks[inner] is JsonObject innerEntry && toRemove.Contains(innerEntry))
                {
                    innerHooks.RemoveAt(inner);
                }
            }

            if (innerHooks.Count == 0)
            {
                hookArray.RemoveAt(outer);
            }
        }
    }
}
