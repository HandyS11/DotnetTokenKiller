using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CopilotCliIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-copilotcli-test-{Guid.NewGuid()}");
    private readonly string _isolatedHome;
    private readonly CopilotCliIntegrator _sut;

    public CopilotCliIntegratorTests()
    {
        _isolatedHome = Path.Combine(_tempDir, "isolated-home");
        _sut = new CopilotCliIntegrator(new HomePaths(_isolatedHome));
    }

    private string HookScriptPath => Path.Combine(_tempDir, ".github", "hooks", "dotnet-to-dtk.py");
    private string HookJsonPath => Path.Combine(_tempDir, ".github", "hooks", "dtk-dotnet.json");
    private string InstructionsPath => Path.Combine(_tempDir, ".github", "copilot-instructions.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void ProviderName_ReturnsCopilotCli()
    {
        _sut.ProviderName.Should().Be("copilot-cli");
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesAllThreeFiles()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().HaveCount(3);
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();

        File.Exists(HookScriptPath).Should().BeTrue();
        File.Exists(HookJsonPath).Should().BeTrue();
        File.Exists(InstructionsPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_HookJson_HasCopilotPreToolUseShape()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(HookJsonPath)) as JsonObject;

        root.Should().NotBeNull();
        root!["version"]!.GetValue<int>().Should().Be(1);
        var entry = root["hooks"]!["preToolUse"]!.AsArray()[0]!;
        entry["type"]!.GetValue<string>().Should().Be("command");
        entry["matcher"]!.GetValue<string>().Should().Be("bash");
        entry["bash"]!.GetValue<string>().Should().Contain("dotnet-to-dtk.py");
    }

    [Fact]
    public async Task IntegrateAsync_HookScript_ContainsCopilotDecisionSchema()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(HookScriptPath);

        content.Should().Contain("def rewrite");
        content.Should().Contain("permissionDecision");
        content.Should().Contain("modifiedArgs");
        content.Should().NotContain("updatedInput");
    }

    [Fact]
    public async Task IntegrateAsync_Instructions_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("dtk dotnet build");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_NoForce_SkipsAllFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(3);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_WithForce_OverwritesFiles()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().HaveCount(3);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_ExistingInstructionsWithoutMarker_NoForce_SkipsFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        const string original = "# Copilot rules\n\nBe nice.";
        await File.WriteAllTextAsync(InstructionsPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().Contain(InstructionsPath);
        (await File.ReadAllTextAsync(InstructionsPath)).Should().Be(original);
    }
}
