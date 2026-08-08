using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class HookHealthCheckerTests : IDisposable
{
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

    private async Task IntegrateAsync()
        => await new GeminiCliIntegrator(Home).IntegrateAsync(_tempDir, force: false, default);

    [Fact]
    public async Task RunAsync_NoHooksAnywhere_ReportsOnePassingInformationalCheck()
    {
        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle();
        checks[0].Passed.Should().BeTrue("dtk works without hooks, so their absence is not a failure");
        checks[0].Message.Should().Contain("dtk integrate");
    }

    [Fact]
    public async Task RunAsync_HealthyInstall_StatusAndProbeBothPass()
    {
        await IntegrateAsync();

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().HaveCount(2);
        checks.Should().OnlyContain(c => c.Passed);
        checks.Select(c => c.Name).Should().Contain("gemini hook (project)", "gemini hook probe (project)");
    }

    [Fact]
    public async Task RunAsync_ScriptStale_StatusFailsAndNamesTheRemedy()
    {
        await IntegrateAsync();
        var scriptPath = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0].Script.Path;
        await File.WriteAllTextAsync(
            scriptPath,
            ArtifactStamping.Apply("_DTK_SUBCOMMANDS = (\"build\",)\n", StampStyle.HashComment));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var status = checks.First(c => c.Name == "gemini hook (project)");
        status.Passed.Should().BeFalse();
        status.Message.Should().Contain("stale").And.Contain("dtk integrate gemini");
        status.Message.Should().NotContain("--force", "a stale-but-unmodified hook refreshes without it");
    }

    [Fact]
    public async Task RunAsync_ScriptEditedLocally_StatusFailsAndAsksForForce()
    {
        await IntegrateAsync();
        var scriptPath = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0].Script.Path;
        await File.AppendAllTextAsync(scriptPath, "# my own change\n");

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var status = checks.First(c => c.Name == "gemini hook (project)");
        status.Passed.Should().BeFalse();
        status.Message.Should().Contain("modified").And.Contain("--force");
    }

    [Fact]
    public async Task RunAsync_ScriptPresentButNotRegistered_StatusFailsAndProbeIsNotRun()
    {
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        await File.WriteAllTextAsync(installation.RegistrationPath, "{}");

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle("one root cause must produce one failure, not two");
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task RunAsync_MalformedRegistrationJson_FailsWithoutThrowing()
    {
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        await File.WriteAllTextAsync(installation.RegistrationPath, "{ not json");

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        // A diagnostic that crashes on a broken config is useless exactly when it is needed.
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain("could not be read");
    }

    [Fact]
    public async Task RunAsync_RegistrationUnreadable_StatusFailsNamingPathAndReason()
    {
        // An I/O failure reading the registration (locked, permission-denied, ...) must become a
        // failed check, not an unhandled exception out of RunAsync. An exclusive lock held from
        // within this process is used rather than chmod, since chmod-based "unreadable" files are
        // not reliably unreadable when tests run as root.
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
    public async Task RunAsync_ScriptUnreadable_StatusFailsNamingPathAndReason()
    {
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];

        await using (new FileStream(installation.Script.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var checks = await _sut.RunAsync(Integrators, _tempDir, default);

            var status = checks.First(c => c.Name == "gemini hook (project)");
            status.Passed.Should().BeFalse();
            status.Message.Should().Contain(installation.Script.Path).And.Contain("could not be read");
        }
    }

    [Fact]
    public async Task RunAsync_HookDoesNotRewrite_ProbeFailsAndNamesTheSubcommand()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(new CommandResult("dtk dotnet build", string.Empty, 0));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("list package");
    }

    [Fact]
    public async Task RunAsync_InterpreterMissing_ProbeFailsWithTheInterpreterName()
    {
        await IntegrateAsync();
        _runner.RunCapturedWithInputAsync(null!, null!, null!)
            .ReturnsForAnyArgs(Task.FromException<CommandResult>(
                new System.ComponentModel.Win32Exception("No such file or directory")));

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        var probe = checks.First(c => c.Name == "gemini hook probe (project)");
        probe.Passed.Should().BeFalse();
        probe.Message.Should().Contain("python3");
    }

    [Fact]
    public async Task RunAsync_EditedInterpreterInRegistration_ProbesWithThatInterpreter()
    {
        // Windows users are told to change python3 to python in settings.json. Probing with a
        // hardcoded python3 would fail a working install.
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        var json = await File.ReadAllTextAsync(installation.RegistrationPath);
        await File.WriteAllTextAsync(
            installation.RegistrationPath,
            json.Replace("python3 ", "python ", StringComparison.Ordinal));

        await _sut.RunAsync(Integrators, _tempDir, default);

        await _runner.Received().RunCapturedWithInputAsync(
            "python",
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_QuotedInterpreterPathWithSpace_ProbesWithTheWholeQuotedPath()
    {
        // On Windows a registration commonly reads "C:\Program Files\Python\python.exe" hook.py.
        // Splitting the registered command on whitespace alone would take just "C:\Program and
        // report a false failure on a working install.
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        var root = JsonNode.Parse(await File.ReadAllTextAsync(installation.RegistrationPath));
        const string interpreter = "C:\\Program Files\\Python\\python.exe";
        ReplaceStringValue(root, "python3", $"\"{interpreter}\"");
        await File.WriteAllTextAsync(installation.RegistrationPath, root!.ToJsonString());

        await _sut.RunAsync(Integrators, _tempDir, default);

        await _runner.Received().RunCapturedWithInputAsync(
            interpreter,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ScriptMovedOrDeletedButStillRegistered_FailsNamingTheMissingScript()
    {
        // A hook registered in settings.json whose script was moved or deleted must not be silently
        // skipped as "no integration here" — that would report a broken integration as healthy,
        // which is the exact failure class this checker exists to catch.
        await IntegrateAsync();
        var installation = Integrators[0].DescribeHooks(_tempDir, HookScope.Project)[0];
        File.Delete(installation.Script.Path);

        var checks = await _sut.RunAsync(Integrators, _tempDir, default);

        checks.Should().ContainSingle("one root cause must produce one failure, not two");
        checks[0].Passed.Should().BeFalse();
        checks[0].Message.Should().Contain(installation.Script.Path).And.Contain("missing");
    }

    /// <summary>Replaces every string value containing <paramref name="oldSubstring"/> in a JSON tree.</summary>
    /// <param name="node">The JSON node (object, array, or value) to search and mutate in place.</param>
    /// <param name="oldSubstring">The substring to look for in each string value.</param>
    /// <param name="newSubstring">The substring to replace it with.</param>
    private static void ReplaceStringValue(JsonNode? node, string oldSubstring, string newSubstring)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToList())
            {
                if (TryReplace(obj[key], oldSubstring, newSubstring, out var replaced))
                {
                    obj[key] = replaced;
                }
                else
                {
                    ReplaceStringValue(obj[key], oldSubstring, newSubstring);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                if (TryReplace(arr[i], oldSubstring, newSubstring, out var replaced))
                {
                    arr[i] = replaced;
                }
                else
                {
                    ReplaceStringValue(arr[i], oldSubstring, newSubstring);
                }
            }
        }
    }

    private static bool TryReplace(JsonNode? value, string oldSubstring, string newSubstring, out string? replaced)
    {
        if (value is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var text)
            && text.Contains(oldSubstring, StringComparison.Ordinal))
        {
            replaced = text.Replace(oldSubstring, newSubstring, StringComparison.Ordinal);
            return true;
        }

        replaced = null;
        return false;
    }
}
