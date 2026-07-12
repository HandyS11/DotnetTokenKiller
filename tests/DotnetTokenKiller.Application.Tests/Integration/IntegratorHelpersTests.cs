using System.Text.Json.Nodes;
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
        content.Should().Contain("PreToolUse");
        content.Should().Contain("python3 hook.py");
        context.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingWithDuplicateHook_SkipsFile()
    {
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

        context.Skipped.Should().ContainSingle();
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
    public async Task MergeJsonSettingsAsync_ExistingNewProjectDirRootedCommand_IsIdempotent()
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
        context.Skipped.Should().ContainSingle();
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
}
