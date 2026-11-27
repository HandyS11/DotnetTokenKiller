using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>Runs <c>dtk hook</c> as the harnesses do: a separate process, payload on stdin, reply on stdout.</summary>
public class HookIntegrationTests
{
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Claude_RewritesAndPreservesNonAsciiTextAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"tool_input":{"command":"dotnet build # répertoire"}}""", "hook", "claude");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        JsonNode.Parse(stdout)!["hookSpecificOutput"]!["updatedInput"]!["command"]!.GetValue<string>()
            .Should().Be("dtk dotnet build # répertoire", "stdin must be decoded as UTF-8 whatever the console code page");
    }

    [UnixFact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Claude_CommandBothRewrite_IsRefusedAsync()
    {
        // rtk's hook registered in the user's settings, and an rtk on PATH that rewrites git as rtk does: a shell
        // script, hence Unix only. It prefixes git (exit 3, rtk's "ask") and leaves a prefixed command alone (exit 1).
        var dir = IntegrationTestHelper.NewIsolatedDir();
        var home = Path.Combine(dir, "home");
        var bin = Path.Combine(dir, "bin");
        var project = Path.Combine(dir, "project");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(home, ".claude", "settings.json"),
            """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook claude"}]}]}}""");
        var rtk = Path.Combine(bin, "rtk");
        await File.WriteAllTextAsync(rtk, """
            #!/bin/sh
            [ "$1" = rewrite ] || exit 1
            case "$2" in
              *"rtk git"*) exit 1 ;;
              *git*) printf '%s\n' "$2" | sed 's/git/rtk git/'; exit 3 ;;
            esac
            exit 1

            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(rtk, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var payload = new JsonObject
        {
            ["hook_event_name"] = "PreToolUse",
            ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = "dotnet build; git status" },
            ["cwd"] = project
        }.ToJsonString();
        var environment = new Dictionary<string, string>
        {
            ["HOME"] = home,
            ["USERPROFILE"] = home,
            ["CLAUDE_PROJECT_DIR"] = project,
            ["PATH"] = $"{bin}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}"
        };

        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsInDirAsync(
            dir, environment, payload, "hook", "claude");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        stdout.Should().Be(RtkRewriteRace.DenyReply("dtk dotnet build; rtk git status") + "\n");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Gemini_NothingToRewrite_AllowsAsync()
    {
        var (stdout, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"tool_input":{"command":"ls"}}""", "hook", "gemini");

        exitCode.Should().Be(0);
        stdout.Trim().Should().Be("""{"decision":"allow"}""");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_CopilotCli_CompoundCommand_AsksAsync()
    {
        var (stdout, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"toolName":"bash","toolArgs":"{\"command\":\"dotnet test; ls\"}"}""", "hook", "copilot-cli");

        exitCode.Should().Be(0);
        var root = JsonNode.Parse(stdout)!;
        root["permissionDecision"]!.GetValue<string>().Should().Be("ask");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test; ls");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_CopilotCli_LineContinuationIntoTee_AsksAsync()
    {
        // `--te\<newline>e` reaches dtk as `--tee` once the shell removes the backslash-newline: never "allow".
        var (stdout, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"toolName":"bash","toolArgs":"{\"command\":\"dotnet test --te\\\\\\ne ~/.bashrc\"}"}""",
            "hook", "copilot-cli");

        exitCode.Should().Be(0);
        var root = JsonNode.Parse(stdout)!;
        root["permissionDecision"]!.GetValue<string>().Should().Be("ask");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test --te\\\ne ~/.bashrc");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_UnknownProvider_ExitsZeroWithUsageOnStderrAsync()
    {
        // dtk rejects the provider without reading stdin. A payload larger than any pipe buffer makes the
        // write outlive the process on every OS, as a fast native exit already did with "{}" on Windows.
        var payload = new string(' ', 1024 * 1024) + "{}";

        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(payload, "hook", "aider");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("dtk hook <claude|gemini|copilot-cli|codex|opencode|antigravity|pi|oh-my-pi|cursor|devin|droid|crush|kilo|amp>");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Codex_RewritesWithTheMandatoryAllowAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet test"}}""", "hook", "codex");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        var output = JsonNode.Parse(stdout)!["hookSpecificOutput"]!;
        output["permissionDecision"]!.GetValue<string>().Should().Be("allow");
        output["updatedInput"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Codex_NothingToRewrite_PrintsNothingAsync()
    {
        // A command dtk does not rewrite gets no reply at all, leaving the call to Codex's own approval policy.
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"tool_name":"Bash","tool_input":{"command":"ls"}}""", "hook", "codex");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Should().BeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_OpenCode_RewritesAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"command":"dotnet format --verify-no-changes"}""", "hook", "opencode");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        JsonNode.Parse(stdout)!["command"]!.GetValue<string>().Should().Be("dtk dotnet format --verify-no-changes");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_OpenCode_NothingToRewrite_PrintsNothingAsync()
    {
        // A command dtk does not rewrite gets no reply at all, so the plugin leaves the command as OpenCode gave it.
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"command":"ls -la"}""", "hook", "opencode");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Should().BeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Pi_RewritesAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"command":"dotnet format --verify-no-changes"}""", "hook", "pi");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        JsonNode.Parse(stdout)!["command"]!.GetValue<string>().Should().Be("dtk dotnet format --verify-no-changes");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_OhMyPi_RewritesAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"command":"dotnet format --verify-no-changes"}""", "hook", "oh-my-pi");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        JsonNode.Parse(stdout)!["command"]!.GetValue<string>().Should().Be("dtk dotnet format --verify-no-changes");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Antigravity_AsksWithTheOverwriteAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"toolCall":{"name":"run_command","args":{"CommandLine":"dotnet restore"}}}""", "hook", "antigravity");

        exitCode.Should().Be(0);
        stderr.Should().BeEmpty();
        var root = JsonNode.Parse(stdout)!;
        root["decision"]!.GetValue<string>().Should().Be("ask");
        root["overwrite"]!["CommandLine"]!.GetValue<string>().Should().Be("dtk dotnet restore");
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_Antigravity_NothingToRewrite_PrintsNothingAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync(
            """{"toolCall":{"name":"run_command","args":{"CommandLine":"ls"}}}""", "hook", "antigravity");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Should().BeEmpty();
    }

    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Hook_DoesNotTouchTrackingOrConfigAsync()
    {
        // The hook fires on every shell tool call, so it must never open the tracking database or read config.
        var dir = IntegrationTestHelper.NewIsolatedDir();

        var (_, _, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsInDirAsync(
            dir, """{"tool_input":{"command":"dotnet build"}}""", "hook", "claude");

        exitCode.Should().Be(0);
        Directory.Exists(dir).Should().BeFalse("dtk hook must write nothing under the tracking, tee or config paths");
    }
}
