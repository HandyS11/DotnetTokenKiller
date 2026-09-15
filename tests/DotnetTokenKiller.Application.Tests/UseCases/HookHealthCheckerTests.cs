using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Tests.Integration;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class HookHealthCheckerTests : IDisposable
{
    /// <summary>The arguments the probe must pass to <c>dtk</c> for the Gemini hook.</summary>
    private static readonly string[] GeminiHookArguments = ["hook", "gemini"];

    /// <summary>The arguments the probe must pass to <c>dtk</c> for the Copilot CLI hook.</summary>
    private static readonly string[] CopilotCliHookArguments = ["hook", "copilot-cli"];

    /// <summary>The arguments the probe must pass to <c>dtk</c> for the OpenCode hook.</summary>
    private static readonly string[] OpenCodeHookArguments = ["hook", "opencode"];

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-hookhealth-{Guid.NewGuid()}");
    private readonly HookHealthChecker _sut;

    /// <summary>What resolving <c>dtk</c> from <c>PATH</c> returns; <see langword="null"/> when it is not on <c>PATH</c>.</summary>
    private string? _dtkOnPath = Path.Combine(Path.GetTempPath(), "tools", "dtk");

    public HookHealthCheckerTests()
    {
        _sut = new HookHealthChecker(_runner, () => _dtkOnPath);
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

    private CodexIntegrator Codex => new(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);

    private OpenCodeIntegrator OpenCode => new(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);

    private string CodexConfigPath => Path.Combine(Home.CodexDir, "config.toml");

    private string CodexGlobalHooksPath => Codex.DescribeHooks(_tempDir, HookScope.Global)[0].RegistrationPath;

    /// <summary>Writes a global <c>hooks.json</c> whose first group runs rtk's hook, putting dtk's at 1:0.</summary>
    private async Task WriteCodexGlobalHooksAfterAForeignGroupAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexGlobalHooksPath)!);
        await File.WriteAllTextAsync(CodexGlobalHooksPath, """
            {"hooks":{"PreToolUse":[
              {"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook codex"}]},
              {"matcher":"Bash","hooks":[{"type":"command","command":"dtk hook codex","timeout":10}]}
            ]}}
            """);
    }

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
    public async Task RunAsync_Probe_RunsTheDtkResolvedFromPathByItsAbsolutePath()
    {
        // A bare "dtk" handed to Process.Start is looked up beside the running executable before PATH,
        // so an installed dtk would always probe itself; the probe must run the file PATH resolves to.
        await IntegrateAsync();

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(GeminiHookArguments)),
            Arg.Is<string>(payload => payload.Contains("tool_input", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        checks.First(c => c.Name == "gemini hook probe (project)").Message.Should().Contain(_dtkOnPath!);
    }

    [Fact]
    public async Task AddApplication_ResolvesAChecker_ThatNeverStartsABareDtk()
    {
        // The seam constructor takes a Func the container cannot supply, so DI must pick the PATH-resolving one.
        await IntegrateAsync();
        await using var provider = new ServiceCollection()
            .AddApplication()
            .AddSingleton(_runner)
            .BuildServiceProvider();

        var checks = await provider.GetRequiredService<HookHealthChecker>().RunAsync(Integrators, _tempDir, default);

        checks.Should().HaveCount(2);
        await _runner.DidNotReceive().RunCapturedWithInputAsync(
            "dtk", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DtkNotFoundOnPath_ProbeFailsWithoutStartingAProcess()
    {
        await IntegrateAsync();
        _dtkOnPath = null;

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("not found on PATH").And.Contain("~/.dotnet/tools");
        await _runner.DidNotReceiveWithAnyArgs().RunCapturedWithInputAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RunAsync_RegistrationWithCommentsAndTrailingCommas_IsClassifiedNotReportedUnreadable()
    {
        // Claude Code and Gemini CLI both accept comments in their settings files; a read-only
        // diagnostic has no reason to be stricter than the harness that reads them.
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        Directory.CreateDirectory(Path.GetDirectoryName(installation.RegistrationPath)!);
        await File.WriteAllTextAsync(installation.RegistrationPath, """
            {
              // dtk rewrites dotnet commands
              "hooks": {
                "BeforeTool": [
                  { "matcher": "run_shell_command", "hooks": [ { "type": "command", "command": "dtk hook gemini; exit 0" }, ] },
                ],
              },
            }
            """);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("gemini hook (project)", "gemini hook probe (project)");
        checks.Should().OnlyContain(c => c.Passed);
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
        LegacyHookFixtures.WriteStampedScript(installation.LegacyScriptPath!);

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
        LegacyHookFixtures.WriteStampedScript(installation.LegacyScriptPath!);

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
        LegacyHookFixtures.WriteEditedScript(installation.LegacyScriptPath!);

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

    [Theory]
    [InlineData("""{"hooks":{},"hooks":{}}""")]
    [InlineData("""{"hooks":{"BeforeTool":[{"matcher":"run_shell_command","hooks":[{"type":"command","command":"dtk hook gemini; exit 0","command":"dtk hook gemini; exit 0"}]}]}}""")]
    public async Task RunAsync_RegistrationWithDuplicateKeys_FailsWithoutThrowing(string duplicated)
    {
        // JsonNode.Parse accepts a repeated key and throws ArgumentException only when the object is enumerated.
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        Directory.CreateDirectory(Path.GetDirectoryName(installation.RegistrationPath)!);
        await File.WriteAllTextAsync(installation.RegistrationPath, duplicated);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain(installation.RegistrationPath).And.Contain("could not be read");
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
    public async Task RunAsync_DtkTooOldReportingOnStdout_ProbeFailsQuotingThatError()
    {
        // Spectre.Console.Cli, which parses dtk's arguments, prints "Unknown command" to stdout, not stderr.
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(new CommandResult("\nError: Unknown command 'hook'.\n\n       hook gemini\n", string.Empty, 255));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("exited with code 255: Error: Unknown command 'hook'.")
            .And.Contain("dotnet tool update -g DotnetTokenKiller");
    }

    [Fact]
    public async Task RunAsync_CopilotCliInstall_ProbesWithTheCopilotPayloadShape()
    {
        // Copilot CLI's registration also holds non-string values ("version", "timeoutSec"), which the
        // registration search must step over.
        var copilot = new CopilotCliIntegrator(Home);
        await copilot.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([copilot], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("copilot-cli hook (project)", "copilot-cli hook probe (project)");
        checks.Should().OnlyContain(c => c.Passed);
        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(CopilotCliHookArguments)),
            Arg.Is<string>(payload => payload.Contains("\"toolName\":\"bash\"", StringComparison.Ordinal)
                                      && payload.Contains("toolArgs", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AntigravityInstall_IsRegisteredAndProbedWithTheToolCallShape()
    {
        var antigravity = new AntigravityIntegrator(new RtkHookCoexistence(Home.ClaudeDir, Path.Combine(_tempDir, "rtk.toml")), Home);
        await antigravity.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([antigravity], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("antigravity hook (project)", "antigravity hook probe (project)");
        checks.Should().OnlyContain(c => c.Passed);
        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<string>(payload => payload.Contains("\"CommandLine\"", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CurrentOpenCodePlugin_PassesAndProbesWithTheOpenCodePayload()
    {
        await OpenCode.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("opencode hook (project)", "opencode hook probe (project)");
        checks.Should().OnlyContain(c => c.Passed && !c.IsWarning);
        await _runner.Received(1).RunCapturedWithInputAsync(
            _dtkOnPath!,
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(OpenCodeHookArguments)),
            Arg.Is<string>(payload => payload.StartsWith("{\"command\":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PluginFromAnOlderDtk_FailsStaleWithTheInitRemedy()
    {
        var path = OpenCode.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var older = OpenCodePlugin.Body.Replace("5000", "4000", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(older, StampStyle.SlashComment));

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("stale").And.Contain("dtk init opencode");
        await _runner.DidNotReceiveWithAnyArgs().RunCapturedWithInputAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RunAsync_LocallyEditedPluginStillRunningDtk_PassesAsModifiedAndIsProbed()
    {
        await OpenCode.IntegrateAsync(_tempDir, force: false, default);
        var path = OpenCode.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("5000", "9000", StringComparison.Ordinal));

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal("opencode hook (project)", "opencode hook probe (project)");
        checks[0].Message.Should().Be("registered (modified locally)");
    }

    [Fact]
    public async Task RunAsync_ForeignFileAtThePluginPath_IsNotReported()
    {
        var path = OpenCode.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "export const Other = async () => ({});");

        var checks = await _sut.RunAsync([OpenCode], _tempDir, default);

        checks.Should().ContainSingle().Which.Name.Should().Be("hook integration");
    }

    [Fact]
    public async Task RunAsync_ProbeTimesOut_ProbeFailsSayingItDidNotRespond()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(new OperationCanceledException()));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("did not respond within 10s");
    }

    [Fact]
    public async Task RunAsync_CallerCancelsDuringTheProbe_Throws()
    {
        // Only the probe's own timeout is a failed check; the caller's cancellation must still cancel doctor.
        await IntegrateAsync();
        using var cts = new CancellationTokenSource();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(async Task<CommandResult> (_) =>
            {
                await cts.CancelAsync();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => _sut.RunAsync(Integrators, _tempDir, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_ProbeFailsWithNoOutput_SaysSo()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(new CommandResult(" \n", string.Empty, 1));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.First(c => c.Name == "gemini hook probe (project)").Message
            .Should().Contain("exited with code 1: (no output).");
    }

    [Fact]
    public async Task RunAsync_ResolvedDtkCannotBeStarted_ProbeFailsNamingThatPath()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(
                new System.ComponentModel.Win32Exception("No such file or directory")));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain($"could not run {_dtkOnPath}").And.Contain("No such file or directory");
    }

    [Fact]
    public async Task RunAsync_InstallationWithoutALegacyScriptAndNoRegistration_IsNotReported()
    {
        var integrator = new FixedHooks(new HookInstallation(
            "codex", HookScope.Project, Path.Combine(_tempDir, ".codex", "hooks.json"), "dtk hook codex", null, HookPayloadKind.ClaudeCode));

        var checks = await _sut.RunAsync([integrator], _tempDir, default);

        checks.Should().ContainSingle().Which.Name.Should().Be("hook integration");
    }

    [Fact]
    public async Task RunAsync_CodexHookNotApprovedInAnUntrustedProject_WarnsTwiceWithoutFailing()
    {
        await Codex.IntegrateAsync(_tempDir, force: false, default);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal(
            "codex hook (project)", "codex hook probe (project)", "codex hook approval (project)", "codex project trust (project)");
        checks.Should().OnlyContain(c => c.Passed);
        checks.Skip(2).Should().OnlyContain(c => c.IsWarning);
        checks[2].Message.Should().Contain("/hooks");
    }

    [Fact]
    public async Task RunAsync_CodexHookApprovedInATrustedProject_HasNoWarnings()
    {
        await Codex.IntegrateAsync(_tempDir, force: false, default);
        var hooksPath = Codex.DescribeHooks(_tempDir, HookScope.Project)[0].RegistrationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(CodexConfigPath)!);
        await File.WriteAllTextAsync(CodexConfigPath, $"""
            [projects.'{_tempDir}']
            trust_level = "trusted"

            [hooks.state.'{hooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Should().HaveCount(4).And.OnlyContain(c => c.Passed && !c.IsWarning);
    }

    [Fact]
    public async Task RunAsync_CodexHookTurnedOffUnderHooks_WarnsThatItIsTurnedOff()
    {
        await Codex.IntegrateGlobalAsync(force: false, default);
        var hooksPath = Codex.DescribeHooks(_tempDir, HookScope.Global)[0].RegistrationPath;
        await File.WriteAllTextAsync(CodexConfigPath, $"""
            [hooks.state.'{hooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            enabled = false
            """);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        var approval = checks.Single(c => c.Name == "codex hook approval (global)");
        approval.IsWarning.Should().BeTrue();
        approval.Message.Should().Contain("turned off").And.Contain("/hooks");
    }

    [Fact]
    public async Task RunAsync_CodexApprovalOnlyForAForeignHandlerBeforeDtks_WarnsNotYetApproved()
    {
        // Codex keys each handler by its group and handler position, so approving rtk's hook at 0:0 says
        // nothing about dtk's at 1:0.
        await WriteCodexGlobalHooksAfterAForeignGroupAsync();
        await File.WriteAllTextAsync(CodexConfigPath, $"""
            [hooks.state.'{CodexGlobalHooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            """);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        var approval = checks.Single(c => c.Name == "codex hook approval (global)");
        approval.IsWarning.Should().BeTrue();
        approval.Message.Should().Contain("not yet approved");
    }

    [Fact]
    public async Task RunAsync_CodexApprovalForDtksOwnHandlerPosition_Passes()
    {
        await WriteCodexGlobalHooksAfterAForeignGroupAsync();
        await File.WriteAllTextAsync(CodexConfigPath, $"""
            [hooks.state.'{CodexGlobalHooksPath}:pre_tool_use:1:0']
            trusted_hash = "sha256:abc"
            """);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        var approval = checks.Single(c => c.Name == "codex hook approval (global)");
        approval.IsWarning.Should().BeFalse();
        approval.Message.Should().Contain("approval recorded");
    }

    [Fact]
    public async Task RunAsync_CodexForeignHandlerTurnedOffBeforeDtks_DoesNotReportDtksAsTurnedOff()
    {
        await WriteCodexGlobalHooksAfterAForeignGroupAsync();
        await File.WriteAllTextAsync(CodexConfigPath, $"""
            [hooks.state.'{CodexGlobalHooksPath}:pre_tool_use:0:0']
            trusted_hash = "sha256:abc"
            enabled = false
            """);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Single(c => c.Name == "codex hook approval (global)").Message.Should().Contain("not yet approved");
    }

    [Fact]
    public async Task RunAsync_CodexGlobalHook_ChecksApprovalButNotProjectTrust()
    {
        await Codex.IntegrateGlobalAsync(force: false, default);

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal(
            "codex hook (global)", "codex hook probe (global)", "codex hook approval (global)");
    }

    [Fact]
    public async Task RunAsync_CodexConfigUnreadable_WarnsThatApprovalIsUnknown()
    {
        await Codex.IntegrateGlobalAsync(force: false, default);
        await File.WriteAllTextAsync(CodexConfigPath, "[hooks\nnot toml");

        var checks = await _sut.RunAsync([Codex], _tempDir, default);

        var approval = checks.Single(c => c.Name == "codex hook approval (global)");
        approval.IsWarning.Should().BeTrue();
        approval.Message.Should().Contain(CodexConfigPath).And.Contain("could not be read");
    }

    [Fact]
    public async Task RunAsync_ApprovalInspectorThrows_WarnsOnceNamingTheProviderInsteadOfFailing()
    {
        var hooksPath = Path.Combine(_tempDir, ".codex", "hooks.json");
        var integrator = new ThrowingApprovalInspector(new HookInstallation(
            "codex", HookScope.Project, hooksPath, "dtk hook codex", null, HookPayloadKind.CodexCli));
        Directory.CreateDirectory(Path.GetDirectoryName(hooksPath)!);
        await File.WriteAllTextAsync(hooksPath,
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"command":"dtk hook codex"}]}]}}""");

        var checks = await _sut.RunAsync([integrator], _tempDir, default);

        checks.Select(c => c.Name).Should().Equal(
            "codex hook (project)", "codex hook probe (project)", "codex hook approval (project)");
        checks[2].IsWarning.Should().BeTrue("doctor reports what it could not inspect rather than crashing");
        checks[2].Message.Should().Contain("codex").And.Contain("inspector exploded");
    }

    /// <summary>
    /// A hook integrator whose approval inspection throws. Hand-written, as <see cref="FixedHooks"/> is.
    /// </summary>
    /// <param name="described">The installation to describe in its own scope.</param>
    private sealed class ThrowingApprovalInspector(HookInstallation described)
        : IHookIntegrator, IHookApprovalInspector
    {
        public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
            scope == described.Scope ? [described] : [];

        public IReadOnlyList<HookApprovalFinding> InspectApproval(
            HookInstallation installation, string projectDirectory) =>
            throw new InvalidOperationException("inspector exploded");
    }

    /// <summary>
    /// An integrator describing fixed installations. A hand-written fake, because NSubstitute cannot proxy the internal
    /// <see cref="IHookIntegrator"/> (the Application assembly grants no internals to DynamicProxyGenAssembly2).
    /// </summary>
    /// <param name="installations">The installations to describe, filtered by scope.</param>
    private sealed class FixedHooks(params HookInstallation[] installations) : IHookIntegrator
    {
        public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
            [.. installations.Where(installation => installation.Scope == scope)];
    }
}
