using System.Diagnostics;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

/// <summary>
/// Runs the generated hooks through a real Python. xunit 2.x has no runtime skip, so a machine
/// without Python is tolerated locally but fails on CI, where both runner images ship one — that
/// keeps the coverage real without making a local checkout unusable.
/// </summary>
public sealed class HookScriptExecutionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-hookexec-{Guid.NewGuid()}");

    public HookScriptExecutionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("gemini")]
    [InlineData("copilot-cli")]
    public void GeneratedHook_RewritesEveryCanonicalSubcommand(string provider)
    {
        var interpreter = FindPython();
        if (interpreter is null)
        {
            Environment.GetEnvironmentVariable("CI").Should().BeNullOrEmpty(
                "CI runners ship Python, so a missing interpreter there means the probe silently stopped running");
            return;
        }

        var script = provider switch
        {
            "claude" => HookScriptTemplates.ClaudeHook,
            "gemini" => HookScriptTemplates.GeminiHook,
            _ => HookScriptTemplates.CopilotCliHook
        };

        var scriptPath = Path.Combine(_tempDir, $"{provider}-hook.py");
        File.WriteAllText(scriptPath, ArtifactStamping.Apply(script, StampStyle.HashComment));

        var command = string.Join("; ", DotnetSubcommands.Ordered.Select(sub => $"dotnet {sub}"));
        JsonNode payload = provider == "copilot-cli"
            ? new JsonObject
            {
                ["toolName"] = "bash",
                ["toolArgs"] = new JsonObject { ["command"] = command }
            }
            : new JsonObject
            {
                ["tool_input"] = new JsonObject { ["command"] = command }
            };

        var output = Run(interpreter, scriptPath, payload.ToJsonString());

        foreach (var sub in DotnetSubcommands.Ordered)
        {
            output.Should().Contain($"dtk dotnet {sub}",
                "the {0} hook must rewrite every canonical subcommand", provider);
        }
    }

    private static string Run(string interpreter, string scriptPath, string payload)
    {
        var psi = new ProcessStartInfo(interpreter)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(scriptPath);

        using var process = Process.Start(psi)!;
        process.StandardInput.Write(payload);
        process.StandardInput.Close();

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, "the hook exited non-zero: {0}", stdErr);
        return stdOut;
    }

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("--version");

                using var probe = Process.Start(psi);
                probe!.WaitForExit();
                if (probe.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Not on PATH under this name; try the next.
            }
        }

        return null;
    }
}
