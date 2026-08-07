using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class IntegratorHelpersTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-helpers-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    // --- WriteFileAsync ---

    [Fact]
    public async Task WriteFileAsync_NewFile_CreatesFileAndAddsToCreated()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "sub", "file.md");

        await IntegratorHelpers.WriteFileAsync(path, "content", context, CancellationToken.None);

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("content");
        context.Created.Should().ContainSingle().Which.Should().Be(path);
        context.Updated.Should().BeEmpty();
        context.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFile_NoForce_SkipsFile()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "file.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "original");

        await IntegratorHelpers.WriteFileAsync(path, "new", context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("original");
        context.Skipped.Should().ContainSingle().Which.Should().Be(path);
        context.Created.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFile_WithForce_OverwritesAndAddsToUpdated()
    {
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "file.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "original");

        await IntegratorHelpers.WriteFileAsync(path, "new", context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("new");
        context.Updated.Should().ContainSingle().Which.Should().Be(path);
        context.Created.Should().BeEmpty();
        context.Skipped.Should().BeEmpty();
    }

    // --- WriteSectionBasedFileAsync ---

    [Fact]
    public async Task WriteSectionBasedFileAsync_NewFile_CreatesWithSection()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "instructions.md");
        const string section = "<!-- dtk -->\nDTK content\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        File.Exists(path).Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(section);
        context.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_ExistingWithoutMarker_NoForce_SkipsFile()
    {
        // Decision: section-append without --force on an existing user file must SKIP, matching
        // WriteFileAsync's documented contract, instead of silently appending into it.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        const string original = "# My Instructions\n\nExisting content.";
        await File.WriteAllTextAsync(path, original);
        const string section = "<!-- dtk -->\nDTK content\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(original);
        context.Skipped.Should().ContainSingle();
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_ExistingWithoutMarker_WithForce_AppendsSection()
    {
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "# My Instructions\n\nExisting content.");
        const string section = "<!-- dtk -->\nDTK content\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("# My Instructions");
        content.Should().Contain("<!-- dtk -->");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_ExistingWithMarker_NoForce_SkipsFile()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "# Header\n<!-- dtk -->\nOLD\n<!-- /dtk -->\n# Footer");
        const string section = "<!-- dtk -->\nNEW\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("OLD");
        context.Skipped.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_ExistingWithMarker_WithForce_ReplacesSection()
    {
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "# Header\n<!-- dtk -->\nOLD\n<!-- /dtk -->\n# Footer");
        const string section = "<!-- dtk -->\nNEW\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().NotContain("OLD");
        content.Should().Contain("NEW");
        content.Should().Contain("# Header");
        content.Should().Contain("# Footer");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_WhitespaceOnlyFile_WithForce_CreatesSection()
    {
        // A whitespace-only file still "exists", so this requires --force like any other
        // pre-existing file (matching WriteFileAsync's contract).
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "   \n  \n  ");
        const string section = "<!-- dtk -->\nDTK\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("<!-- dtk -->");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_WhitespaceOnlyFile_NoForce_SkipsFile()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        const string original = "   \n  \n  ";
        await File.WriteAllTextAsync(path, original);
        const string section = "<!-- dtk -->\nDTK\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(original);
        context.Skipped.Should().ContainSingle();
    }

    // --- MergeJsonSettingsAsync ---

    [Fact]
    public async Task MergeJsonSettingsAsync_NewFile_CreatesWithHookEntry()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        File.Exists(path).Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "python3 hook.py"
                      }
                    ]
                  }
                ]
              }
            }
            """);
        context.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingWithDuplicateHook_ReportsUnchanged()
    {
        // The hook entry is already registered: there is nothing to write, and --force would not
        // change that, so this is a force-independent no-op reported as unchanged, not skipped.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "python3 hook.py"
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        context.Unchanged.Should().ContainSingle();
        context.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_InvalidJson_ThrowsInvalidOperation()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "not json at all {{{");

        var hookEntry = new JsonObject();

        var act = () => IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "cmd", context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to parse*");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_NonObjectRoot_ThrowsInvalidOperation()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "[1, 2, 3]");

        var hookEntry = new JsonObject();

        var act = () => IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "cmd", context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must contain a JSON object at the root*");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_NullLiteralRoot_NamesWhatItFoundInsteadOfCrashing()
    {
        // JsonNode.Parse("null") returns null rather than a node, so the "what did we find instead"
        // message has to describe it without dereferencing anything.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "null");

        var hookEntry = new JsonObject();

        var act = () => IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "cmd", context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*found 'null'*");
    }

    // --- IntegrationContext ---

    [Fact]
    public void IntegrationContext_ToResult_ReturnsCorrectResult()
    {
        var context = new IntegrationContext(true);
        context.Created.Add("created.md");
        context.Updated.Add("updated.md");
        context.Skipped.Add("skipped.md");

        var result = context.ToResult();

        result.CreatedFiles.Should().ContainSingle().Which.Should().Be("created.md");
        result.UpdatedFiles.Should().ContainSingle().Which.Should().Be("updated.md");
        result.SkippedFiles.Should().ContainSingle().Which.Should().Be("skipped.md");
    }

    [Fact]
    public void IntegrationContext_Force_ReflectsConstructorArg()
    {
        new IntegrationContext(true).Force.Should().BeTrue();
        new IntegrationContext(false).Force.Should().BeFalse();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingWithHooksButDifferentEventKey_AddsNewEntry()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "hooks": {
                "PostToolUse": []
              }
            }
            """);

        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("PreToolUse");
        content.Should().Contain("PostToolUse");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_HooksPropertyIsNotObject_Throws()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """{"hooks": "not-an-object"}""");

        var hookEntry = new JsonObject();

        var act = () => IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "cmd", context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unexpected type*");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_EventKeyPropertyIsNotArray_Throws()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """{"hooks": {"PreToolUse": "not-an-array"}}""");

        var hookEntry = new JsonObject();

        var act = () => IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "cmd", context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unexpected type*");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_HookArrayItemNotObject_SkipsAndAddsNew()
    {
        // IsHookAlreadyRegistered skips non-JsonObject items → the hook is considered not registered
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "hooks": {
                "PreToolUse": [
                  "just-a-string-not-object"
                ]
              }
            }
            """);

        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("python3 hook.py");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_HookEntryWithoutInnerHooksArray_SkipsAndAddsNew()
    {
        // IsHookAlreadyRegistered skips entries where "hooks" is not a JsonArray
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": "not-an-array"
                  }
                ]
              }
            }
            """);

        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("python3 hook.py");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_ExistingWithStartMarkerButMissingEndMarker_PreservesTrailingContent()
    {
        // A missing end marker (e.g. a user accidentally deleted just the "<!-- /dtk -->" line)
        // means the dtk-managed span can no longer be reliably identified. The fix must never
        // delete content in that situation — it inserts the fresh section where the begin marker
        // was and leaves everything that followed untouched, even though that leaves the stale
        // "incomplete section" text dangling without markers around it.
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        // File has the begin marker but no end marker
        await File.WriteAllTextAsync(path, "# Header\n<!-- dtk -->\nOLD incomplete section");
        const string section = "<!-- dtk -->\nNEW\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("# Header");
        content.Should().Contain("NEW");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("OLD incomplete section", "damaged old content must be preserved, not deleted");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteSectionBasedFileAsync_MissingEndMarker_PreservesRealUserContentAfterOldMarker()
    {
        // Simulates a user who deleted only the "<!-- /dtk -->" line, leaving their own content
        // immediately after the (now unterminated) dtk section. That real content must survive.
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(
            path,
            "# Header\n<!-- dtk -->\nOLD\n\n## My Own Notes\nDon't delete me!");
        const string section = "<!-- dtk -->\nNEW\n<!-- /dtk -->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!-- dtk -->", "<!-- /dtk -->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("# Header");
        content.Should().Contain("NEW");
        content.Should().Contain("## My Own Notes");
        content.Should().Contain("Don't delete me!");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingFileNoHooksProperty_AddsHooksAndCreatesEntry()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """{"someOtherProp": true}""");

        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("PreToolUse");
        content.Should().Contain("python3 hook.py");
        content.Should().Contain("someOtherProp");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_InnerHookWithDifferentCommand_DoesNotSkip()
    {
        // Entry has hooks array with a different command → not a duplicate, should add
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "other-hook.py"
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var hookEntry = new JsonObject
        {
            ["matcher"] = "run_shell_command",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("python3 hook.py");
        content.Should().Contain("other-hook.py");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingLegacyRelativeCommand_ReplacesWithNewProjectDirRootedCommand()
    {
        // Regression: a settings.json carrying the pre-fix relative hook command
        // ("python3 .claude/hooks/dotnet-to-dtk.py") must have that stale, broken entry REPLACED
        // by the new $..._PROJECT_DIR-rooted command — not have the new command appended
        // alongside it, which would leave the broken entry registered forever (even with --force).
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string legacyCommand = "python3 .claude/hooks/dotnet-to-dtk.py";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        [HooksProperty] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = legacyCommand
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        const string newCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = newCommand
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, newCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(newCommand);
        context.Updated.Should().ContainSingle();
        context.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingNewProjectDirRootedCommand_IsIdempotentAndReportsUnchanged()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string newCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        [HooksProperty] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = newCommand
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = newCommand
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, newCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(newCommand);
        // The command is already registered: nothing to write, and --force would not change that,
        // so this is a force-independent no-op reported as unchanged, not skipped.
        context.Unchanged.Should().ContainSingle();
        context.Skipped.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_BothLegacyAndNewCommands_RemovesLegacyDuplicate()
    {
        // Regression: if a settings.json ended up with BOTH the legacy relative command and the new
        // $..._PROJECT_DIR-rooted command (possible when an in-between build appended the new one
        // alongside the old before this cleanup existed), merging must drop the stale legacy entry
        // rather than early-returning on the new command and leaving the broken relative entry to
        // keep firing. Exactly one registration — the new command — must survive.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string legacyCommand = "python3 .claude/hooks/dotnet-to-dtk.py";
        const string newCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        [HooksProperty] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = legacyCommand
                            }
                        }
                    },
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        [HooksProperty] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = newCommand
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = newCommand
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, newCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(newCommand);
        context.Updated.Should().ContainSingle();
        context.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_LegacyDuplicateAlongsideForeignEntries_LeavesTheForeignEntriesAlone()
    {
        // PreToolUse is a shared array: other tools (and hand edits) put shapes there that dtk does
        // not recognise — a bare string, an entry with no inner hooks list. Pruning the stale legacy
        // registration must step over those rather than choke on them or drop them.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string legacyCommand = "python3 .claude/hooks/dotnet-to-dtk.py";
        const string newCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    "a bare string an unrelated tool left here",
                    new JsonObject { ["matcher"] = "Bash" },                       // no inner hooks list
                    new JsonObject { ["matcher"] = "Bash", [HooksProperty] = "not an array" },
                    // A non-object inside an inner hooks list, which both the search and the prune
                    // walk element by element.
                    new JsonObject { ["matcher"] = "Bash", [HooksProperty] = new JsonArray { "junk" } },
                    HookEntry(legacyCommand),
                    HookEntry(newCommand)
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(newCommand), newCommand, context, CancellationToken.None);

        // Read as raw text rather than through ReadInnerCommandsAsync: the whole point of this case
        // is that the array holds entries that helper (like any strict reader) cannot walk.
        var written = await File.ReadAllTextAsync(path);
        written.Should().NotContain(legacyCommand, "the stale legacy registration is what gets pruned");
        Regex.Matches(written, Regex.Escape("dotnet-to-dtk.py")).Should().ContainSingle();
        written.Should().Contain("a bare string an unrelated tool left here");
        written.Should().Contain("not an array");
        context.Updated.Should().ContainSingle();
    }

    private static JsonObject HookEntry(string command) => new()
    {
        ["matcher"] = "Bash",
        [HooksProperty] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "command",
                ["command"] = command
            }
        }
    };

    [Fact]
    public async Task WriteSectionBasedFileAsync_EndMarkerFoundAtIndexZero_UsesEndMarkerLengthWhenSplicing()
    {
        // Boundary: markers chosen so that BOTH the begin marker and the end marker resolve to
        // index 0 (the begin marker is a prefix of the end marker). The end-marker offset is
        // therefore exactly 0 — a valid hit that must NOT be treated as "end marker missing".
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "instructions.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "<!--/dtk-->\nUser tail");
        const string section = "<!--dtk-->\nNEW\n<!--/dtk-->";

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            path, "<!--", "<!--/dtk-->", section, context, CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(section + "\nUser tail");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteHookAndSettingsAsync_NewFiles_WritesScriptAndExactSettingsJson()
    {
        // Pins the exact hook-entry shape produced by WriteHookAndSettingsAsync: the "matcher",
        // "type" and "command" property names, the literal "command" type value, and the
        // indented serializer options used to persist the settings file.
        var context = new IntegrationContext(false);
        var scriptPath = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        const string script = "#!/usr/bin/env python3\nprint('hi')\n";
        var spec = new HookSpec(scriptPath, script, settingsPath, "PreToolUse", "Bash", "python3 hook.py");

        await IntegratorHelpers.WriteHookAndSettingsAsync(spec, context, CancellationToken.None);

        (await File.ReadAllTextAsync(scriptPath)).Should().Be(
            ArtifactStamping.Apply(script, StampStyle.HashComment));
        (await File.ReadAllTextAsync(settingsPath)).Should().Be(
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "Bash",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "python3 hook.py"
                      }
                    ]
                  }
                ]
              }
            }
            """);
        context.Created.Should().HaveCount(2);
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_EntryWithoutCommandProperty_IsNotTreatedAsLegacyMatch()
    {
        // The hook command has no quoted env-var segment, so no legacy command can be derived and
        // the legacy lookup must be skipped entirely. If it ran anyway with a null needle it would
        // "match" the existing command-less inner hook and overwrite it instead of appending.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "Bash",
                    "hooks": [
                      {
                        "type": "command"
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "python3 hook.py"
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, "python3 hook.py", context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be(
            """
            {
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "Bash",
                    "hooks": [
                      {
                        "type": "command"
                      }
                    ]
                  },
                  {
                    "matcher": "Bash",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "python3 hook.py"
                      }
                    ]
                  }
                ]
              }
            }
            """);
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_CommandStartingWithQuotedEnvVar_StillReplacesLegacyEntry()
    {
        // Boundary: the quoted env-var segment starts at index 0 (no interpreter prefix), so the
        // derived legacy command is the bare relative path.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string legacyCommand = ".claude/hooks/dotnet-to-dtk.py";
        const string newCommand = "\"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        [HooksProperty] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = legacyCommand
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = newCommand
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, newCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(newCommand);
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_EmptyEnvVarName_StillReplacesLegacyEntry()
    {
        // Boundary: the closing quote sits immediately after the opening "$ — zero characters
        // between the quotes — which is the tightest possible quoted segment.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string legacyCommand = "python3 hook.py";
        const string newCommand = """python3 "$"/hook.py""";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        [HooksProperty] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = legacyCommand
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = newCommand
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, newCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(newCommand);
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_CommandEndingWithClosingQuote_DerivesNoLegacyAndAppends()
    {
        // Boundary: the closing quote is the LAST character of the command, so there is no
        // character after it to inspect. Legacy derivation must bail out rather than read past
        // the end of the string.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        const string command = "python3 \"$CLAUDE_PROJECT_DIR\"";
        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, command, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(command);
        context.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_UnterminatedQuotedEnvVar_DerivesNoLegacyAndAppends()
    {
        // Boundary: an opening "$ with no closing quote at all.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        const string command = """/usr/bin/python3 "$CLAUDE_PROJECT_DIR/hook.py""";
        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, command, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(command);
        context.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_QuotedSegmentWithoutEnvVarSigil_DerivesNoLegacyAndAppends()
    {
        // Boundary: the command contains a quoted segment followed by '/', but the quote does not
        // open an env var ("$). Derivation must stop at the missing "$ and never fall through to
        // the slicing logic with a negative offset.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        const string command = """python3 "/opt/dtk"/hook.py""";
        var hookEntry = new JsonObject
        {
            ["matcher"] = "Bash",
            [HooksProperty] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command
                }
            }
        };

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", hookEntry, command, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(command);
        context.Created.Should().ContainSingle();
    }

    private const string HooksProperty = "hooks";

    private static async Task<List<string>> ReadInnerCommandsAsync(string path, string hookEventKey)
    {
        var content = await File.ReadAllTextAsync(path);
        var root = (JsonObject)JsonNode.Parse(content)!;
        return [.. root[HooksProperty]![hookEventKey]!.AsArray()
            .SelectMany(entry => entry![HooksProperty]!.AsArray())
            .Select(inner => inner!["command"]!.GetValue<string>())];
    }

    // --- ShouldSkipWrite ---
    // Single source of truth consumed by WriteFileAsync, WriteSectionBasedFileAsync, and
    // AiderIntegrator.PrepareConfSectionAsync so their skip decisions can never drift apart.

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldSkipWrite_ReturnsExpected(bool fileExists, bool force, bool expected)
    {
        IntegratorHelpers.ShouldSkipWrite(fileExists, force).Should().Be(expected);
    }

    // --- WriteGeneratedFileAsync ---

    private static GeneratedArtifact Artifact(string path, string body = "print('v2')\n")
        => new(path, body, StampStyle.HashComment, "_DTK_SUBCOMMANDS");

    [Fact]
    public async Task WriteGeneratedFileAsync_FileMissing_CreatesItStamped()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Created.Should().ContainSingle().Which.Should().Be(path);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(path)).Should().BeTrue();
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_AlreadyCurrent_ReportsUnchangedNotSkipped()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var artifact = Artifact(path);
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(artifact.Body, artifact.Style));
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(artifact, context, default);

        context.Unchanged.Should().ContainSingle().Which.Should().Be(path);
        context.Skipped.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_StampedOlderGeneration_RefreshesWithoutForce()
    {
        // The whole point of the feature: an untouched artifact from an older dtk is dtk's own
        // output, so overwriting it destroys nothing.
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment));
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle().Which.Should().Be(path);
        (await File.ReadAllTextAsync(path)).Should().Contain("print('v2')");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_UnstampedButRecognized_RefreshesAndNotes()
    {
        // Nothing installed by dtk 0.6.0 or earlier carries a stamp; without this branch the fix
        // would not fire for a single existing user in the release that ships it.
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "_DTK_SUBCOMMANDS = (\"build\",)\n");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle().Which.Should().Be(path);
        context.Notes.Should().ContainSingle().Which.Should().Contain("older dtk");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_StampedButEdited_SkipsWithoutForce()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        var stamped = ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment);
        await File.WriteAllTextAsync(path, stamped.Replace("v1", "mine", StringComparison.Ordinal));
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Skipped.Should().ContainSingle().Which.Should().Be(path);
        (await File.ReadAllTextAsync(path)).Should().Contain("mine");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_TextAppendedAfterStamp_SkipsWithoutForceAndLeavesBytesIntact()
    {
        // The stamp's digest only ever covers the body above it, so a trailing hand-edit — e.g.
        // "# my own change" tacked on below an otherwise-genuine stamp — must not read as either
        // authentic (its own digest is untouched) or legacy (the file still contains
        // _DTK_SUBCOMMANDS). Getting either wrong silently overwrites the edit.
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        var stamped = ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment);
        var appended = stamped + "# my own change\n";
        await File.WriteAllTextAsync(path, appended);
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Skipped.Should().ContainSingle().Which.Should().Be(path);
        context.Updated.Should().BeEmpty();
        (await File.ReadAllTextAsync(path)).Should().Be(appended, "a skip must leave the file byte-for-byte untouched");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_TextAppendedAfterStampWithForce_Overwrites()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        var stamped = ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment);
        await File.WriteAllTextAsync(path, stamped + "# my own change\n");
        var context = new IntegrationContext(force: true);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle().Which.Should().Be(path);
        (await File.ReadAllTextAsync(path)).Should().Contain("print('v2')");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_UnrecognizedForeignFile_SkipsWithoutForce()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "# someone else's script\n");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Skipped.Should().ContainSingle();
        (await File.ReadAllTextAsync(path)).Should().Contain("someone else");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_StampedButEditedWithForce_Overwrites()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        var stamped = ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment);
        await File.WriteAllTextAsync(path, stamped.Replace("v1", "mine", StringComparison.Ordinal));
        var context = new IntegrationContext(force: true);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle();
        (await File.ReadAllTextAsync(path)).Should().Contain("print('v2')");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_AlreadyCurrentWithForce_StillReportsUnchanged()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var artifact = Artifact(path);
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(artifact.Body, artifact.Style));
        var context = new IntegrationContext(force: true);

        await IntegratorHelpers.WriteGeneratedFileAsync(artifact, context, default);

        context.Unchanged.Should().ContainSingle();
        context.Updated.Should().BeEmpty();
    }
}
