using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CursorHooksFileTests : IDisposable
{
    private const string Command = "dtk hook cursor";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-cursor-hooks-{Guid.NewGuid()}");

    private string HooksPath => Path.Combine(_tempDir, ".cursor", "hooks.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private async Task<JsonObject> ReadAsync() => JsonNode.Parse(await File.ReadAllTextAsync(HooksPath))!.AsObject();

    private async Task SeedAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        await File.WriteAllTextAsync(HooksPath, json);
    }

    private static IntegrationContext Uninstall(string boundary) =>
        IntegrationContext.ForUninstall(boundary, new Dictionary<string, string>());

    [Fact]
    public async Task WriteAsync_NoFile_CreatesVersionOneWithDtksShellEntry()
    {
        var context = new IntegrationContext(force: false);

        await CursorHooksFile.WriteAsync(HooksPath, Command, context, default);

        var root = await ReadAsync();
        root["version"]!.GetValue<int>().Should().Be(1);
        var entry = root["hooks"]!["preToolUse"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        entry["command"]!.GetValue<string>().Should().Be(Command);
        entry["matcher"]!.GetValue<string>().Should().Be("Shell");
        entry["timeout"]!.GetValue<int>().Should().Be(10);
        context.Created.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task WriteAsync_RtkEntryWithoutVersion_KeepsRtkAndAddsVersion()
    {
        await SeedAsync("""{"hooks":{"preToolUse":[{"command":"rtk hook cursor"}],"stop":[{"command":"notify"}]}}""");
        var context = new IntegrationContext(force: false);

        await CursorHooksFile.WriteAsync(HooksPath, Command, context, default);

        var root = await ReadAsync();
        root["version"]!.GetValue<int>().Should().Be(1);
        root["hooks"]!["preToolUse"]!.AsArray().Select(e => e!["command"]!.GetValue<string>())
            .Should().Equal("rtk hook cursor", Command);
        root["hooks"]!["stop"]!.AsArray().Should().ContainSingle();
        context.Updated.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task WriteAsync_SecondRun_ReportsUnchanged()
    {
        await CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);
        var before = await File.ReadAllTextAsync(HooksPath);
        var context = new IntegrationContext(force: true);

        await CursorHooksFile.WriteAsync(HooksPath, Command, context, default);

        context.Unchanged.Should().Equal(HooksPath);
        (await File.ReadAllTextAsync(HooksPath)).Should().Be(before);
    }

    [Fact]
    public async Task WriteAsync_DuplicatedDtkEntries_KeepsTheFirst()
    {
        await SeedAsync("""{"version":1,"hooks":{"preToolUse":[{"command":"dtk hook cursor","timeout":5},{"command":"dtk hook cursor"}]}}""");

        await CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);

        var entry = (await ReadAsync())["hooks"]!["preToolUse"]!.AsArray().Should().ContainSingle().Subject!;
        entry["timeout"]!.GetValue<int>().Should().Be(5, "the surviving entry keeps the user's own settings");
    }

    [Theory]
    [InlineData("""{"hooks":[]}""")]
    [InlineData("""{"hooks":{"preToolUse":{}}}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task WriteAsync_UnexpectedShape_ThrowsAndLeavesTheFileAlone(string json)
    {
        await SeedAsync(json);

        var act = () => CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{HooksPath}*");
        (await File.ReadAllTextAsync(HooksPath)).Should().Be(json);
    }

    [Fact]
    public async Task RemoveAsync_KeepsOtherEntries()
    {
        await SeedAsync("""{"version":1,"hooks":{"preToolUse":[{"command":"rtk hook cursor"},{"command":"dtk hook cursor","matcher":"Shell","timeout":10}]}}""");
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        (await ReadAsync())["hooks"]!["preToolUse"]!.AsArray().Select(e => e!["command"]!.GetValue<string>())
            .Should().Equal("rtk hook cursor");
        context.Updated.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task RemoveAsync_LastEntry_DeletesTheFileAndPrunesTheDirectory()
    {
        await CursorHooksFile.WriteAsync(HooksPath, Command, new IntegrationContext(false), default);
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        File.Exists(HooksPath).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(HooksPath)).Should().BeFalse();
        context.Removed.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task RemoveAsync_NoDtkEntry_ReportsUnchanged()
    {
        await SeedAsync("""{"version":1,"hooks":{"preToolUse":[{"command":"rtk hook cursor"}]}}""");
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        context.Unchanged.Should().Equal(HooksPath);
    }

    [Fact]
    public async Task RemoveAsync_NoFile_DoesNothing()
    {
        var context = Uninstall(_tempDir);

        await CursorHooksFile.RemoveAsync(HooksPath, Command, context, default);

        context.Unchanged.Should().BeEmpty();
        context.Removed.Should().BeEmpty();
    }
}
