using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class AmpIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-amp-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string PluginPath => Path.Combine(ProjectDir, ".amp", "plugins", "dtk.js");
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

    private AmpIntegrator CreateSut() => new(Home);

    [Fact]
    public void ProviderName_IsAmp() => CreateSut().ProviderName.Should().Be("amp");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedPluginWithBothNotes()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        var plugin = await File.ReadAllTextAsync(PluginPath);
        plugin.Should().StartWith(AmpPlugin.Body);
        ArtifactStamping.IsAuthentic(plugin).Should().BeTrue();
        result.Notes.Should().Equal(AmpIntegrator.ReloadNote, AmpIntegrator.ProjectPluginNote);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchangedWithoutNotes()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        result.CreatedFiles.Should().BeEmpty();
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsXdgConfigHomeAndOmitsTheProjectNote()
    {
        var xdg = Path.Combine(_tempDir, "xdg");
        _environment["XDG_CONFIG_HOME"] = xdg;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(xdg, "amp", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(xdg, "amp", "plugins", "dtk.js"));
        result.Notes.Should().Equal(AmpIntegrator.ReloadNote);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_DefaultsUnderDotConfigAmp()
    {
        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(HomeDir, ".config", "amp", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(HomeDir, ".config", "amp", "plugins", "dtk.js"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescribeHooks_ReportsThePluginPathCommandKindAndArtifact(bool global)
    {
        var scope = global ? HookScope.Global : HookScope.Project;
        var expectedPath = global
            ? Path.Combine(HomeDir, ".config", "amp", "plugins", "dtk.js")
            : PluginPath;

        var hook = CreateSut().DescribeHooks(ProjectDir, scope).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(expectedPath);
        hook.Command.Should().Be("dtk hook amp");
        hook.PayloadKind.Should().Be(HookPayloadKind.Amp);
        hook.PluginArtifact.Should().NotBeNull();
        hook.PluginArtifact.Path.Should().Be(expectedPath);
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesThePluginAndPrunesDotAmp()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Contain(PluginPath);
        File.Exists(PluginPath).Should().BeFalse();
        Directory.Exists(Path.Combine(ProjectDir, ".amp")).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallAsync_EditedPlugin_IsKeptWithANote()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        await File.AppendAllTextAsync(PluginPath, "// my edit\n");

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.SkippedFiles.Should().Equal(PluginPath);
        result.Notes.Should().ContainSingle().Which.Should().StartWith($"{PluginPath} was kept");
        File.Exists(PluginPath).Should().BeTrue();
    }
}
