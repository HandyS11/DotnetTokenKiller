using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration.Hooks;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// The removal counterparts of <see cref="IntegratorHelpers"/>' writes, shared by every integrator's
/// <see cref="IUninstallIntegrator.UninstallAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each helper removes only what its install counterpart writes and proves before deleting: a hook entry is dtk's by
/// the same match the install uses to find an equivalent entry; a generated file by its provenance stamp; a file dtk
/// writes whole without a stamp by being byte-identical to what this dtk writes; a section by its markers. Anything
/// dtk cannot prove is its own is kept, reported in <see cref="IntegrationContext.Skipped"/> with a note.
/// </para>
/// <para>
/// A file is deleted only when nothing but dtk's part was in it — no settings besides dtk's hook, no text besides dtk's
/// section — so no content the user wrote is lost, whether dtk or the user created the file. Directories left empty by
/// a deletion are removed up to <see cref="IntegrationContext.PruneBoundary"/>.
/// </para>
/// </remarks>
internal static class UninstallHelpers
{
    /// <summary>Removes dtk's entries from a hook registration merged into a settings file.</summary>
    /// <remarks>
    /// dtk's entries are those <see cref="IntegratorHelpers.MergeJsonSettingsAsync(string, string, string, JsonObject, string, IntegrationContext, CancellationToken)"/>
    /// treats as its own: the current command, a quoting variant of it, or the Python hook script it replaced. A group
    /// left with no handler, and the event array and container left empty, are dropped; a settings file left as
    /// <c>{}</c> is deleted.
    /// </remarks>
    /// <param name="spec">The registration the install writes.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The settings file contains invalid JSON or an unexpected root type.</exception>
    internal static async Task RemoveHookRegistrationAsync(
        HookRegistrationSpec spec, IntegrationContext context, CancellationToken cancellationToken)
    {
        var path = spec.SettingsPath;
        if (!File.Exists(path))
        {
            return;
        }

        var root = await IntegratorHelpers.ReadRootObjectAsync(path, exists: true, cancellationToken).ConfigureAwait(false);

        if (root[spec.ContainerKey] is not JsonObject container
            || container[spec.EventKey] is not JsonArray hookArray
            || IntegratorHelpers.FindEquivalentEntries(hookArray, spec.Command) is not { Count: > 0 } matches)
        {
            context.Unchanged.Add(path);
            return;
        }

        IntegratorHelpers.RemoveEntries(hookArray, matches);

        if (hookArray.Count == 0)
        {
            container.Remove(spec.EventKey);
        }

        if (container.Count == 0)
        {
            root.Remove(spec.ContainerKey);
        }

        if (root.Count == 0)
        {
            DeleteFile(path, context);
            return;
        }

        await IntegratorHelpers.WriteSettingsJsonAsync(path, root, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>
    /// Removes the dtk-managed section, from its begin marker through the end marker after it, from a file the install
    /// merges it into; deletes the file when nothing else is left in it.
    /// </summary>
    /// <param name="path">The file holding the section.</param>
    /// <param name="sectionMarker">String that marks the beginning of the dtk-managed block.</param>
    /// <param name="sectionEndMarker">String that marks the end of the dtk-managed block.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="removeMergedContent">
    /// Removes anything else the install merged into the file outside the section, applied before the section is
    /// removed; <see langword="null"/> when the install merges nothing else.
    /// </param>
    internal static async Task RemoveSectionAsync(
        string path,
        string sectionMarker,
        string sectionEndMarker,
        IntegrationContext context,
        CancellationToken cancellationToken,
        Func<string, string>? removeMergedContent = null)
    {
        if (!File.Exists(path) || KeptForAnotherIntegration(path, context))
        {
            return;
        }

        // Read as written, not with line endings normalized: whatever is left is written back.
        var content = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            Keep(path, "it could not be read", context);
            return;
        }

        var start = content.IndexOf(sectionMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            context.Unchanged.Add(path);
            return;
        }

        var end = content.IndexOf(sectionEndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            Keep(path, $"its dtk section has no closing '{sectionEndMarker}' line, so dtk cannot tell where it ends", context);
            return;
        }

        if (removeMergedContent is not null)
        {
            content = removeMergedContent(content);
            start = content.IndexOf(sectionMarker, StringComparison.Ordinal);
            end = content.IndexOf(sectionEndMarker, start, StringComparison.Ordinal);
        }

        // The line break ending the section goes with it; the one the install put before an appended section stays,
        // ending the line above as it did before the install.
        var after = end + sectionEndMarker.Length;
        after += content.AsSpan(after) switch
        {
            ['\r', '\n', ..] => 2,
            ['\n', ..] => 1,
            _ => 0
        };

        var remaining = content[..start] + content[after..];

        if (string.IsNullOrWhiteSpace(remaining))
        {
            DeleteFile(path, context);
            return;
        }

        await File.WriteAllTextAsync(path, remaining, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>Deletes a file dtk generated in full, when its provenance stamp proves the content is dtk's.</summary>
    /// <remarks>
    /// A stamp from any dtk version counts: it proves the body is exactly what some dtk wrote. An unstamped copy is
    /// kept even when it carries <see cref="GeneratedArtifact.LegacySignature"/>, which proves where the file came from
    /// but not that it was left unedited.
    /// </remarks>
    /// <param name="artifact">The artifact the install writes.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RemoveGeneratedFileAsync(
        GeneratedArtifact artifact, IntegrationContext context, CancellationToken cancellationToken)
    {
        var path = artifact.Path;
        if (!File.Exists(path) || KeptForAnotherIntegration(path, context))
        {
            return;
        }

        var existing = await IntegratorHelpers.TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            Keep(path, "it could not be read", context);
        }
        else if (ArtifactStamping.IsAuthentic(existing))
        {
            DeleteFile(path, context);
        }
        else
        {
            Keep(
                path,
                ArtifactStamping.HasStamp(existing)
                    ? "it was edited after dtk generated it"
                    : "it carries no dtk provenance stamp, so dtk cannot tell whether it was edited",
                context);
        }
    }

    /// <summary>
    /// Deletes a file the install writes whole without a stamp, when its content is exactly what this dtk writes or
    /// what a released dtk wrote (line endings aside).
    /// </summary>
    /// <param name="path">The file the install writes.</param>
    /// <param name="content">The content the install writes.</param>
    /// <param name="releasedHashes">
    /// The SHA-256 hashes of the bodies released dtk versions wrote there, e.g.
    /// <see cref="IntegrationInstructions.ReleasedCursorRuleHashes"/>.
    /// </param>
    /// <param name="installCommand">
    /// The <c>dtk init</c> command that writes the file, e.g. <c>dtk init aider --global</c>, for the note on a kept
    /// file.
    /// </param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RemoveOwnedFileAsync(
        string path,
        string content,
        IReadOnlyList<string> releasedHashes,
        string installCommand,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(releasedHashes);

        if (!File.Exists(path))
        {
            return;
        }

        var existing = await IntegratorHelpers.TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            Keep(path, "it could not be read", context);
        }
        else if (string.Equals(existing, content.ReplaceLineEndings("\n"), StringComparison.Ordinal)
                 || releasedHashes.Contains(HashOwnedFile(existing), StringComparer.Ordinal))
        {
            DeleteFile(path, context);
        }
        else
        {
            context.Skipped.Add(path);
            context.Notes.Add(
                $"{path} was kept: it differs from what this dtk and every earlier release wrote, so it was edited " +
                "or comes from a dtk version this one does not know. To remove it anyway, run " +
                $"`{installCommand} --force` to restore dtk's version, then `{installCommand} --uninstall`.");
        }
    }

    /// <summary>
    /// The lowercase hex SHA-256 of a whole-file body's UTF-8 bytes, as recorded in
    /// <see cref="IntegrationInstructions.ReleasedCursorRuleHashes"/> and its siblings.
    /// </summary>
    /// <param name="body">The file's content, already read with <c>\n</c> line endings.</param>
    internal static string HashOwnedFile(string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    /// <summary>
    /// Deletes the Python hook script an older dtk installed, once no registration file runs it and dtk can prove it
    /// wrote the script.
    /// </summary>
    /// <param name="scriptPath">Where the Python script would be.</param>
    /// <param name="registrationFiles">Every file the harness reads hooks from beside the script's directory.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RemoveLegacyHookScriptAsync(
        string scriptPath, IReadOnlyList<string> registrationFiles, IntegrationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registrationFiles);

        if (!File.Exists(scriptPath))
        {
            return;
        }

        foreach (var file in registrationFiles.Where(File.Exists))
        {
            var registration = await IntegratorHelpers.TryReadExistingAsync(file, cancellationToken).ConfigureAwait(false);
            if (registration?.Contains(IntegratorHelpers.LegacyHookScriptName, StringComparison.Ordinal) != false)
            {
                Keep(scriptPath, $"{file} may still run it", context);
                return;
            }
        }

        var existing = await IntegratorHelpers.TryReadExistingAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        if (existing is { } script && IntegratorHelpers.IsDtkLegacyHookScript(script))
        {
            DeleteFile(scriptPath, context);
        }
        else
        {
            Keep(scriptPath, "it differs from what dtk wrote", context);
        }
    }

    /// <summary>
    /// Whether an installation's registration is in place, as far as a text search can tell: its file runs
    /// <c>dtk hook &lt;provider&gt;</c> (or, for a generated plugin, calls it), or still runs the Python script an
    /// older dtk registered. A file that cannot be read counts as registered, so its shared files are kept.
    /// </summary>
    /// <param name="installation">The installation to look for.</param>
    internal static bool IsRegistered(HookInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        try
        {
            if (!File.Exists(installation.RegistrationPath))
            {
                return false;
            }

            var content = File.ReadAllText(installation.RegistrationPath);
            return installation.PluginArtifact is null
                ? content.Contains(HookCommands.Invocation(installation.ProviderName), StringComparison.Ordinal)
                  || content.Contains(IntegratorHelpers.LegacyHookScriptName, StringComparison.Ordinal)
                : content.Contains(OpenCodePlugin.InvocationSignature, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Records a file kept because dtk cannot prove its content is its own.</summary>
    /// <param name="path">The file kept.</param>
    /// <param name="reason">Why, completing "was kept: …".</param>
    /// <param name="context">The uninstall context.</param>
    internal static void Keep(string path, string reason, IntegrationContext context)
    {
        context.Skipped.Add(path);
        context.Notes.Add($"{path} was kept: {reason}. Delete it yourself if nothing needs it.");
    }

    /// <summary>Deletes a file dtk has proved is its own, then the directories that leaves empty.</summary>
    /// <param name="path">The file to delete.</param>
    /// <param name="context">The uninstall context.</param>
    internal static void DeleteFile(string path, IntegrationContext context)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Keep(path, $"it could not be deleted ({ex.Message})", context);
            return;
        }

        context.Removed.Add(path);
        PruneEmptyDirectories(Path.GetDirectoryName(path), context.PruneBoundary);
    }

    /// <summary>
    /// Whether dtk's part of a shared file stays because another installed dtk integration also uses it; records the
    /// file as unchanged with a note when it does.
    /// </summary>
    /// <param name="path">The shared file.</param>
    /// <param name="context">The uninstall context.</param>
    private static bool KeptForAnotherIntegration(string path, IntegrationContext context)
    {
        if (!context.SharedInUse.TryGetValue(path, out var provider))
        {
            return false;
        }

        context.Unchanged.Add(path);
        context.Notes.Add(
            $"{path} still holds dtk's instructions: dtk's {provider} integration uses them too. "
            + "Uninstall that one as well to remove them.");
        return true;
    }

    /// <summary>Removes <paramref name="directory"/> and its ancestors while empty, stopping below <paramref name="boundary"/>.</summary>
    /// <param name="directory">The directory a file was just deleted from.</param>
    /// <param name="boundary">The directory never removed, nor anything outside it; <see langword="null"/> removes none.</param>
    private static void PruneEmptyDirectories(string? directory, string? boundary)
    {
        if (directory is null || boundary is null)
        {
            return;
        }

        var root = Path.GetFullPath(boundary);
        for (var current = Path.GetFullPath(directory); IsStrictlyInside(current, root); current = Path.GetDirectoryName(current)!)
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    return;
                }

                Directory.Delete(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file is gone, which is what mattered; a directory that cannot be listed or removed stays.
                return;
            }
        }
    }

    private static bool IsStrictlyInside(string directory, string root)
    {
        var relative = Path.GetRelativePath(root, directory);
        return relative != "."
               && relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
               && !Path.IsPathRooted(relative);
    }

    /// <summary>Reads a file as written, or returns <see langword="null"/> when it cannot be read.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<string?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
