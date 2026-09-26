using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CrushIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-crush-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private HomePaths Home => new(HomeDir, name => _environment.GetValueOrDefault(name));
    private string DotRc => Path.Combine(ProjectDir, ".crushrc");
    private string PlainRc => Path.Combine(ProjectDir, "crushrc");
    private string AgentsPath => Path.Combine(ProjectDir, "AGENTS.md");
    private string SkillPath => Path.Combine(ProjectDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private CrushIntegrator CreateSut() => new(Home);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void ProviderName_IsCrush() => CreateSut().ProviderName.Should().Be("crush");

    [Fact]
    public async Task IntegrateAsync_FreshProject_WritesInstructionsSkillAndDotCrushrc()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, DotRc);
        result.Notes.Should().Equal(CrushIntegrator.VersionNote, CrushIntegrator.SubagentNote);
        (await File.ReadAllTextAsync(DotRc)).Should().Be(CrushrcFile.Section("dtk hook crush"));
    }

    [Fact]
    public async Task IntegrateAsync_ProjectUsesPlainCrushrc_WritesThere()
    {
        Write(PlainRc, "option debug true\n");

        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        File.Exists(DotRc).Should().BeFalse();
        (await File.ReadAllTextAsync(PlainRc)).Should().Contain("--command 'dtk hook crush'");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Contain(DotRc);
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsCrushGlobalConfig()
    {
        var global = Path.Combine(_tempDir, "crush-global");
        _environment["CRUSH_GLOBAL_CONFIG"] = global;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Contain(
        [
            Path.Combine(global, "CRUSH.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(global, "crushrc")
        ]);
        (await File.ReadAllTextAsync(Path.Combine(global, "CRUSH.md"))).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    internal void DescribeHooks_IsAScriptRegistrationAtTheScopesCrushrc(bool global)
    {
        var scope = global ? HookScope.Global : HookScope.Project;

        var hook = CreateSut().DescribeHooks(ProjectDir, scope).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(global ? Path.Combine(HomeDir, ".config", "crush", "crushrc") : DotRc);
        hook.IsScriptRegistration.Should().BeTrue();
        hook.Command.Should().Be("dtk hook crush");
        hook.PayloadKind.Should().Be(HookPayloadKind.Crush);
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesEverythingAndKeepsUserConfig()
    {
        Write(PlainRc, "option debug true\n");
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Contain([AgentsPath, SkillPath]);
        (await File.ReadAllTextAsync(PlainRc)).Should().Be("option debug true\n");
    }

    [Fact]
    public async Task IntegrateAsync_DamagedSection_ThrowsNamingTheFile()
    {
        Write(DotRc, "# >>> dtk (DotnetTokenKiller) >>>\n");

        var act = () => CreateSut().IntegrateAsync(ProjectDir, false, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{DotRc}*");
    }
}
