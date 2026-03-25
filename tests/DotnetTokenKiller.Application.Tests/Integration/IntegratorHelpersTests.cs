using DotnetTokenKiller.Application.Integration;
using FluentAssertions;
using System.Text.Json.Nodes;

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
    public async Task WriteSectionBasedFileAsync_ExistingWithoutMarker_AppendsSection()
    {
        var context = new IntegrationContext(false);
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
    public async Task WriteSectionBasedFileAsync_WhitespaceOnlyFile_CreatesSection()
    {
        var context = new IntegrationContext(false);
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
}
