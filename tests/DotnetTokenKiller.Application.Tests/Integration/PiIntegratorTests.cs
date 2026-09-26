using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class PiIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-pi-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string ExtensionPath => Path.Combine(ProjectDir, ".pi", "extensions", "dtk.js");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private HomePaths Home => new(HomeDir, name => _environment.GetValueOrDefault(name));

    private RtkHookCoexistence Rtk() => new(Home.ClaudeDir, RtkConfigPath);

    private PiIntegrator CreateSut() => new(Rtk(), Home);

    [Fact]
    public void ProviderName_IsPi() => CreateSut().ProviderName.Should().Be("pi");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedExtension()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, ExtensionPath);
        var extension = await File.ReadAllTextAsync(ExtensionPath);
        extension.Should().StartWith(PiExtension.Body("pi", "pi"));
        ArtifactStamping.IsAuthentic(extension).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_ProjectScope_NotesPiProjectTrust()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().Contain(PiIntegrator.TrustNote);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderPiAgentDirWithoutTrustNote()
    {
        var agentDir = Path.Combine(_tempDir, "custom-pi");
        _environment["PI_CODING_AGENT_DIR"] = agentDir;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(agentDir, "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(agentDir, "extensions", "dtk.js"));
        result.Notes.Should().NotContain(PiIntegrator.TrustNote);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchanged()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, ExtensionPath);
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_EditedExtension_IsLeftAloneWithoutForceAndReplacedWithIt()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        var edited = (await File.ReadAllTextAsync(ExtensionPath)).Replace("5000", "9000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(ExtensionPath, edited);

        var kept = await CreateSut().IntegrateAsync(ProjectDir, false, default);
        (await File.ReadAllTextAsync(ExtensionPath)).Should().Be(edited);
        kept.SkippedFiles.Should().Equal(ExtensionPath);

        var forced = await CreateSut().IntegrateAsync(ProjectDir, true, default);
        forced.UpdatedFiles.Should().Contain(ExtensionPath);
    }

    [Fact]
    public async Task IntegrateAsync_ExtensionFromAnOlderDtk_IsRefreshedWithoutForce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExtensionPath)!);
        var older = PiExtension.Body("pi", "pi").Replace("5000", "4000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(ExtensionPath, ArtifactStamping.Apply(older, StampStyle.SlashComment));

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UpdatedFiles.Should().Contain(ExtensionPath);
    }

    [Theory]
    [InlineData(".pi")]
    [InlineData(".omp")]
    public async Task IntegrateAsync_RtkExtensionInEitherHarness_ExcludesDotnetInRtkConfig(string folder)
    {
        var rtkExtension = Path.Combine(ProjectDir, folder, "extensions", "rtk.ts");
        Directory.CreateDirectory(Path.GetDirectoryName(rtkExtension)!);
        await File.WriteAllTextAsync(rtkExtension, "// all rewrite logic lives in `rtk rewrite`\nawait pi.exec(\"rtk\", [\"rewrite\", cmd])");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
    }

    [Fact]
    public async Task UninstallAsync_SkillStillUsedByOpenCode_IsKept()
    {
        var useCase = new IntegrateUseCase([CreateSut(), new OpenCodeIntegrator(Rtk(), Home)]);
        await useCase.RunAsync("opencode", ProjectDir, false, default);
        await useCase.RunAsync("pi", ProjectDir, false, default);

        await useCase.UninstallAsync("pi", ProjectDir, false, default);

        File.Exists(ExtensionPath).Should().BeFalse();
        File.Exists(SkillPath).Should().BeTrue("OpenCode's hook is still registered in the project");
    }

    [Fact]
    public void InspectApproval_ProjectScope_ReportsTrustAsANote()
    {
        var sut = CreateSut();
        var project = sut.DescribeHooks(ProjectDir, HookScope.Project)[0];
        var global = sut.DescribeHooks(ProjectDir, HookScope.Global)[0];

        sut.InspectApproval(project, ProjectDir).Should().ContainSingle()
            .Which.Should().Be(new HookApprovalFinding("project trust", true, PiIntegrator.TrustNote));
        sut.InspectApproval(global, ProjectDir).Should().BeEmpty();
    }
}
