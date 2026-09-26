using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Integration;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class OhMyPiIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-ohmypi-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string ExtensionPath => Path.Combine(ProjectDir, ".omp", "extensions", "dtk.js");
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

    private OhMyPiIntegrator CreateSut() => new(Rtk(), Home);

    private PiIntegrator CreatePi() => new(Rtk(), Home);

    private string SharedAgentDir => Path.Combine(_tempDir, "shared-agent");

    private string SharedExtensionPath => Path.Combine(SharedAgentDir, "extensions", "dtk.js");

    /// <summary>Points both harnesses' global agent directory at <see cref="SharedAgentDir"/>, as oh-my-pi does with no profile.</summary>
    private void ShareTheAgentDir() => _environment["PI_CODING_AGENT_DIR"] = SharedAgentDir;

    [Fact]
    public void ProviderName_IsOhMyPi() => CreateSut().ProviderName.Should().Be("oh-my-pi");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedExtension()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, ExtensionPath);
        var extension = await File.ReadAllTextAsync(ExtensionPath);
        extension.Should().StartWith(PiExtension.Body("oh-my-pi", "oh-my-pi"));
        ArtifactStamping.IsAuthentic(extension).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_ProjectScope_HasNoTrustNote()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.Notes.Should().NotContain(PiIntegrator.TrustNote);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderDotOmpAgent()
    {
        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(HomeDir, ".omp", "agent", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(HomeDir, ".omp", "agent", "extensions", "dtk.js"));
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
        var older = PiExtension.Body("oh-my-pi", "oh-my-pi").Replace("5000", "4000", StringComparison.Ordinal);
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
        await useCase.RunAsync("oh-my-pi", ProjectDir, false, default);

        await useCase.UninstallAsync("oh-my-pi", ProjectDir, false, default);

        File.Exists(ExtensionPath).Should().BeFalse();
        File.Exists(SkillPath).Should().BeTrue("OpenCode's hook is still registered in the project");
    }

    [Fact]
    public async Task IntegrateGlobalAsync_AgentDirSharedWithPi_WritesPisExtensionAndSaysSo()
    {
        ShareTheAgentDir();

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(SharedAgentDir, "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            SharedExtensionPath);
        (await File.ReadAllTextAsync(SharedExtensionPath)).Should().StartWith(PiExtension.Body("pi", "pi"));
        result.Notes.Should().Contain(
            $"pi and oh-my-pi share {SharedAgentDir} (PI_CODING_AGENT_DIR); one extension serves both, installed as 'dtk init pi --global'.");
        Directory.Exists(Path.Combine(HomeDir, ".omp")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IntegrateGlobalAsync_AgentDirSharedWithPi_EitherOrderLeavesOneStableExtension(bool piFirst)
    {
        ShareTheAgentDir();
        Task<IntegrationResult> PiAsync() => CreatePi().IntegrateGlobalAsync(false, default);
        Task<IntegrationResult> OmpAsync() => CreateSut().IntegrateGlobalAsync(false, default);

        await (piFirst ? PiAsync() : OmpAsync());
        var second = await (piFirst ? OmpAsync() : PiAsync());

        second.UnchangedFiles.Should().Contain(SharedExtensionPath);
        second.CreatedFiles.Should().BeEmpty();
        second.UpdatedFiles.Should().BeEmpty();
        second.SkippedFiles.Should().BeEmpty();
        Directory.GetFiles(Path.GetDirectoryName(SharedExtensionPath)!).Should().Equal(SharedExtensionPath);
        (await File.ReadAllTextAsync(SharedExtensionPath)).Should().StartWith(PiExtension.Body("pi", "pi"));
    }

    [Fact]
    public async Task UninstallGlobal_AgentDirSharedWithPi_KeepsTheExtensionAndPointsAtPi()
    {
        ShareTheAgentDir();
        var useCase = new IntegrateUseCase([CreatePi(), CreateSut()]);
        await useCase.RunGlobalAsync("oh-my-pi", false, default);
        var before = await File.ReadAllTextAsync(SharedExtensionPath);

        var result = await useCase.UninstallAsync("oh-my-pi", ProjectDir, true, default);

        (await File.ReadAllTextAsync(SharedExtensionPath)).Should().Be(before);
        File.Exists(Path.Combine(SharedAgentDir, "AGENTS.md")).Should().BeTrue();
        result.RemovedFiles.Should().BeEmpty();
        result.Notes.Should().ContainSingle().Which.Should().Contain(SharedAgentDir).And.Contain("dtk init pi --global --uninstall");
    }

    [Fact]
    public async Task Doctor_AgentDirSharedWithPi_ReportsTheExtensionOnceUnderPiWithoutAFailure()
    {
        ShareTheAgentDir();
        await CreateSut().IntegrateGlobalAsync(false, default);
        var checker = new HookHealthChecker(Substitute.For<ICommandRunner>(), () => null);

        var checks = await checker.RunAsync([CreatePi(), CreateSut()], ProjectDir, default);

        checks.Should().ContainSingle(check => check.Name == "pi hook (global)").Which.Passed.Should().BeTrue();
        checks.Where(check => check.Name != "pi hook probe (global)").Should().OnlyContain(check => check.Passed);
        checks.Should().NotContain(check => check.Name.StartsWith("oh-my-pi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UninstallGlobal_Pi_AgentDirSharedWithOhMyPi_RemovesTheSharedFiles()
    {
        ShareTheAgentDir();
        var useCase = new IntegrateUseCase([CreatePi(), CreateSut()]);
        await useCase.RunGlobalAsync("pi", false, default);
        await useCase.RunGlobalAsync("oh-my-pi", false, default);
        var skill = Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

        var result = await useCase.UninstallAsync("pi", ProjectDir, true, default);

        result.RemovedFiles.Should().Contain([SharedExtensionPath, Path.Combine(SharedAgentDir, "AGENTS.md"), skill]);
        result.UnchangedFiles.Should().BeEmpty("oh-my-pi registers nothing of its own in a shared agent dir");
        File.Exists(skill).Should().BeFalse();
    }
}
