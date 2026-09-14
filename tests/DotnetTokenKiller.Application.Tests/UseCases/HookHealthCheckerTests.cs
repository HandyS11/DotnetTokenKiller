using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Tests.Integration;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class HookHealthCheckerTests : IDisposable
{
    /// <summary>The arguments the probe must pass to <c>dtk</c> for the Gemini hook.</summary>
    private static readonly string[] GeminiHookArguments = ["hook", "gemini"];

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-hookhealth-{Guid.NewGuid()}");
    private readonly HookHealthChecker _sut;

    public HookHealthCheckerTests()
    {
        _sut = new HookHealthChecker(_runner);
        Directory.CreateDirectory(_tempDir);

        // Default: the probe succeeds, so status-check tests are not perturbed by it.
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(new CommandResult(RewrittenPayload(), string.Empty, 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static string RewrittenPayload()
        => string.Concat(DotnetTokenKiller.Domain.DotnetSubcommands.Ordered.Select(s => $"dtk dotnet {s} "));

    private HomePaths Home => new(Path.Combine(_tempDir, "home"));

    private IReadOnlyList<IHookIntegrator> Integrators => [new GeminiCliIntegrator(Home)];

    private async Task IntegrateAsync(HookScope scope = HookScope.Project)
    {
        var integrator = new GeminiCliIntegrator(Home);
        if (scope == HookScope.Global)
        {
            await integrator.IntegrateGlobalAsync(force: false, default);
        }
        else
        {
            await integrator.IntegrateAsync(_tempDir, force: false, default);
        }
    }

    /// <summary>Lowercased scope label matching the <c>(project)</c>/<c>(global)</c> suffix in check names.</summary>
    /// <param name="scope">The scope to render.</param>
    private static string ScopeLabel(HookScope scope) => scope.ToString().ToLowerInvariant();

    [Fact]
    public async Task RunAsync_NoHooksAnywhere_ReportsOnePassingInformationalCheck()
    {
        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeTrue("dtk works without hooks, so their absence is not a failure");
        checks[0].Message.Should().Contain("dtk init");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_HealthyInstall_StatusAndProbeBothPass(bool isGlobal)
    {
        var scope = isGlobal ? HookScope.Global : HookScope.Project;
        await IntegrateAsync(scope);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().HaveCount(2);
        checks.Should().OnlyContain(c => c.Passed);
        checks.Select(c => c.Name).Should().Equal($"gemini hook ({ScopeLabel(scope)})", $"gemini hook probe ({ScopeLabel(scope)})");
    }

    [Fact]
    public async Task RunAsync_Probe_RunsTheDtkOnPathAsTheHarnessWould()
    {
        await IntegrateAsync();

        await _sut.RunAsync(Integrators, _tempDir, default);

        await _runner.Received(1).RunCapturedWithInputAsync(
            "dtk",
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(GeminiHookArguments)),
            Arg.Is<string>(payload => payload.Contains("tool_input", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_PythonEraRegistration_FailsWithTheMigrateRemedyAndIsNotProbed(bool isGlobal)
    {
        var scope = isGlobal ? HookScope.Global : HookScope.Project;
        var installation = Integrators[0].DescribeHooks(_tempDir, scope)[0];
        Directory.CreateDirectory(Path.GetDirectoryName(installation.RegistrationPath)!);
        await File.WriteAllTextAsync(installation.RegistrationPath, """
            {"hooks":{"BeforeTool":[{"matcher":"run_shell_command","hooks":[{"type":"command","command":"python3 \"$GEMINI_PROJECT_DIR\"/.gemini/hooks/dotnet-to-dtk.py"}]}]}}
            """);
        LegacyHookFixtures.WriteStampedScript(installation.LegacyScriptPath);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle("one root cause must produce one failure, not two");
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("legacy Python hook").And.Contain("dtk init gemini");
        if (isGlobal)
        {
            checks[0].Message.Should().Contain("--global");
        }
        else
        {
            checks[0].Message.Should().NotContain("--global");
        }

        await _runner.DidNotReceiveWithAnyArgs().RunCapturedWithInputAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LegacyScriptLeftButNothingRegistered_FailsNotRegistered(bool isGlobal)
    {
        var scope = isGlobal ? HookScope.Global : HookScope.Project;
        var installation = Integrators[0].DescribeHooks(_tempDir, scope)[0];
        LegacyHookFixtures.WriteStampedScript(installation.LegacyScriptPath);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("not registered").And.Contain("dtk init gemini");
    }

    [Fact]
    public async Task RunAsync_RegistrationWithoutTheDtkHook_FailsNotRegistered()
    {
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        await File.WriteAllTextAsync(installation.RegistrationPath, "{}");
        LegacyHookFixtures.WriteEditedScript(installation.LegacyScriptPath);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Message.Should().Contain("not registered").And.Contain(installation.Command);
    }

    [Fact]
    public async Task RunAsync_MalformedRegistrationJson_FailsWithoutThrowing()
    {
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        await File.WriteAllTextAsync(installation.RegistrationPath, "{ not json");

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        // A diagnostic that crashes on a broken config is useless exactly when it is needed.
        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("could not be read as JSON");
    }

    [Fact]
    public async Task RunAsync_RegistrationUnreadable_StatusFailsNamingPathAndReason()
    {
        // An exclusive lock held from within this process, rather than chmod, which root ignores.
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];

        await using (new FileStream(installation.RegistrationPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var checks = await _sut.RunAsync(Integrators, _tempDir, default);

            checks.Should().ContainSingle();
            checks[0].Passed.Should().BeFalse();
            checks[0].Message.Should().Contain(installation.RegistrationPath).And.Contain("could not be read");
        }
    }

    [Fact]
    public async Task RunAsync_UnrelatedSettingsFile_IsNotReported()
    {
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        Directory.CreateDirectory(Path.GetDirectoryName(installation.RegistrationPath)!);
        await File.WriteAllTextAsync(installation.RegistrationPath, """{"theme":"dark"}""");

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle().Which.Passed.Should().BeTrue("a settings file without any dtk hook is not a dtk install");
    }

    [Fact]
    public async Task RunAsync_HookDoesNotRewrite_ProbeFailsNamingTheSubcommandAndTheUpdate()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(new CommandResult("dtk dotnet build", string.Empty, 0));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("list package").And.Contain("dotnet tool update -g DotnetTokenKiller");
    }

    [Fact]
    public async Task RunAsync_DtkTooOldForHook_ProbeFailsSuggestingTheUpdate()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(new CommandResult(string.Empty, "Error: Unknown command 'hook'.", 255));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("exited with code 255").And.Contain("Unknown command 'hook'")
            .And.Contain("dotnet tool update -g DotnetTokenKiller");
    }

    [Fact]
    public async Task RunAsync_DtkNotOnPath_ProbeFailsNamingPath()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(
                new System.ComponentModel.Win32Exception("No such file or directory")));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("could not run dtk").And.Contain("PATH");
    }
}
