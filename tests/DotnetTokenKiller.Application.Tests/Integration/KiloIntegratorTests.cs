using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class KiloIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-kilo-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string PluginPath => Path.Combine(ProjectDir, ".kilo", "plugin", "dtk.js");
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

    private KiloIntegrator CreateSut() => new(Home);

    [Fact]
    public void ProviderName_IsKilo() => CreateSut().ProviderName.Should().Be("kilo");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedPluginWithReloadNote()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        var plugin = await File.ReadAllTextAsync(PluginPath);
        plugin.Should().StartWith(KiloPlugin.Body);
        ArtifactStamping.IsAuthentic(plugin).Should().BeTrue();
        result.Notes.Should().Equal(KiloIntegrator.ReloadNote);
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchangedWithoutNote()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        result.CreatedFiles.Should().BeEmpty();
        result.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsKiloConfigDir()
    {
        var kiloConfig = Path.Combine(_tempDir, "kilo-config");
        _environment["KILO_CONFIG_DIR"] = kiloConfig;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(kiloConfig, "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(kiloConfig, "plugin", "dtk.js"));
    }

    [Fact]
    public async Task IntegrateGlobalAsync_HonorsXdgConfigHomeWhenKiloConfigDirUnset()
    {
        var xdg = Path.Combine(_tempDir, "xdg");
        _environment["XDG_CONFIG_HOME"] = xdg;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(xdg, "kilo", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(xdg, "kilo", "plugin", "dtk.js"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescribeHooks_ReportsThePluginPathCommandKindAndArtifact(bool global)
    {
        var scope = global ? HookScope.Global : HookScope.Project;
        var expectedPath = global
            ? Path.Combine(HomeDir, ".config", "kilo", "plugin", "dtk.js")
            : PluginPath;

        var hook = CreateSut().DescribeHooks(ProjectDir, scope).Should().ContainSingle().Subject;

        hook.RegistrationPath.Should().Be(expectedPath);
        hook.Command.Should().Be("dtk hook kilo");
        hook.PayloadKind.Should().Be(HookPayloadKind.Kilo);
        hook.PluginArtifact.Should().NotBeNull();
        hook.PluginArtifact.Path.Should().Be(expectedPath);
    }

    [Fact]
    public async Task UninstallAsync_Project_RemovesThePluginAndPrunesDotKilo()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().UninstallAsync(ProjectDir, HookScope.Project, new Dictionary<string, string>(), default);

        result.RemovedFiles.Should().Contain(PluginPath);
        File.Exists(PluginPath).Should().BeFalse();
        Directory.Exists(Path.Combine(ProjectDir, ".kilo")).Should().BeFalse();
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
