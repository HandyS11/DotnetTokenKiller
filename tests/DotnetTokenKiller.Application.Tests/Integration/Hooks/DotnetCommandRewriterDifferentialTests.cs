using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

/// <summary>
/// Runs a seeded corpus of shell commands through the Python hook's <c>rewrite()</c> and
/// <c>_is_simple_command()</c> and through the C# port, and requires identical answers. It exists only
/// while the Python templates do: it is the evidence that retiring them changes no rewrite.
/// </summary>
public sealed class DotnetCommandRewriterDifferentialTests : IDisposable
{
    private const string Driver = """
        import importlib.util, json, sys
        spec = importlib.util.spec_from_file_location("hook", sys.argv[1])
        hook = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(hook)
        commands = json.load(sys.stdin)
        json.dump([[hook.rewrite(c), hook._is_simple_command(c)] for c in commands], sys.stdout)
        """;

    private static readonly string[] Fragments =
    [
        "dotnet", "dtk", "dtk.exe", "~/.dotnet/tools/dtk", @"C:\tools\dtk.exe", "/usr/bin/dotnet", "./dotnet",
        "build", "test", "restore", "clean", "format", "list", "package", "reference", "tests", "build-server",
        "publish", "--no-restore", " ", "  ", "\t", "\u00a0", "\n", ";", "&&", "||", "|", "&", "(", ")", "{", "}",
        "`", "$(", "'", "\"", "\\", "\u00e9", "\u00b2", "_x", "echo", "cd /tmp/dtk", "x=1", "2>&1", "#"
    ];

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-hookdiff-{Guid.NewGuid()}");

    public DotnetCommandRewriterDifferentialTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void Rewrite_AgreesWithThePythonHookOnEveryCorpusCommand()
    {
        var interpreter = FindPython();
        if (interpreter is null)
        {
            Environment.GetEnvironmentVariable("CI").Should().BeNullOrEmpty(
                "CI runners ship Python, so a missing interpreter there means the comparison silently stopped running");
            return;
        }

        var corpus = BuildCorpus();
        var hookPath = Path.Combine(_tempDir, "hook.py");
        File.WriteAllText(hookPath, ArtifactStamping.Apply(HookScriptTemplates.CopilotCliHook, StampStyle.HashComment));
        var driverPath = Path.Combine(_tempDir, "driver.py");
        File.WriteAllText(driverPath, Driver);

        using var python = JsonDocument.Parse(RunPython(interpreter, driverPath, hookPath, JsonSerializer.Serialize(corpus)));
        var answers = python.RootElement.EnumerateArray().ToList();

        answers.Should().HaveCount(corpus.Count);
        var mismatches = corpus
            .Select((command, i) => (command, python: answers[i]))
            .Where(pair => pair.python[0].GetString() != DotnetCommandRewriter.Rewrite(pair.command)
                           || pair.python[1].GetBoolean() != DotnetCommandRewriter.IsSimpleCommand(pair.command))
            .Select(pair => JsonSerializer.Serialize(pair.command))
            .Take(20)
            .ToList();

        mismatches.Should().BeEmpty("the C# port must rewrite exactly as the Python hook did");
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "Reproducibility is the requirement. A seeded Random makes the corpus "
                        + "byte-identical across machines and runs; a cryptographic source would "
                        + "make each run compare Python and C# against a different corpus.")]
    [SuppressMessage("Major Code Smell", "S2245:Using pseudorandom number generators is security-sensitive",
        Justification = "See CA5394: the seeded sequence is the point, and no security decision "
                        + "depends on it.")]
    private static List<string> BuildCorpus()
    {
        var random = new Random(20260914);
        var corpus = new List<string>(5000);
        for (var i = 0; i < 5000; i++)
        {
            var builder = new StringBuilder();
            var count = random.Next(1, 9);
            for (var j = 0; j < count; j++)
            {
                if (j > 0 && random.Next(2) == 0)
                {
                    builder.Append(' ');
                }

                builder.Append(Fragments[random.Next(Fragments.Length)]);
            }

            corpus.Add(builder.ToString());
        }

        return corpus;
    }

    private static string RunPython(string interpreter, string driverPath, string hookPath, string stdin)
    {
        var psi = new ProcessStartInfo(interpreter)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(driverPath);
        psi.ArgumentList.Add(hookPath);

        using var process = Process.Start(psi)!;
        process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, "the Python driver failed: {0}", stdErr);
        return stdOut;
    }

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
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
