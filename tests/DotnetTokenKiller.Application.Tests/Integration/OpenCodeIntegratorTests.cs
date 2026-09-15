using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class OpenCodeIntegratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-opencode-test-{Guid.NewGuid()}");
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    private string ProjectDir => Path.Combine(_tempDir, "project");
    private string HomeDir => Path.Combine(_tempDir, "home");
    private string RtkConfigPath => Path.Combine(_tempDir, "config", "rtk", "config.toml");
    private string PluginPath => Path.Combine(ProjectDir, ".opencode", "plugins", "dtk.js");
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

    private OpenCodeIntegrator CreateSut() => new(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home);

    [Fact]
    public void ProviderName_IsOpencode() => CreateSut().ProviderName.Should().Be("opencode");

    [Fact]
    public async Task IntegrateAsync_FreshProject_CreatesInstructionsSkillAndStampedPlugin()
    {
        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        var plugin = await File.ReadAllTextAsync(PluginPath);
        plugin.Should().StartWith(OpenCodePlugin.Body);
        ArtifactStamping.IsAuthentic(plugin).Should().BeTrue();
    }

    [Fact]
    public void PluginBody_KeepsTheContractOpenCodeAndWindowsNeed()
    {
        const string body = OpenCodePlugin.Body;

        body.Should().Contain("import { spawn } from \"node:child_process\";");
        body.Should().Contain(OpenCodePlugin.InvocationSignature);
        body.Should().Contain("input?.tool !== \"bash\"").And.Contain("command.includes(\"dotnet\")");
        body.Should().Contain("output.args.command = rewritten").And.NotContain("output.args =");
        body.Should().NotContain("$`").And.NotContain("which").And.NotContain("shell: true");
        body.Split("export ").Should().HaveCount(2, "OpenCode v1 fails a plugin silently when any export is not a function");
    }

    [Fact]
    public async Task IntegrateAsync_SecondRun_ReportsEverythingUnchanged()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath, PluginPath);
        result.CreatedFiles.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrateAsync_EditedPlugin_IsLeftAloneWithoutForceAndReplacedWithIt()
    {
        await CreateSut().IntegrateAsync(ProjectDir, false, default);
        var edited = (await File.ReadAllTextAsync(PluginPath)).Replace("5000", "9000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(PluginPath, edited);

        var kept = await CreateSut().IntegrateAsync(ProjectDir, false, default);
        (await File.ReadAllTextAsync(PluginPath)).Should().Be(edited);
        kept.SkippedFiles.Should().Equal(PluginPath);

        var forced = await CreateSut().IntegrateAsync(ProjectDir, true, default);
        forced.UpdatedFiles.Should().Contain(PluginPath);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(PluginPath)).Should().BeTrue();
    }

    [Fact]
    public async Task IntegrateAsync_UnstampedPluginStillRunningDtk_IsSkippedWithoutForce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PluginPath)!);
        await File.WriteAllTextAsync(PluginPath, OpenCodePlugin.Body);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.SkippedFiles.Should().Contain(PluginPath);
        (await File.ReadAllTextAsync(PluginPath)).Should().Be(OpenCodePlugin.Body);
    }

    [Fact]
    public async Task IntegrateGlobalAsync_WritesUnderXdgConfigHomeAndTheSharedSkillsDirectory()
    {
        var xdg = Path.Combine(_tempDir, "xdg");
        _environment["XDG_CONFIG_HOME"] = xdg;

        var result = await CreateSut().IntegrateGlobalAsync(false, default);

        result.CreatedFiles.Should().Equal(
            Path.Combine(xdg, "opencode", "AGENTS.md"),
            Path.Combine(HomeDir, ".agents", "skills", "dotnet-token-killer", "SKILL.md"),
            Path.Combine(xdg, "opencode", "plugins", "dtk.js"));
    }

    [Fact]
    public async Task IntegrateAsync_AfterCodex_SharesOneSectionAndOneSkill()
    {
        await new CodexIntegrator(new RtkHookCoexistence(Home.ClaudeDir, RtkConfigPath), Home).IntegrateAsync(ProjectDir, false, default);

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.UnchangedFiles.Should().Equal(AgentsPath, SkillPath);
        result.CreatedFiles.Should().Equal(PluginPath);
        (await File.ReadAllTextAsync(AgentsPath)).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Theory]
    [InlineData("plugins", "rtk.ts")]
    [InlineData("plugin", "rtk.ts")]
    public async Task IntegrateAsync_RtkOpenCodePlugin_ExcludesDotnetInRtkConfig(string folder, string file)
    {
        var rtkPlugin = Path.Combine(ProjectDir, ".opencode", folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(rtkPlugin)!);
        await File.WriteAllTextAsync(rtkPlugin, "const result = await $`rtk rewrite ${command}`.quiet().nothrow()");

        var result = await CreateSut().IntegrateAsync(ProjectDir, false, default);

        result.CreatedFiles.Should().Contain(RtkConfigPath);
    }
}
