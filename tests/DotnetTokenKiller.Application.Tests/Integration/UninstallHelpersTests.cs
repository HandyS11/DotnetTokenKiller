using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class UninstallHelpersTests : IDisposable
{
    private const string Marker = "<!-- dtk -->";
    private const string EndMarker = "<!-- /dtk -->";
    private const string Section = "<!-- dtk -->\n## dtk\nbody\n<!-- /dtk -->";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-uninstall-test-{Guid.NewGuid()}");

    public UninstallHelpersTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private IntegrationContext Uninstall(IReadOnlyDictionary<string, string>? sharedInUse = null) =>
        IntegrationContext.ForUninstall(_tempDir, sharedInUse ?? new Dictionary<string, string>());

    private static HookRegistrationSpec Spec(string path, string command = "dtk hook claude", string container = "hooks") =>
        new(path, "PreToolUse", "Bash", command, ContainerKey: container);

    private static JsonObject HandlerGroup(string command) =>
        new() { ["matcher"] = "Bash", ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command }) };

    // --- RemoveHookRegistrationAsync ---

    [Fact]
    public async Task RemoveHookRegistrationAsync_FileWithOnlyDtksHook_DeletesItAndTheDirectoriesItEmptied()
    {
        var path = Path.Combine(_tempDir, ".claude", "settings.json");
        await IntegratorHelpers.WriteHookRegistrationAsync(Spec(path), new IntegrationContext(false), default);
        var context = Uninstall();

        await UninstallHelpers.RemoveHookRegistrationAsync(Spec(path), context, default);

        context.Removed.Should().Equal(path);
        Directory.Exists(Path.Combine(_tempDir, ".claude")).Should().BeFalse();
        Directory.Exists(_tempDir).Should().BeTrue("the prune boundary itself is never removed");
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_OtherSettingsAndHooks_RemovesOnlyDtksEntryAndRestoresTheFile()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var original = new JsonObject
        {
            ["model"] = "opus",
            ["hooks"] = new JsonObject { ["PreToolUse"] = new JsonArray(HandlerGroup("rtk hook claude")) }
        };
        await IntegratorHelpers.WriteSettingsJsonAsync(path, original, default);
        var before = await File.ReadAllTextAsync(path);
        await IntegratorHelpers.WriteHookRegistrationAsync(Spec(path), new IntegrationContext(false), default);
        var context = Uninstall();

        await UninstallHelpers.RemoveHookRegistrationAsync(Spec(path), context, default);

        context.Updated.Should().Equal(path);
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_GroupSharedWithAnotherHandler_KeepsTheGroupAndTheOtherHandler()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var group = HandlerGroup("echo mine");
        ((JsonArray)group["hooks"]!).Add((JsonNode)new JsonObject { ["type"] = "command", ["command"] = "dtk hook claude" });
        await IntegratorHelpers.WriteSettingsJsonAsync(
            path, new JsonObject { ["hooks"] = new JsonObject { ["PreToolUse"] = new JsonArray(group) } }, default);

        await UninstallHelpers.RemoveHookRegistrationAsync(Spec(path), Uninstall(), default);

        var handlers = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PreToolUse"]![0]!["hooks"]!.AsArray();
        handlers.Should().ContainSingle().Which!["command"]!.GetValue<string>().Should().Be("echo mine");
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_QuotedVariantAndLegacyPythonEntry_RemovesBoth()
    {
        // The same entries the install treats as dtk's own, so uninstall and install never disagree on what is dtk's.
        var path = Path.Combine(_tempDir, "settings.json");
        await IntegratorHelpers.WriteSettingsJsonAsync(path, new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray(
                    HandlerGroup("\"dtk\" hook claude"),
                    HandlerGroup(LegacyHookFixtures.PythonCommand("/x/.claude/hooks/dotnet-to-dtk.py")))
            }
        }, default);
        var context = Uninstall();

        await UninstallHelpers.RemoveHookRegistrationAsync(Spec(path), context, default);

        context.Removed.Should().Equal(path);
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_NoDtkEntry_ReportsUnchangedAndLeavesTheFileByteIdentical()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        const string content = "{ \"hooks\": { \"PreToolUse\": [] }, \"x\": 1 }";
        await File.WriteAllTextAsync(path, content);
        var context = Uninstall();

        await UninstallHelpers.RemoveHookRegistrationAsync(Spec(path), context, default);

        context.Unchanged.Should().Equal(path);
        (await File.ReadAllTextAsync(path)).Should().Be(content);
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_MissingFile_ReportsNothing()
    {
        var context = Uninstall();

        await UninstallHelpers.RemoveHookRegistrationAsync(Spec(Path.Combine(_tempDir, "none.json")), context, default);

        context.Removed.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
        context.Unchanged.Should().BeEmpty();
        context.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_NamedContainer_DropsOnlyDtksGroup()
    {
        var path = Path.Combine(_tempDir, "hooks.json");
        await IntegratorHelpers.WriteSettingsJsonAsync(path, new JsonObject
        {
            ["other"] = new JsonObject { ["PreToolUse"] = new JsonArray(HandlerGroup("echo other")) }
        }, default);
        var before = await File.ReadAllTextAsync(path);
        var spec = Spec(path, "dtk hook antigravity || exit 0", container: "dtk");
        await IntegratorHelpers.WriteHookRegistrationAsync(spec, new IntegrationContext(false), default);

        await UninstallHelpers.RemoveHookRegistrationAsync(spec, Uninstall(), default);

        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_MalformedJson_Throws()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(path, "{ not json");

        var act = () => UninstallHelpers.RemoveHookRegistrationAsync(Spec(path), Uninstall(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_NullContainer_RemovesDtkAndKeepsOtherRootEvents()
    {
        var path = Path.Combine(_tempDir, "hooks.v1.json");
        await File.WriteAllTextAsync(path, """
            {"PostToolUse":[{"matcher":"*","hooks":[{"type":"command","command":"lint"}]}],
             "PreToolUse":[{"matcher":"exec","hooks":[{"type":"command","command":"dtk hook devin","timeout":10}]}]}
            """);
        var context = IntegrationContext.ForUninstall(_tempDir, new Dictionary<string, string>());

        await UninstallHelpers.RemoveHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "exec", "dtk hook devin", 10, ContainerKey: null), context, default);

        JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject().Select(pair => pair.Key).Should().Equal("PostToolUse");
        context.Updated.Should().Equal(path);
    }

    [Fact]
    public async Task RemoveHookRegistrationAsync_NullContainer_DeletesTheFileItEmpties()
    {
        var path = Path.Combine(_tempDir, ".devin", "hooks.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            """{"PreToolUse":[{"matcher":"exec","hooks":[{"type":"command","command":"dtk hook devin","timeout":10}]}]}""");
        var context = IntegrationContext.ForUninstall(_tempDir, new Dictionary<string, string>());

        await UninstallHelpers.RemoveHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "exec", "dtk hook devin", 10, ContainerKey: null), context, default);

        File.Exists(path).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(path)).Should().BeFalse("the uninstall prunes the directory it empties");
    }

    // --- RemoveSectionAsync ---

    [Fact]
    public async Task RemoveSectionAsync_SectionAppendedToUserContent_RestoresTheOriginal()
    {
        const string original = "# Mine\n\nNotes.\n";
        var path = Path.Combine(_tempDir, "AGENTS.md");
        await File.WriteAllTextAsync(path, original);
        await IntegratorHelpers.WriteSectionBasedFileAsync(path, Marker, EndMarker, Section, new IntegrationContext(true), default);
        var context = Uninstall();

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, context, default);

        context.Updated.Should().Equal(path);
        (await File.ReadAllTextAsync(path)).Should().Be(original);
    }

    [Fact]
    public async Task RemoveSectionAsync_SectionBetweenUserContent_KeepsBothSides()
    {
        var path = Path.Combine(_tempDir, "AGENTS.md");
        await File.WriteAllTextAsync(path, "before\n" + Section + "\nafter\n");

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, Uninstall(), default);

        (await File.ReadAllTextAsync(path)).Should().Be("before\nafter\n");
    }

    [Fact]
    public async Task RemoveSectionAsync_CrLfFile_RemovesTheSectionsLineBreakAndKeepsTheRestAsWritten()
    {
        var path = Path.Combine(_tempDir, "AGENTS.md");
        await File.WriteAllTextAsync(path, "before\r\n" + Section.ReplaceLineEndings("\r\n") + "\r\nafter\r\n");

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, Uninstall(), default);

        (await File.ReadAllTextAsync(path)).Should().Be("before\r\nafter\r\n");
    }

    [Fact]
    public async Task RemoveSectionAsync_FileHoldingOnlyTheSection_DeletesIt()
    {
        var path = Path.Combine(_tempDir, "docs", "AGENTS.md");
        await IntegratorHelpers.WriteSectionBasedFileAsync(path, Marker, EndMarker, Section + "\n", new IntegrationContext(false), default);
        var context = Uninstall();

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, context, default);

        context.Removed.Should().Equal(path);
        Directory.Exists(Path.Combine(_tempDir, "docs")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveSectionAsync_NoMarker_ReportsUnchanged()
    {
        var path = Path.Combine(_tempDir, "AGENTS.md");
        await File.WriteAllTextAsync(path, "mine\n");
        var context = Uninstall();

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, context, default);

        context.Unchanged.Should().Equal(path);
        (await File.ReadAllTextAsync(path)).Should().Be("mine\n");
    }

    [Fact]
    public async Task RemoveSectionAsync_MissingEndMarker_KeepsTheFileWithANote()
    {
        var path = Path.Combine(_tempDir, "AGENTS.md");
        const string content = "mine\n<!-- dtk -->\nbody\nmore of mine\n";
        await File.WriteAllTextAsync(path, content);
        var context = Uninstall();

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, context, default);

        context.Skipped.Should().Equal(path);
        context.Notes.Should().ContainSingle().Which.Should().Contain(EndMarker);
        (await File.ReadAllTextAsync(path)).Should().Be(content);
    }

    [Fact]
    public async Task RemoveSectionAsync_SharedFileAnotherIntegrationUses_LeavesItWithANote()
    {
        var path = Path.Combine(_tempDir, "AGENTS.md");
        await File.WriteAllTextAsync(path, Section);
        var context = Uninstall(new Dictionary<string, string> { [path] = "opencode" });

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, context, default);

        context.Unchanged.Should().Equal(path);
        context.Notes.Should().ContainSingle().Which.Should().Contain("opencode");
        (await File.ReadAllTextAsync(path)).Should().Be(Section);
    }

    [Fact]
    public async Task RemoveSectionAsync_MergedContent_RemovedBeforeTheSection()
    {
        var path = Path.Combine(_tempDir, "conf.yml");
        await File.WriteAllTextAsync(path, "read: [a, dtk.md]\n" + Section);

        await UninstallHelpers.RemoveSectionAsync(
            path, Marker, EndMarker, Uninstall(), default, content => content.Replace(", dtk.md", string.Empty, StringComparison.Ordinal));

        (await File.ReadAllTextAsync(path)).Should().Be("read: [a]\n");
    }

    // --- RemoveGeneratedFileAsync ---

    private GeneratedArtifact Artifact() =>
        new(Path.Combine(_tempDir, "skills", "dtk", "SKILL.md"), "name: dtk\nbody\n", StampStyle.HtmlComment, "name: dtk");

    [Fact]
    public async Task RemoveGeneratedFileAsync_StampVerifies_DeletesItAndItsEmptyFolders()
    {
        var artifact = Artifact();
        await IntegratorHelpers.WriteGeneratedFileAsync(artifact, new IntegrationContext(false), default);
        var context = Uninstall();

        await UninstallHelpers.RemoveGeneratedFileAsync(artifact, context, default);

        context.Removed.Should().Equal(artifact.Path);
        Directory.Exists(Path.Combine(_tempDir, "skills")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveGeneratedFileAsync_StampFromAnOlderTemplate_StillDeletes()
    {
        var artifact = Artifact();
        Directory.CreateDirectory(Path.GetDirectoryName(artifact.Path)!);
        await File.WriteAllTextAsync(artifact.Path, ArtifactStamping.Apply("name: dtk\nolder body\n", artifact.Style));
        var context = Uninstall();

        await UninstallHelpers.RemoveGeneratedFileAsync(artifact, context, default);

        context.Removed.Should().Equal(artifact.Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveGeneratedFileAsync_EditedOrUnstamped_KeepsItWithANote(bool stamped)
    {
        var artifact = Artifact();
        Directory.CreateDirectory(Path.GetDirectoryName(artifact.Path)!);
        var content = stamped
            ? ArtifactStamping.Apply(artifact.Body, artifact.Style).Replace("body", "my edit", StringComparison.Ordinal)
            : artifact.Body;
        await File.WriteAllTextAsync(artifact.Path, content);
        var context = Uninstall();

        await UninstallHelpers.RemoveGeneratedFileAsync(artifact, context, default);

        context.Skipped.Should().Equal(artifact.Path);
        context.Notes.Should().ContainSingle().Which.Should().StartWith($"{artifact.Path} was kept");
        (await File.ReadAllTextAsync(artifact.Path)).Should().Be(content);
    }

    // --- RemoveOwnedFileAsync ---

    [Fact]
    public async Task RemoveOwnedFileAsync_ExactlyWhatDtkWrites_Deletes()
    {
        var path = Path.Combine(_tempDir, ".cursor", "rules", "dtk.mdc");
        await IntegratorHelpers.WriteFileAsync(path, "rule\n", new IntegrationContext(false), default);
        var context = Uninstall();

        await UninstallHelpers.RemoveOwnedFileAsync(path, "rule\n", [], "dtk init cursor", context, default);

        context.Removed.Should().Equal(path);
        Directory.Exists(Path.Combine(_tempDir, ".cursor")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveOwnedFileAsync_Edited_KeepsIt()
    {
        var path = Path.Combine(_tempDir, "dtk.mdc");
        await File.WriteAllTextAsync(path, "rule\nmine\n");
        var context = Uninstall();

        await UninstallHelpers.RemoveOwnedFileAsync(path, "rule\n", [], "dtk init cursor", context, default);

        context.Skipped.Should().Equal(path);
        File.Exists(path).Should().BeTrue();
    }

    // --- RemoveLegacyHookScriptAsync ---

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_DtksScriptNoLongerRegistered_Deletes()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", IntegratorHelpers.LegacyHookScriptName);
        LegacyHookFixtures.WriteStampedScript(script);
        var context = Uninstall();

        await UninstallHelpers.RemoveLegacyHookScriptAsync(script, [Path.Combine(_tempDir, ".claude", "settings.json")], context, default);

        context.Removed.Should().Equal(script);
        Directory.Exists(Path.Combine(_tempDir, ".claude")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_StillRegistered_KeepsIt()
    {
        var script = Path.Combine(_tempDir, "hooks", IntegratorHelpers.LegacyHookScriptName);
        LegacyHookFixtures.WriteStampedScript(script);
        var local = Path.Combine(_tempDir, "settings.local.json");
        await File.WriteAllTextAsync(local, LegacyHookFixtures.PythonCommand(script));
        var context = Uninstall();

        await UninstallHelpers.RemoveLegacyHookScriptAsync(script, [local], context, default);

        context.Skipped.Should().Equal(script);
        File.Exists(script).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_EditedScript_KeepsIt()
    {
        var script = Path.Combine(_tempDir, "hooks", IntegratorHelpers.LegacyHookScriptName);
        LegacyHookFixtures.WriteEditedScript(script);
        var context = Uninstall();

        await UninstallHelpers.RemoveLegacyHookScriptAsync(script, [], context, default);

        context.Skipped.Should().Equal(script);
    }

    // --- IsRegistered ---

    [Fact]
    public async Task IsRegistered_TracksTheRegistrationFile()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var installation = new HookInstallation(
            "claude", HookScope.Project, path, HookCommands.Invocation("claude"), null, HookPayloadKind.ClaudeCode);

        UninstallHelpers.IsRegistered(installation).Should().BeFalse("the file does not exist");

        await File.WriteAllTextAsync(path, "{}");
        UninstallHelpers.IsRegistered(installation).Should().BeFalse("no entry runs dtk");

        await IntegratorHelpers.WriteHookRegistrationAsync(Spec(path), new IntegrationContext(false), default);
        UninstallHelpers.IsRegistered(installation).Should().BeTrue();
    }

    [Fact]
    public async Task IsRegistered_ScriptRegistration_IgnoresCommentedOutLines()
    {
        var path = Path.Combine(_tempDir, ".crushrc");
        var installation = new HookInstallation(
            "crush", HookScope.Project, path, HookCommands.Invocation("crush"), null, HookPayloadKind.Crush)
        {
            IsScriptRegistration = true
        };
        Directory.CreateDirectory(_tempDir);

        await File.WriteAllTextAsync(path, "# hook add PreToolUse --name dtk --command 'dtk hook crush'\n");
        UninstallHelpers.IsRegistered(installation).Should().BeFalse("a commented-out line registers nothing");

        await File.WriteAllTextAsync(path, "  hook add PreToolUse --name dtk --command 'dtk hook crush'\n");
        UninstallHelpers.IsRegistered(installation).Should().BeTrue();
    }

    // --- Unreadable and undeletable files ---

    [Fact]
    public async Task RemoveSectionAsync_UnreadableFile_KeepsItWithANote()
    {
        var path = Path.Combine(_tempDir, "AGENTS.md");
        await File.WriteAllTextAsync(path, Section);
        if (!TryMakeUnreadable(path))
        {
            return;
        }

        var context = Uninstall();

        await UninstallHelpers.RemoveSectionAsync(path, Marker, EndMarker, context, default);

        context.Skipped.Should().Equal(path);
        context.Notes.Should().ContainSingle().Which.Should().Contain("could not be read");
    }

    [Fact]
    public async Task RemoveGeneratedFileAsync_UnreadableFile_KeepsItWithANote()
    {
        var artifact = Artifact();
        await IntegratorHelpers.WriteGeneratedFileAsync(artifact, new IntegrationContext(false), default);
        if (!TryMakeUnreadable(artifact.Path))
        {
            return;
        }

        var context = Uninstall();

        await UninstallHelpers.RemoveGeneratedFileAsync(artifact, context, default);

        context.Skipped.Should().Equal(artifact.Path);
        context.Notes.Should().ContainSingle().Which.Should().Contain("could not be read");
        File.Exists(artifact.Path).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveOwnedFileAsync_UnreadableFile_KeepsItWithANote()
    {
        var path = Path.Combine(_tempDir, "dtk.mdc");
        await File.WriteAllTextAsync(path, "rule\n");
        if (!TryMakeUnreadable(path))
        {
            return;
        }

        var context = Uninstall();

        await UninstallHelpers.RemoveOwnedFileAsync(path, "rule\n", [], "dtk init cursor", context, default);

        context.Skipped.Should().Equal(path);
        context.Notes.Should().ContainSingle().Which.Should().Contain("could not be read");
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_RegistrationWithoutTheScript_Deletes()
    {
        var script = Path.Combine(_tempDir, "hooks", IntegratorHelpers.LegacyHookScriptName);
        LegacyHookFixtures.WriteStampedScript(script);
        var settings = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(settings, "{}");
        var context = Uninstall();

        await UninstallHelpers.RemoveLegacyHookScriptAsync(script, [settings], context, default);

        context.Removed.Should().Equal(script);
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_UnreadableRegistration_KeepsTheScript()
    {
        var script = Path.Combine(_tempDir, "hooks", IntegratorHelpers.LegacyHookScriptName);
        LegacyHookFixtures.WriteStampedScript(script);
        var settings = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(settings, "{}");
        if (!TryMakeUnreadable(settings))
        {
            return;
        }

        var context = Uninstall();

        await UninstallHelpers.RemoveLegacyHookScriptAsync(script, [settings], context, default);

        context.Skipped.Should().Equal(script);
        context.Notes.Should().ContainSingle().Which.Should().Contain($"{settings} may still run it");
    }

    [Fact]
    public async Task IsRegistered_UnreadableFile_CountsAsRegistered()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        await IntegratorHelpers.WriteHookRegistrationAsync(Spec(path), new IntegrationContext(false), default);
        if (!TryMakeUnreadable(path))
        {
            return;
        }

        var installation = new HookInstallation(
            "claude", HookScope.Project, path, HookCommands.Invocation("claude"), null, HookPayloadKind.ClaudeCode);

        UninstallHelpers.IsRegistered(installation).Should().BeTrue("a file dtk cannot read may still run dtk");
    }

    [Fact]
    public async Task DeleteFile_DirectoryNotWritable_KeepsTheFileWithANote()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(_tempDir, "locked");
        var path = Path.Combine(directory, "dtk.js");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "x");
        var originalMode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (CanCreateFileIn(directory))
            {
                return;
            }

            var context = Uninstall();

            UninstallHelpers.DeleteFile(path, context);

            File.Exists(path).Should().BeTrue();
            context.Removed.Should().BeEmpty();
            context.Skipped.Should().Equal(path);
            context.Notes.Should().ContainSingle().Which.Should().Contain("could not be deleted");
        }
        finally
        {
            File.SetUnixFileMode(directory, originalMode);
        }
    }

    [Fact]
    public async Task DeleteFile_EmptiedDirectoryCannotBeRemoved_StillReportsTheFileRemoved()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = Path.Combine(_tempDir, "provider");
        var path = Path.Combine(parent, "plugins", "dtk.js");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "x");
        var originalMode = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (CanCreateFileIn(parent))
            {
                return;
            }

            var context = Uninstall();

            UninstallHelpers.DeleteFile(path, context);

            File.Exists(path).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(path)).Should().BeTrue("its parent does not allow removing it");
            context.Removed.Should().Equal(path);
        }
        finally
        {
            File.SetUnixFileMode(parent, originalMode);
        }
    }

    /// <summary>
    /// Makes <paramref name="path"/> unreadable; <see langword="false"/> where that cannot be done (Windows, or a user
    /// such as root who reads it anyway), so the test returns without asserting.
    /// </summary>
    /// <param name="path">The file to make unreadable.</param>
    private static bool TryMakeUnreadable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            File.ReadAllBytes(path);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Whether this user can create a file in <paramref name="directory"/> despite its mode (true as root).</summary>
    /// <param name="directory">The directory to probe.</param>
    private static bool CanCreateFileIn(string directory)
    {
        var probe = Path.Combine(directory, ".dtk-writability-probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
