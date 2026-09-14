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

    [Fact]
    public async Task WriteFileAsync_ExistingFileIdenticalContent_NoForce_ReportsUnchangedNotSkipped()
    {
        // A file already byte-identical to what dtk would write is force-independent: there is
        // nothing to write and --force would not change that, so this must never surface as
        // "skipped ... use --force" — that would be false advice.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "file.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "content");

        await IntegratorHelpers.WriteFileAsync(path, "content", context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("content");
        context.Unchanged.Should().ContainSingle().Which.Should().Be(path);
        context.Skipped.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
        context.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFileIdenticalContent_WithForce_StillReportsUnchanged()
    {
        var context = new IntegrationContext(true);
        var path = Path.Combine(_tempDir, "file.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "content");

        await IntegratorHelpers.WriteFileAsync(path, "content", context, CancellationToken.None);

        context.Unchanged.Should().ContainSingle().Which.Should().Be(path);
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFileIdenticalAfterLineEndingNormalization_NoForce_ReportsUnchanged()
    {
        // Comparison normalizes both sides to '\n', matching WriteGeneratedFileAsync, so a file
        // that only differs by CRLF-vs-LF line endings is still recognized as identical.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "file.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "line1\r\nline2\r\n");

        await IntegratorHelpers.WriteFileAsync(path, "line1\nline2\n", context, CancellationToken.None);

        context.Unchanged.Should().ContainSingle().Which.Should().Be(path);
        context.Skipped.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFileUnreadable_NoForce_SkipsInsteadOfThrowing()
    {
        // dtk cannot prove it wrote a file it cannot read, so an unreadable existing file must
        // never be treated as Unchanged, and the read failure (locked, permission denied) must not
        // abort the whole integration run — it falls back to the same skip-or-force decision as a
        // content difference. An exclusive lock held from within this process is used rather than
        // chmod, since chmod-based "unreadable" files are not reliably unreadable when tests run as
        // root.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "file.md");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "content");

        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => IntegratorHelpers.WriteFileAsync(path, "content", context, CancellationToken.None);

            await act.Should().NotThrowAsync();
        }

        context.Skipped.Should().ContainSingle().Which.Should().Be(path);
        context.Unchanged.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
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
    public async Task MergeJsonSettingsAsync_WholePathQuotedVariantRegistered_UpgradesInPlaceToCurrentCommand()
    {
        // A user hand-editing settings.json might quote the whole path instead of just the env-var
        // segment (see #120). That registers the same hook and must be recognized as such — the two
        // commands are equal once every '"' is stripped — so it is upgraded in place rather than
        // appended as a duplicate.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string currentCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        const string wholePathQuotedVariant = "python3 \"$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py\"";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray { HookEntry(wholePathQuotedVariant) }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(currentCommand), currentCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(currentCommand);
        context.Updated.Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_IdenticalCommandAndWholePathQuotedVariantBothRegistered_KeepsOnlyOneEntry()
    {
        // When the current command AND an equivalent variant are both already registered (possible
        // when a hand-edit introduced the variant alongside dtk's own entry), the variant must be
        // dropped and exactly one registration — the identical one — must survive.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string currentCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        const string wholePathQuotedVariant = "python3 \"$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py\"";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    HookEntry(currentCommand),
                    HookEntry(wholePathQuotedVariant)
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(currentCommand), currentCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().ContainSingle().Which.Should().Be(currentCommand);
        context.Updated.Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_VariantRegisteredBeforeIdenticalCommand_KeepsTheIdenticalEntry()
    {
        // Reviewer-caught bug (PR #148): the multi-match branch used to upgrade matches[0] in place
        // and drop the rest, so which registration survived depended on array order. With the variant
        // ahead of the already-identical command, that deleted the identical entry — and the extra
        // "timeout" property living on it — keeping the variant's entry instead. The already-identical
        // registration must survive regardless of position.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string currentCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        const string wholePathQuotedVariant = "python3 \"$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py\"";
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
                                ["command"] = wholePathQuotedVariant,
                                ["timeout"] = 5
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
                                ["command"] = currentCommand,
                                ["timeout"] = 30
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(currentCommand), currentCommand, context, CancellationToken.None);

        var written = await File.ReadAllTextAsync(path);
        var root = (JsonObject)JsonNode.Parse(written)!;
        var outerEntries = root[HooksProperty]!["PreToolUse"]!.AsArray();
        var innerHooks = outerEntries.SelectMany(entry => entry![HooksProperty]!.AsArray()).ToList();

        outerEntries.Should().ContainSingle("the variant's outer entry must be dropped entirely, not just downgraded");
        innerHooks.Should().ContainSingle().Which!["command"]!.GetValue<string>().Should().Be(currentCommand);
        innerHooks[0]!["timeout"]!.GetValue<int>().Should()
            .Be(30, "the already-identical registration survived, carrying its own extra properties with it");
        context.Updated.Should().ContainSingle().Which.Should().Be(path);
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_NewFile_EscapesEmbeddedQuotesAsBackslashQuoteNotUnicodeEscape()
    {
        // dtk's own hook command embeds literal quotes around the env var, so every merge writes at
        // least one embedded quote. The default WriteIndented encoder would rewrite it as a Unicode
        // escape sequence; the fix must keep it as a plain, JSON-required backslash-quote instead.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        const string command = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(command), command, context, CancellationToken.None);

        var raw = await File.ReadAllTextAsync(path);
        raw.Should().Contain(
            "\\\"$CLAUDE_PROJECT_DIR\\\"", "the embedded quotes must be escaped with a backslash, not \\u0022");
        raw.Should().NotContain("\\u0022", "quotes must never be rewritten as \\u0022 escapes");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_ExistingFileWithHtmlSensitiveAndNonAsciiCharacters_KeepsThemVerbatim()
    {
        // UnsafeRelaxedJsonEscaping leaves '< > & \'' and non-ASCII characters unescaped, unlike the
        // default encoder, which treats them as HTML-sensitive. That is safe here: these are local
        // settings files read directly by the Claude/Gemini CLIs, never rendered as HTML.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path,
            """
            {
              "permissions": {
                "allow": [
                  "Bash(npm run test && echo 'ok' > out.txt)"
                ]
              },
              "description": "café"
            }
            """);
        const string command = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(command), command, context, CancellationToken.None);

        var raw = await File.ReadAllTextAsync(path);
        raw.Should().Contain("Bash(npm run test && echo 'ok' > out.txt)");
        raw.Should().Contain("café");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_WrittenFile_EndsWithExactlyOneNewline()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        const string command = "python3 hook.py";

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(command), command, context, CancellationToken.None);

        var raw = await File.ReadAllTextAsync(path);
        raw.Should().EndWith("}\n");
        raw.Should().NotEndWith("\n\n");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_UnrelatedHookInSameEventArray_IsLeftUntouchedWhenVariantUpgraded()
    {
        // PreToolUse is a shared array; another tool's hook (e.g. rtk's coexistence entry) sitting
        // alongside the variant being upgraded must survive untouched.
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        const string currentCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";
        const string wholePathQuotedVariant = "python3 \"$CLAUDE_PROJECT_DIR/.claude/hooks/dotnet-to-dtk.py\"";
        const string unrelatedCommand = "rtk hook claude";
        var initialRoot = new JsonObject
        {
            [HooksProperty] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    HookEntry(unrelatedCommand),
                    HookEntry(wholePathQuotedVariant)
                }
            }
        };
        await File.WriteAllTextAsync(path, initialRoot.ToJsonString());

        await IntegratorHelpers.MergeJsonSettingsAsync(
            path, "PreToolUse", HookEntry(currentCommand), currentCommand, context, CancellationToken.None);

        var innerCommands = await ReadInnerCommandsAsync(path, "PreToolUse");
        innerCommands.Should().BeEquivalentTo(unrelatedCommand, currentCommand);
        context.Updated.Should().ContainSingle();
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
        // authentic (its own digest is untouched) or legacy. The installed body deliberately
        // carries the real _DTK_SUBCOMMANDS legacy signature so this test actually exercises the
        // legacy branch's "no stamp at all" test (ArtifactStamping.HasStamp) rather than passing
        // vacuously because the body happens not to contain the signature: a legacy check keyed on
        // "!TryParse(...) && Contains(signature)" instead would misclassify this exact file as
        // legacy and refresh it anyway, discarding the edit through the other branch.
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        var stamped = ArtifactStamping.Apply("_DTK_SUBCOMMANDS = (\"build\",)\n", StampStyle.HashComment);
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
        // Mirror of the skip case above, same installed content, so the pair pins "without --force
        // it is skipped, with --force it is overwritten" for the identical scenario. --force bypasses
        // the authentic/legacy decision entirely, so unlike the skip test above this one does not
        // discriminate between the correct and buggy legacy check — it exists to confirm --force
        // still works for this specific installed content, not to pin which check produced isLegacy.
        var path = Path.Combine(_tempDir, "hook.py");
        Directory.CreateDirectory(_tempDir);
        var stamped = ArtifactStamping.Apply("_DTK_SUBCOMMANDS = (\"build\",)\n", StampStyle.HashComment);
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

    [Fact]
    public async Task WriteGeneratedFileAsync_ExistingArtifactUnreadable_NoForce_SkipsInsteadOfThrowing()
    {
        // Same hazard as WriteFileAsync: dtk cannot prove authorship of an artifact it cannot read,
        // so it must never be refreshed or reported Unchanged, and the read failure must not crash
        // the whole 'dtk integrate' run. An exclusive lock held from within this process is used
        // rather than chmod, since chmod-based "unreadable" files are not reliably unreadable when
        // tests run as root.
        var path = Path.Combine(_tempDir, "hook.py");
        var artifact = Artifact(path);
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(artifact.Body, artifact.Style));
        var context = new IntegrationContext(force: false);

        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => IntegratorHelpers.WriteGeneratedFileAsync(artifact, context, default);

            await act.Should().NotThrowAsync();
        }

        context.Skipped.Should().ContainSingle().Which.Should().Be(path);
        context.Unchanged.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
    }

    // --- WriteHookRegistrationAsync / MergeJsonSettingsAsync legacy migration ---

    [Theory]
    [InlineData("python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py")]
    [InlineData("python3 .claude/hooks/dotnet-to-dtk.py")]
    [InlineData("python \"C:\\Users\\me\\.claude\\hooks\\dotnet-to-dtk.py\"")]
    [InlineData("\"C:\\Program Files\\Python\\python.exe\" \"$HOME\"/.claude/hooks/dotnet-to-dtk.py")]
    public async Task MergeJsonSettingsAsync_PythonHookRegistration_IsUpgradedInPlaceToTheDtkHook(string legacyCommand)
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, $$$"""
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":{{{System.Text.Json.JsonSerializer.Serialize(legacyCommand)}}},"timeout":30}]}]}}
            """);

        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude"), context, CancellationToken.None);

        (await ReadInnerCommandsAsync(path, "PreToolUse")).Should().ContainSingle().Which.Should().Be("dtk hook claude");
        (await File.ReadAllTextAsync(path)).Should().Contain("\"timeout\": 30", "an upgrade in place keeps the entry's other properties");
        context.Updated.Should().ContainSingle();
        replacedLegacy.Should().BeTrue("the caller may only retire the Python script when its registration was migrated");
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_HookRunningAnotherScript_IsLeftAloneAndTheDtkHookAppended()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"python3 .claude/hooks/rtk-rewrite.py"}]}]}}
            """);

        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude"), context, CancellationToken.None);

        (await ReadInnerCommandsAsync(path, "PreToolUse")).Should().Equal("python3 .claude/hooks/rtk-rewrite.py", "dtk hook claude");
        replacedLegacy.Should().BeFalse();
    }

    [Fact]
    public async Task WriteHookRegistrationAsync_NewFile_WritesOnlyTheRegistration()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "BeforeTool", "run_shell_command", "dtk hook gemini; exit 0"), context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be(
            """
            {
              "hooks": {
                "BeforeTool": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "dtk hook gemini; exit 0"
                      }
                    ]
                  }
                ]
              }
            }

            """);
        context.Created.Should().Equal(path);
        Directory.EnumerateFiles(_tempDir).Should().Equal(path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveLegacyHookScriptAsync_ScriptDtkWrote_IsDeletedWithItsEmptyDirectory(bool stamped)
    {
        var context = new IntegrationContext(false);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        if (stamped)
        {
            LegacyHookFixtures.WriteStampedScript(script);
        }
        else
        {
            LegacyHookFixtures.WriteUnstampedScript(script);
        }

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeFalse("an emptied hooks directory is removed too");
        context.Removed.Should().Equal(script);
        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_DirectoryWithOtherFiles_IsKept()
    {
        var context = new IntegrationContext(false);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "hooks", "dtk-dotnet.json"), "{}");

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_EditedScript_IsKeptWithANote()
    {
        var context = new IntegrationContext(false);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteEditedScript(script);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeTrue();
        context.Removed.Should().BeEmpty();
        context.Notes.Should().ContainSingle().Which.Should().Contain(script).And.Contain("no longer used")
            .And.NotContain("re-run", "a re-run finds the registration already migrated and never deletes the script");
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_EditedScriptWithForce_IsDeleted()
    {
        var context = new IntegrationContext(true);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteEditedScript(script);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeFalse();
        context.Removed.Should().Equal(script);
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_NoScript_DoesNothing()
    {
        var context = new IntegrationContext(false);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py"), context, CancellationToken.None);

        context.Removed.Should().BeEmpty();
        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_CurrentCommandAlreadyRegistered_ReportsNoLegacyReplaced()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        var spec = new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude");
        await IntegratorHelpers.WriteHookRegistrationAsync(spec, new IntegrationContext(false), CancellationToken.None);

        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(spec, context, CancellationToken.None);

        replacedLegacy.Should().BeFalse();
        context.Unchanged.Should().Equal(path);
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_LegacyDuplicateBesideTheCurrentCommand_ReportsTheLegacyReplaced()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"dtk hook claude"},{"type":"command","command":"python3 .claude/hooks/dotnet-to-dtk.py"}]}]}}
            """);

        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude"), context, CancellationToken.None);

        (await ReadInnerCommandsAsync(path, "PreToolUse")).Should().Equal("dtk hook claude");
        replacedLegacy.Should().BeTrue("a removed Python entry is a migrated one too");
    }

    // --- RetireLegacyHookScriptAsync ---

    private string RetireScriptPath => Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");

    private string RetireSettingsPath => Path.Combine(_tempDir, "settings.json");

    private string RetireLocalSettingsPath => Path.Combine(_tempDir, "settings.local.json");

    [Fact]
    public async Task RetireLegacyHookScriptAsync_MigratedAndNothingElseRunsIt_RemovesTheScript()
    {
        var context = new IntegrationContext(false);
        LegacyHookFixtures.WriteStampedScript(RetireScriptPath);
        await File.WriteAllTextAsync(RetireSettingsPath, """{"hooks":{"PreToolUse":[]}}""");

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            RetireScriptPath, replacedLegacyRegistration: true, [RetireSettingsPath, RetireLocalSettingsPath], context, CancellationToken.None);

        File.Exists(RetireScriptPath).Should().BeFalse();
        context.Removed.Should().Equal(RetireScriptPath);
        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task RetireLegacyHookScriptAsync_AnotherSettingsFileStillRunsIt_KeepsTheScriptNamingThatFile()
    {
        var context = new IntegrationContext(false);
        LegacyHookFixtures.WriteStampedScript(RetireScriptPath);
        await File.WriteAllTextAsync(RetireLocalSettingsPath, """
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"python3 .claude/hooks/dotnet-to-dtk.py"}]}]}}
            """);

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            RetireScriptPath, replacedLegacyRegistration: true, [RetireSettingsPath, RetireLocalSettingsPath], context, CancellationToken.None);

        File.Exists(RetireScriptPath).Should().BeTrue("a missing script makes python3 exit 2, which Claude Code treats as blocking");
        context.Removed.Should().BeEmpty();
        context.Notes.Should().ContainSingle().Which.Should().Contain(RetireScriptPath).And.Contain(RetireLocalSettingsPath);
    }

    [Fact]
    public async Task RetireLegacyHookScriptAsync_SettingsFileUnreadable_KeepsTheScriptNamingThatFile()
    {
        // An exclusive lock held from within this process, rather than chmod, which root ignores.
        var context = new IntegrationContext(false);
        LegacyHookFixtures.WriteStampedScript(RetireScriptPath);
        await File.WriteAllTextAsync(RetireLocalSettingsPath, "{}");

        await using (new FileStream(RetireLocalSettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await IntegratorHelpers.RetireLegacyHookScriptAsync(
                RetireScriptPath, replacedLegacyRegistration: true, [RetireLocalSettingsPath], context, CancellationToken.None);
        }

        File.Exists(RetireScriptPath).Should().BeTrue("dtk cannot prove a file it cannot read no longer runs the script");
        context.Notes.Should().ContainSingle().Which.Should().Contain(RetireLocalSettingsPath);
    }

    [Fact]
    public async Task RetireLegacyHookScriptAsync_NoRegistrationWasMigrated_KeepsTheScriptWithANote()
    {
        var context = new IntegrationContext(true);
        LegacyHookFixtures.WriteStampedScript(RetireScriptPath);

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            RetireScriptPath, replacedLegacyRegistration: false, [RetireSettingsPath, RetireLocalSettingsPath], context, CancellationToken.None);

        File.Exists(RetireScriptPath).Should().BeTrue("even --force cannot tell what else still runs a script no registration dtk updated ran");
        context.Removed.Should().BeEmpty();
        context.Notes.Should().ContainSingle().Which.Should().Contain(RetireScriptPath).And.Contain("left in place");
    }

    [Fact]
    public async Task RetireLegacyHookScriptAsync_NoScript_DoesNothing()
    {
        var context = new IntegrationContext(false);

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            RetireScriptPath, replacedLegacyRegistration: false, [RetireSettingsPath], context, CancellationToken.None);

        context.Removed.Should().BeEmpty();
        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_ScriptCannotBeDeleted_KeepsItWithANoteInsteadOfThrowing()
    {
        // Unlinking needs write permission on the directory. POSIX modes are a no-op on Windows, and root
        // ignores them, so both are skipped rather than asserting something the mode did not cause.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var context = new IntegrationContext(false);
        var script = RetireScriptPath;
        LegacyHookFixtures.WriteStampedScript(script);
        var hooksDirectory = Path.GetDirectoryName(script)!;
        var originalMode = File.GetUnixFileMode(hooksDirectory);
        try
        {
            File.SetUnixFileMode(hooksDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (CanCreateFileIn(hooksDirectory))
            {
                return;
            }

            var act = () => IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

            await act.Should().NotThrowAsync();
            File.Exists(script).Should().BeTrue();
            context.Removed.Should().BeEmpty();
            context.Notes.Should().ContainSingle().Which.Should().Contain(script).And.Contain("could not be deleted");
        }
        finally
        {
            File.SetUnixFileMode(hooksDirectory, originalMode);
        }
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_EmptiedDirectoryCannotBeDeleted_StillReportsTheScriptRemoved()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var context = new IntegrationContext(false);
        var parent = Path.Combine(_tempDir, "provider");
        var script = Path.Combine(parent, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        var originalMode = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (CanCreateFileIn(parent))
            {
                return;
            }

            var act = () => IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

            await act.Should().NotThrowAsync("the script is gone; an empty directory left behind costs nothing");
            File.Exists(script).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(script)).Should().BeTrue();
            context.Removed.Should().Equal(script);
        }
        finally
        {
            File.SetUnixFileMode(parent, originalMode);
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

    [Fact]
    public async Task WriteOwnedFileAsync_ReplaceExisting_OverwritesWithoutForce()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "dtk-dotnet.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "old");

        await IntegratorHelpers.WriteOwnedFileAsync(path, "new", replaceExisting: true, context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("new");
        context.Updated.Should().Equal(path);
    }

    [Fact]
    public async Task WriteOwnedFileAsync_NotReplaceable_SkipsWithoutForce()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "dtk-dotnet.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "old");

        await IntegratorHelpers.WriteOwnedFileAsync(path, "new", replaceExisting: false, context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("old");
        context.Skipped.Should().Equal(path);
    }

    [Fact]
    public void IntegrationContext_ToResult_CarriesRemovedFiles()
    {
        var context = new IntegrationContext(false);
        context.Removed.Add("/p/.claude/hooks/dotnet-to-dtk.py");

        context.ToResult().RemovedFiles.Should().Equal("/p/.claude/hooks/dotnet-to-dtk.py");
    }
}
