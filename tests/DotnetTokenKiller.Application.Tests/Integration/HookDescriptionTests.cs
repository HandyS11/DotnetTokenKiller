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
    public async Task DescribeHooks_Project_PointsAtThePathsIntegrateActuallyWrote()
    {
        // The point of a shared description: a diagnostic that keeps its own copy of where the hook
        // lives will eventually check a path integrate no longer writes, and report success doing it.
        var home = new HomePaths(_tempDir);
        var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new GeminiCliIntegrator(home),
            new CopilotCliIntegrator(home)
        };

        foreach (var integrator in integrators)
        {
            var projectDir = Path.Combine(_tempDir, ((IProviderIntegrator)integrator).ProviderName);
            await ((IProviderIntegrator)integrator).IntegrateAsync(projectDir, force: false, default);

            foreach (var installation in integrator.DescribeHooks(projectDir, HookScope.Project))
            {
                File.Exists(installation.Script.Path).Should().BeTrue(
                    "integrate wrote the script that DescribeHooks names ({0})", installation.Script.Path);
                File.Exists(installation.RegistrationPath).Should().BeTrue(
                    "integrate wrote the registration file that DescribeHooks names ({0})",
                    installation.RegistrationPath);
            }
        }
    }

    [Fact]
    public void DescribeHooks_Global_UsesTheHomeConfigPaths()
    {
        var home = new HomePaths(_tempDir);
        var integrator = new GeminiCliIntegrator(home);

        var installation = integrator.DescribeHooks(_tempDir, HookScope.Global).Should().ContainSingle().Subject;

        installation.Script.Path.Should().StartWith(home.GeminiDir);
        installation.Scope.Should().Be(HookScope.Global);
    }

    [Fact]
    public void DescribeHooks_EveryHookProvider_CarriesTheHookLegacySignature()
    {
        var home = new HomePaths(_tempDir);
        var rtkConfigPath = Path.Combine(_tempDir, "isolated-config", "rtk", "config.toml");
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new GeminiCliIntegrator(home),
            new CopilotCliIntegrator(home)
        };

        foreach (var integrator in integrators)
        {
            foreach (var installation in integrator.DescribeHooks(_tempDir, HookScope.Project))
            {
                installation.Script.LegacySignature.Should().Be(IntegratorHelpers.HookLegacySignature);
                installation.Script.Body.Should().Contain(IntegratorHelpers.HookLegacySignature);
            }
        }
    }
}
