using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

/// <summary>
/// <c>--uninstall</c> recognizes the whole-file bodies released dtk versions wrote for Cursor, Windsurf and Aider, and
/// the hash lists in <see cref="IntegrationInstructions"/> stay complete when that text changes.
/// </summary>
public sealed class ReleasedOwnedFileHashesTests : IDisposable
{
    /// <summary>The Windsurf rule and Aider instructions file dtk 0.7.0 through 0.8.0 wrote, byte for byte.</summary>
    private const string V080MarkdownRule =
        """
        # DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ## Usage

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        dtk dotnet format
        dtk dotnet format --verify-no-changes
        dtk dotnet list package --outdated
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        """;

    /// <summary>The Cursor rule dtk 0.7.0 through 0.8.0 wrote, byte for byte.</summary>
    private const string V080CursorRule =
        $"""
        ---
        description: Use dtk instead of dotnet for build, test, restore, clean, format, and list package commands
        globs:
          - "**/*.cs"
          - "**/*.csproj"
          - "**/*.slnx"
          - "**/*.sln"
        alwaysApply: false
        ---

        {V080MarkdownRule}
        """;

    /// <summary>
    /// The hashes of the bodies this dtk writes. When a test pinning one fails, the text changed: if the old body has
    /// shipped in a release, append its hash (the pinned value) to the matching list in
    /// <see cref="IntegrationInstructions"/>, then pin the new one here.
    /// </summary>
    private const string CurrentCursorRuleHash = "90fe9cc216b6d130483a2e6cb1cdd0c2f56e3e903aa9e4db8a7d90fb2ac50c2f";

    /// <inheritdoc cref="CurrentCursorRuleHash"/>
    private const string CurrentMarkdownRuleHash = "259680360aae93a3a7aecfd6b079e523175ab967886cf995d6f4446c4ae9be0d";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-released-hashes-{Guid.NewGuid()}");

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private HomePaths Home => new(Path.Combine(_tempDir, "home"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void ReleasedHashes_IncludeTheV080Bodies()
    {
        IntegrationInstructions.ReleasedCursorRuleHashes.Should().Contain(UninstallHelpers.HashOwnedFile(V080CursorRule));
        IntegrationInstructions.ReleasedMarkdownRuleHashes.Should().Contain(UninstallHelpers.HashOwnedFile(V080MarkdownRule));
    }

    [Fact]
    public async Task CurrentCursorRule_MatchesThePinnedHash()
    {
        await new CursorIntegrator().IntegrateAsync(ProjectDir, false, default);

        var hash = UninstallHelpers.HashOwnedFile(await ReadAsync(Path.Combine(ProjectDir, ".cursor", "rules", "dtk.mdc")));

        hash.Should().Be(CurrentCursorRuleHash,
            "a changed Cursor rule must append the previous body's hash to ReleasedCursorRuleHashes once it has shipped");
    }

    [Fact]
    public async Task CurrentMarkdownRules_MatchThePinnedHash()
    {
        await new WindsurfIntegrator().IntegrateAsync(ProjectDir, false, default);
        await new AiderIntegrator(Home).IntegrateAsync(ProjectDir, false, default);

        UninstallHelpers.HashOwnedFile(await ReadAsync(Path.Combine(ProjectDir, ".windsurf", "rules", "dtk.md")))
            .Should().Be(CurrentMarkdownRuleHash,
                "a changed Windsurf rule must append the previous body's hash to ReleasedMarkdownRuleHashes once it has shipped");
        UninstallHelpers.HashOwnedFile(await ReadAsync(Path.Combine(ProjectDir, ".aider-dtk-instructions.md")))
            .Should().Be(CurrentMarkdownRuleHash,
                "a changed Aider instructions file must append the previous body's hash to ReleasedMarkdownRuleHashes once it has shipped");
    }

    [Fact]
    public async Task Uninstall_CursorRuleWrittenByV080_IsRemoved()
    {
        var path = await WriteAsync(Path.Combine(ProjectDir, ".cursor", "rules", "dtk.mdc"), V080CursorRule);

        var result = await ((IUninstallIntegrator)new CursorIntegrator()).UninstallAsync(
            ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Equal(path);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Uninstall_WindsurfRuleWrittenByV080_IsRemoved()
    {
        var path = await WriteAsync(Path.Combine(ProjectDir, ".windsurf", "rules", "dtk.md"), V080MarkdownRule);

        var result = await ((IUninstallIntegrator)new WindsurfIntegrator()).UninstallAsync(
            ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Equal(path);
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(HookScope.Project)]
    [InlineData(HookScope.Global)]
    internal async Task Uninstall_AiderInstructionsWrittenByV080_AreRemoved(HookScope scope)
    {
        var home = Home;
        var path = await WriteAsync(
            scope == HookScope.Global ? home.AiderInstructionsPath : Path.Combine(ProjectDir, ".aider-dtk-instructions.md"),
            V080MarkdownRule.ReplaceLineEndings("\r\n"));

        var result = await new AiderIntegrator(home).UninstallAsync(ProjectDir, scope, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Equal(path);
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(HookScope.Project, "dtk init aider")]
    [InlineData(HookScope.Global, "dtk init aider --global")]
    internal async Task Uninstall_EditedAiderInstructions_KeptWithANoteOnRemovingThem(HookScope scope, string command)
    {
        var home = Home;
        var path = await WriteAsync(
            scope == HookScope.Global ? home.AiderInstructionsPath : Path.Combine(ProjectDir, ".aider-dtk-instructions.md"),
            V080MarkdownRule + "\n- My own note.\n");

        var result = await new AiderIntegrator(home).UninstallAsync(ProjectDir, scope, new Dictionary<string, string>(), default);

        result.SkippedFiles.Should().Equal(path);
        result.Notes.Should().ContainSingle().Which.Should()
            .StartWith($"{path} was kept")
            .And.EndWith($"run `{command} --force` to restore dtk's version, then `{command} --uninstall`.");
        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static async Task<string> ReadAsync(string path) =>
        (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
}
