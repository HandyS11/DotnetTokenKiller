using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class GitHubCopilotIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-copilot-test-{Guid.NewGuid()}");
    private readonly GitHubCopilotIntegrator _sut = new();

    private string InstructionsPath => Path.Combine(_tempDir, ".github", "copilot-instructions.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_CreatesFile()
    {
        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().ContainSingle();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        File.Exists(InstructionsPath).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_CreatedFile_ContainsDtkSection()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("<!-- /dtk -->");
        content.Should().Contain("DotnetTokenKiller");
    }

    [Fact]
    public async Task IntegrateAsync_FileWithMarker_NoForce_SkipsFile()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Should().Be(InstructionsPath);
    }

    [Fact]
    public async Task IntegrateAsync_FileWithMarker_WithForce_UpdatesFile()
    {
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().ContainSingle().Which.Should().Be(InstructionsPath);
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_FileWithMarker_WithForce_ReplacesOnlyDtkSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        await File.WriteAllTextAsync(InstructionsPath,
            "# My Rules\n\nDo stuff.\n\n<!-- dtk -->\nOLD CONTENT\n<!-- /dtk -->\n\n## Other");

        await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Contain("# My Rules");
        content.Should().Contain("## Other");
        content.Should().NotContain("OLD CONTENT");
        content.Should().Contain("DotnetTokenKiller");
    }

    [Fact]
    public async Task IntegrateAsync_FileWithoutMarker_NoForce_SkipsFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        const string original = "# My Rules\n\nDo stuff.";
        await File.WriteAllTextAsync(InstructionsPath, original);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        result.SkippedFiles.Should().ContainSingle().Which.Should().Be(InstructionsPath);
        var content = await File.ReadAllTextAsync(InstructionsPath);
        content.Should().Be(original);
    }

    [Fact]
    public async Task IntegrateAsync_FileWithoutMarker_WithForce_AppendsDtkSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        await File.WriteAllTextAsync(InstructionsPath, "# My Rules\n\nDo stuff.");

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().ContainSingle().Which.Should().Be(InstructionsPath);
        result.CreatedFiles.Should().BeEmpty();

        var content = await File.ReadAllTextAsync(InstructionsPath);
        content.Should().Contain("# My Rules");
        content.Should().Contain("DotnetTokenKiller");
        content.Should().Contain("<!-- dtk -->");
    }

    [Fact]
    public async Task IntegrateAsync_FileWithMarkerButNoEndMarker_PreservesTrailingContent()
    {
        // A missing end marker means the dtk-managed span can't be reliably identified — the fix
        // must preserve whatever followed the begin marker rather than deleting it.
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        await File.WriteAllTextAsync(InstructionsPath, "# My Rules\n\n<!-- dtk -->\nOrphaned content");

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().ContainSingle();

        var content = await File.ReadAllTextAsync(InstructionsPath);
        content.Should().Contain("# My Rules");
        content.Should().Contain("<!-- dtk -->");
        content.Should().Contain("Orphaned content");
    }

    [Fact]
    public void ProviderName_ReturnsCopilot()
    {
        _sut.ProviderName.Should().Be("copilot");
    }

    [Fact]
    public async Task IntegrateAsync_FreshDirectory_RendersExactSection()
    {
        // The six providers sharing IntegrationInstructions (Aider, Cursor, Gemini CLI, GitHub
        // Copilot, JetBrains AI, Windsurf) are otherwise guarded only by .Contain(...)
        // assertions, which would miss a whitespace/interpolation regression in
        // IntegrationInstructions.Intro/UsageBody or in how this integrator composes them
        // around its markers. This pins the fully rendered output byte-for-byte for one of them.
        await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        var content = await File.ReadAllTextAsync(InstructionsPath);

        content.Should().Be(
            """
            <!-- dtk -->
            ## DotnetTokenKiller (dtk)

            Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
            `dtk` filters output to actionable signal only, reducing noise by 50-97%.

            ```sh
            dtk dotnet build MyProject.slnx
            dtk dotnet test --filter "Category=Unit"
            dtk dotnet restore
            dtk dotnet clean
            dtk dotnet format
            dtk dotnet format --verify-no-changes
            ```

            - All arguments and flags are forwarded to `dotnet` unchanged.
            - Exit codes are preserved — CI pipelines work correctly.
            - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
            <!-- /dtk -->
            """);
    }

    [Fact]
    public async Task IntegrateAsync_WhitespaceOnlyFileWithoutMarker_WithForce_CreatesCopilotSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstructionsPath)!);
        await File.WriteAllTextAsync(InstructionsPath, "   \n  \n  ");

        var result = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);

        result.UpdatedFiles.Should().ContainSingle().Which.Should().Be(InstructionsPath);
        var content = await File.ReadAllTextAsync(InstructionsPath);
        content.Should().Contain("<!-- dtk -->");
        content.Should().NotStartWith(Environment.NewLine);
    }
}
