using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class HookDescriptionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-hookdesc-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task DescribeHooks_Project_NamesTheRegistrationInitWrote()
    {
        // A diagnostic that keeps its own copy of where the hook lives will eventually check a file init no
        // longer writes, and report success doing it.
        var home = new HomePaths(_tempDir);
        var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new GeminiCliIntegrator(home),
            new CopilotCliIntegrator(home),
            new CodexIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new OpenCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home)
        };

        foreach (var integrator in integrators)
        {
            var projectDir = Path.Combine(_tempDir, ((IProviderIntegrator)integrator).ProviderName);
            await ((IProviderIntegrator)integrator).IntegrateAsync(projectDir, force: false, default);

            foreach (var installation in integrator.DescribeHooks(projectDir, HookScope.Project))
            {
                var registration = await File.ReadAllTextAsync(installation.RegistrationPath);
                registration.Should().Contain(installation.PluginArtifact is null ? installation.Command : OpenCodePlugin.InvocationSignature);
                File.Exists(installation.LegacyScriptPath).Should().BeFalse("init no longer writes a script");
            }
        }
    }

    [Fact]
    public void DescribeHooks_Global_UsesTheHomeConfigPaths()
    {
        var home = new HomePaths(_tempDir);

        var installation = new GeminiCliIntegrator(home).DescribeHooks(_tempDir, HookScope.Global).Should().ContainSingle().Subject;

        installation.RegistrationPath.Should().StartWith(home.GeminiDir);
        installation.LegacyScriptPath.Should().StartWith(home.GeminiDir);
        installation.Scope.Should().Be(HookScope.Global);
    }

    [Fact]
    public void DescribeHooks_EveryHookProvider_RegistersItsOwnDtkHook()
    {
        var home = new HomePaths(_tempDir);
        var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
        var expected = new Dictionary<string, string>
        {
            ["claude"] = "dtk hook claude",
            ["gemini"] = "dtk hook gemini; exit 0",
            ["copilot-cli"] = "dtk hook copilot-cli; exit 0",
            ["codex"] = "dtk hook codex",
            ["opencode"] = "dtk hook opencode"
        };
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new GeminiCliIntegrator(home),
            new CopilotCliIntegrator(home),
            new CodexIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new OpenCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home)
        };

        foreach (var installation in integrators.SelectMany(i => i.DescribeHooks(_tempDir, HookScope.Project)))
        {
            installation.Command.Should().Be(expected[installation.ProviderName]);
            if (installation.LegacyScriptPath is not null)
            {
                Path.GetFileName(installation.LegacyScriptPath).Should().Be(IntegratorHelpers.LegacyHookScriptName);
            }
        }
    }
}
