# Native `dtk hook` and `dtk init` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the generated Python hook scripts with a native `dtk hook <provider>` subcommand, and rename `dtk integrate` to `dtk init` with `integrate` kept as an alias.

**Architecture:** A pure C# port of the Python `rewrite()` lives in `Application/Integration/Hooks`, with one payload handler per harness. `Program.cs` short-circuits `dtk hook …` before DI and Spectre, like the passthrough path. `init` registers `dtk hook <provider>` (guarded with `; exit 0` for Gemini and Copilot CLI) instead of writing a script, migrates Python registrations in place, deletes Python scripts dtk can prove it wrote, and `doctor` probes the `dtk` on `PATH`.

**Tech Stack:** .NET 10, C# 14, System.Text.Json (`JsonNode`, AOT-safe), Spectre.Console.Cli 0.55 (`WithAlias`), xunit + FluentAssertions + NSubstitute + Verify, POSIX sh for the shell checks.

**Spec:** `docs/superpowers/specs/2026-09-14-native-hook-and-init-design.md`

## Global Constraints

- `TreatWarningsAsErrors` is on: every analyzer warning (Roslynator, Sonar, NetAnalyzers) must be fixed, not suppressed, unless the plan says otherwise.
- `IsAotCompatible` is on for Domain, Application and Infrastructure: no reflection-based `JsonSerializer` calls in `src/`; use `JsonNode`.
- File-scoped namespaces, `var`, `_camelCase` private fields, `Async` suffix, LF line endings, 4-space indent in `.cs`, 2-space in JSON/YAML/XML, no trailing whitespace.
- XML doc comments on every new type and member in `src/` (the build generates documentation files and warns on missing ones).
- Registered commands, verbatim: Claude `dtk hook claude`; Gemini `dtk hook gemini; exit 0`; Copilot CLI `bash` and `powershell` both `dtk hook copilot-cli; exit 0`.
- `dtk hook` always exits 0 and never touches tracking, config, SQLite or the tokenizer.
- The legacy script file name is `dotnet-to-dtk.py`; the legacy signature is `_DTK_SUBCOMMANDS`.
- User-visible text says `dtk init`, never `dtk integrate` (except where documenting the alias).
- Run tests per project with `dtk dotnet test <project path> --filter "FullyQualifiedName~<Class>"`, not against the `.slnx` (a `--filter` on the solution can falsely report 0 tests).
- The build-spawning CLI integration tests (`DotnetBuildIntegrationTests` and friends) cannot pass on this machine; run the CLI integration project with a `--filter` naming the classes a task touches, and let CI gate the rest.
- Every commit message ends with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.
- Do not change `.claude/settings.json` or `.claude/hooks/dotnet-to-dtk.py` in this repository (spec section 7).

## File Map

Created:

| File | Responsibility |
|---|---|
| `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs` | Pure port of `rewrite()` and `_is_simple_command()` |
| `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs` | Provider name → payload kind; payload bytes → reply JSON |
| `src/DotnetTokenKiller.Application/Integration/Hooks/HookCommands.cs` | The `hook` verb and the registered command strings |
| `src/DotnetTokenKiller.Cli/HookEntryPoint.cs` | stdin/stdout I/O for `dtk hook`, always exit 0 |
| `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterTests.cs` | Rewriter cases |
| `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterDifferentialTests.cs` | C# vs Python on a corpus (deleted in Task 8) |
| `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs` | Per-provider reply cases |
| `tests/DotnetTokenKiller.Application.Tests/Integration/LegacyHookFixtures.cs` | Writes Python-era installs for migration tests |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs` | In-process entry point cases |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs` | `dtk hook` as a real process |
| `eng/hooks/check-hook-shells.sh` | Runs `dtk hook` through sh/bash/pwsh/powershell.exe as the harnesses do |

Renamed: `IntegrateCommand.cs` → `InitCommand.cs`, `IntegrateCommandSettings.cs` → `InitCommandSettings.cs`, `IntegrateCommandTests.cs` → `InitCommandTests.cs`, snapshot `Configure_IntegrateHelp` → `Configure_InitHelp`.

Deleted (Task 8): `HookScriptTemplates.cs`, `HookScriptTemplatesTests.cs`, `HookScriptExecutionTests.cs`, `DotnetCommandRewriterDifferentialTests.cs`.

Modified: `Program.cs`, `CliConfigurator.cs`, `CompletionCommand.cs`, `IntegratorHelpers.cs`, `IntegrationContext.cs`, `IntegrationResult.cs`, `IHookIntegrator.cs`, the three hook integrators, `HookHealthChecker.cs`, `IntegrateUseCase.cs`, `DotnetSubcommands.cs`, `ArtifactStamping.cs` (doc only), their tests, `ParityCases.cs`, `IntegrationTestHelper.cs`, `.github/copilot-instructions.md`, `eng/aot/test-windows.sh`, `eng/aot/test-package.sh`, `.github/workflows/fallback-package.yml`, `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docfx/index.md`, `docfx/articles/{getting-started,usage,ai-agent-setup}.md`, `CLAUDE.md`, the spec (one presence rule, Task 7).

---

### Task 1: Port the rewrite to C#

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterTests.cs`

**Interfaces:**
- Consumes: `DotnetTokenKiller.Domain.DotnetSubcommands.Sorted` (`IReadOnlyList<string>`, ordinal alphabetical, multi-token names space-separated).
- Produces: `internal static class DotnetCommandRewriter` with `internal static string Rewrite(string command)` (returns the same instance when nothing changes) and `internal static bool IsSimpleCommand(string command)`.

Every expectation below was produced by running the current Python `rewrite()` and `_is_simple_command()` on 2026-09-14. If a case fails, run it through `.claude/hooks/dotnet-to-dtk.py` (`python3 -c 'import importlib.util as u; s=u.spec_from_file_location("h", ".claude/hooks/dotnet-to-dtk.py"); h=u.module_from_spec(s); s.loader.exec_module(h); print(repr(h.rewrite("<case>")))'`): the Python result wins.

- [ ] **Step 1: Write the failing tests**

```csharp
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class DotnetCommandRewriterTests
{
    [Theory]
    [InlineData("dotnet build", "dtk dotnet build")]
    [InlineData("dotnet test", "dtk dotnet test")]
    [InlineData("dotnet restore", "dtk dotnet restore")]
    [InlineData("dotnet clean", "dtk dotnet clean")]
    [InlineData("dotnet format", "dtk dotnet format")]
    [InlineData("dotnet list package", "dtk dotnet list package")]
    [InlineData("dotnet list package --outdated", "dtk dotnet list package --outdated")]
    [InlineData("dotnet list   package", "dtk dotnet list   package")]
    [InlineData("dotnet   build", "dtk dotnet build")]
    [InlineData("dotnet\tformat", "dtk dotnet format")]
    [InlineData("dotnet\u00a0build", "dtk dotnet build")]
    [InlineData("cd src && dotnet test", "cd src && dtk dotnet test")]
    [InlineData("`dotnet build`", "`dtk dotnet build`")]
    [InlineData("echo dtk; dotnet build", "echo dtk; dtk dotnet build")]
    [InlineData("cd /tmp/dtk && dotnet test", "cd /tmp/dtk && dtk dotnet test")]
    [InlineData("(dotnet build)", "(dtk dotnet build)")]
    [InlineData("$(dotnet build)", "$(dtk dotnet build)")]
    [InlineData("{ dotnet build; }", "{ dtk dotnet build; }")]
    [InlineData("dotnet build\ndotnet test", "dtk dotnet build\ndtk dotnet test")]
    [InlineData("dotnet build && dotnet test", "dtk dotnet build && dtk dotnet test")]
    [InlineData("echo \u00e9 && dotnet build", "echo \u00e9 && dtk dotnet build")]
    [InlineData("echo \"a\\\" b\"; dotnet build", "echo \"a\\\" b\"; dtk dotnet build")]
    [InlineData("x=1 dotnet build", "x=1 dtk dotnet build")]
    [InlineData("sudo dotnet build", "sudo dtk dotnet build")]
    [InlineData("DTK dotnet build", "DTK dtk dotnet build")]
    [InlineData("dtk.EXE dotnet build", "dtk.EXE dtk dotnet build")]
    [InlineData("dotnet build-server shutdown", "dtk dotnet build-server shutdown")]
    public void Rewrite_QualifyingInvocation_IsPrefixedWithDtk(string command, string expected)
    {
        DotnetCommandRewriter.Rewrite(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("/usr/lib64/dotnet/dotnet build")]
    [InlineData("./dotnet build")]
    [InlineData("mydotnet build")]
    [InlineData("DOTNET build")]
    [InlineData("dotnet tests")]
    [InlineData("dotnet build_x")]
    [InlineData("dotnet build\u00e9")]
    [InlineData("dotnet build\u00b2")]
    [InlineData("dotnet publish")]
    [InlineData("dotnet --info")]
    [InlineData("dotnet list  reference")]
    [InlineData("git commit -m \"fix dotnet build\"")]
    [InlineData("echo 'dotnet test'")]
    [InlineData("echo 'it\\'s'; dotnet build")]
    [InlineData("`dtk dotnet build`")]
    [InlineData("(dtk dotnet build)")]
    [InlineData("$(dtk dotnet build)")]
    [InlineData("echo hi;dtk dotnet build")]
    [InlineData("true&&dtk dotnet test")]
    [InlineData("ls|dtk dotnet format")]
    [InlineData("dtk  dotnet build")]
    [InlineData("~/.dotnet/tools/dtk dotnet build")]
    [InlineData(@"C:\tools\dtk.exe dotnet restore")]
    [InlineData("")]
    public void Rewrite_NonQualifyingInvocation_IsReturnedUnchanged(string command)
    {
        DotnetCommandRewriter.Rewrite(command).Should().BeSameAs(command);
    }

    [Theory]
    [InlineData("dotnet build", true)]
    [InlineData("dotnet test --filter \"A|B\"", true)]
    [InlineData("dotnet test --filter 'A&B'", true)]
    [InlineData("echo \\; dotnet build", true)]
    [InlineData("dotnet build && rm -rf x", false)]
    [InlineData("dotnet build; ls", false)]
    [InlineData("dotnet build | tee log", false)]
    [InlineData("$(dotnet build)", false)]
    [InlineData("dotnet build\nls", false)]
    [InlineData("dotnet build 2>&1", false)]
    public void IsSimpleCommand_MatchesThePythonHook(string command, bool expected)
    {
        DotnetCommandRewriter.IsSimpleCommand(command).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetCommandRewriterTests"`
Expected: build error, `DotnetCommandRewriter` does not exist.

- [ ] **Step 3: Implement the rewriter**

```csharp
using System.Buffers;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// Prefixes <c>dotnet &lt;subcommand&gt;</c> invocations in a shell command with <c>dtk</c>, for the harness
/// hooks <c>dtk hook</c> answers.
/// </summary>
/// <remarks>
/// A behavior-identical port of <c>rewrite()</c> and <c>_is_simple_command()</c> from the Python hooks dtk
/// generated before <c>dtk hook</c>. A match is <c>dotnet</c>, whitespace, and a subcommand (any whitespace
/// run between a multi-token subcommand's tokens, the longest subcommand tried first, a word boundary after
/// it). It is left alone when the character before it cannot end a previous command (a path such as
/// <c>/usr/lib/dotnet/dotnet build</c>), when it sits inside quotes, or when <c>dtk</c> already runs it.
/// Python's <c>\s</c> and <c>\w</c> are Unicode-aware, which <see cref="IsPythonSpace"/> and
/// <see cref="IsPythonWord"/> reproduce.
/// </remarks>
internal static class DotnetCommandRewriter
{
    private const string Dotnet = "dotnet";
    private const string Prefix = "dtk dotnet ";

    /// <summary>Characters that may precede <c>dotnet</c> where a command starts.</summary>
    private static readonly SearchValues<char> Boundaries = SearchValues.Create(" \t;&|({`\n");

    /// <summary>Unquoted characters that chain, pipe or subshell another command.</summary>
    private static readonly SearchValues<char> Chaining = SearchValues.Create(";&|`\n()");

    /// <summary>Each subcommand's tokens, longest name first; a stable sort keeps equal lengths alphabetical.</summary>
    private static readonly string[][] SubcommandTokens =
        [.. DotnetSubcommands.Sorted.OrderByDescending(name => name.Length).Select(name => name.Split(' '))];

    /// <summary>Prefixes every qualifying <c>dotnet &lt;subcommand&gt;</c> in <paramref name="command"/> with <c>dtk</c>.</summary>
    /// <param name="command">The shell command a harness is about to run.</param>
    /// <returns>The rewritten command, or <paramref name="command"/> itself when nothing qualified.</returns>
    internal static string Rewrite(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        StringBuilder? rewritten = null;
        var copied = 0;
        var start = command.IndexOf(Dotnet, StringComparison.Ordinal);

        while (start >= 0)
        {
            if (!IsWordBefore(command, start)
                && TryMatchSubcommand(command, start + Dotnet.Length, out var subcommandStart, out var end))
            {
                if (IsCommandPosition(command, start))
                {
                    rewritten ??= new StringBuilder(command.Length + 4);
                    rewritten.Append(command, copied, start - copied)
                        .Append(Prefix)
                        .Append(command, subcommandStart, end - subcommandStart);
                    copied = end;
                }

                start = command.IndexOf(Dotnet, end, StringComparison.Ordinal);
            }
            else
            {
                start = command.IndexOf(Dotnet, start + 1, StringComparison.Ordinal);
            }
        }

        return rewritten is null ? command : rewritten.Append(command, copied, command.Length - copied).ToString();
    }

    /// <summary>
    /// Whether <paramref name="command"/> is a single invocation with no unquoted operator that could run
    /// another command beside it. Copilot CLI auto-approves only such commands.
    /// </summary>
    /// <param name="command">The original, unrewritten command.</param>
    internal static bool IsSimpleCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var i = 0;
        while (i < command.Length)
        {
            var c = command[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (Chaining.Contains(c) && !IsInsideQuotes(command, i))
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool TryMatchSubcommand(string command, int position, out int subcommandStart, out int end)
    {
        subcommandStart = SkipSpaces(command, position);
        end = subcommandStart;
        if (subcommandStart == position)
        {
            return false;
        }

        foreach (var tokens in SubcommandTokens)
        {
            if (TryMatchTokens(command, subcommandStart, tokens, out end) && !IsWordAt(command, end))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchTokens(string command, int position, string[] tokens, out int end)
    {
        end = position;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                var afterSpaces = SkipSpaces(command, end);
                if (afterSpaces == end)
                {
                    return false;
                }

                end = afterSpaces;
            }

            if (!command.AsSpan(end).StartsWith(tokens[i], StringComparison.Ordinal))
            {
                return false;
            }

            end += tokens[i].Length;
        }

        return true;
    }

    /// <summary>The boundary, quote and already-prefixed checks of the Python <c>_replace</c>.</summary>
    private static bool IsCommandPosition(string command, int start)
    {
        if (start > 0 && !Boundaries.Contains(command[start - 1]))
        {
            return false;
        }

        if (IsInsideQuotes(command, start))
        {
            return false;
        }

        var preceding = command.AsSpan(0, start);
        var length = preceding.Length;
        while (length > 0 && IsPythonSpace(preceding[length - 1]))
        {
            length--;
        }

        preceding = preceding[..length];
        var word = preceding[(preceding.LastIndexOfAny(Boundaries) + 1)..];
        var program = word[(word.LastIndexOfAny('/', '\\') + 1)..];
        return program is not ("dtk" or "dtk.exe");
    }

    /// <summary>Whether <paramref name="index"/> falls inside a quoted region, honoring backslash escapes outside single quotes.</summary>
    private static bool IsInsideQuotes(string command, int index)
    {
        var inSingle = false;
        var inDouble = false;
        var i = 0;
        while (i < index)
        {
            var c = command[i];
            if (c == '\\' && !inSingle)
            {
                i += 2;
                continue;
            }

            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }

            i++;
        }

        return inSingle || inDouble;
    }

    private static int SkipSpaces(string command, int index)
    {
        while (index < command.Length && IsPythonSpace(command[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsWordBefore(string command, int index) =>
        index > 0
        && Rune.DecodeLastFromUtf16(command.AsSpan(0, index), out var rune, out _) == OperationStatus.Done
        && IsPythonWord(rune);

    private static bool IsWordAt(string command, int index) =>
        index < command.Length
        && Rune.DecodeFromUtf16(command.AsSpan(index), out var rune, out _) == OperationStatus.Done
        && IsPythonWord(rune);

    /// <summary>Python's <c>str.isspace()</c>, which also counts the separators U+001C–U+001F.</summary>
    private static bool IsPythonSpace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';

    /// <summary>Python's <c>\w</c>: an underscore, a letter, or any numeric character, including superscripts.</summary>
    private static bool IsPythonWord(Rune rune) =>
        rune.Value == '_'
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetCommandRewriterTests"`
Expected: all pass, no build warnings.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterTests.cs
git commit -m "feat: port the hook's dotnet rewrite to C#

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 2: Prove the port matches Python on a generated corpus

**Files:**
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterDifferentialTests.cs`

**Interfaces:**
- Consumes: `DotnetCommandRewriter.Rewrite`, `DotnetCommandRewriter.IsSimpleCommand` (Task 1); `HookScriptTemplates.CopilotCliHook` (existing; its module defines both `rewrite` and `_is_simple_command`); `ArtifactStamping.Apply(string, StampStyle)`.
- Produces: nothing later tasks call. Task 8 deletes this file together with the Python templates.

- [ ] **Step 1: Write the differential test**

```csharp
using System.Diagnostics;
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
```

- [ ] **Step 2: Run it**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetCommandRewriterDifferentialTests"`
Expected: PASS. If it fails, the message lists up to 20 JSON-encoded commands. For each, compare `python3` on `.claude/hooks/dotnet-to-dtk.py` against `DotnetCommandRewriter`, fix the C# port (never the corpus), add the command as an `InlineData` case in `DotnetCommandRewriterTests`, and rerun both classes.

- [ ] **Step 3: Record the result**

Append one line to the "Differential check" section at the end of this plan: the date, the corpus size (5000 generated commands plus the Task 1 cases), and "0 mismatches". The PR description quotes it.

- [ ] **Step 4: Commit**

```bash
git add tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterDifferentialTests.cs docs/superpowers/plans/2026-09-14-native-hook-and-init.md
git commit -m "test: compare the C# rewrite with the Python hook on a seeded corpus

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 3: Reply to each harness's payload

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs`
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/HookCommands.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`

**Interfaces:**
- Consumes: `DotnetCommandRewriter` (Task 1); the existing `internal enum HookPayloadKind { ClaudeCode, GeminiCli, CopilotCli }` in `Integration/IHookIntegrator.cs`.
- Produces:
  - `internal static class HookPayloads` with `internal static bool TryGetKind(string provider, out HookPayloadKind kind)` (exact, case-sensitive names `claude`, `gemini`, `copilot-cli`) and `internal static string? Reply(HookPayloadKind kind, ReadOnlySpan<byte> payload)` (compact JSON, no trailing newline, or `null` for "print nothing").
  - `internal static class HookCommands` with `internal const string Verb = "hook"`, `internal static string Invocation(string provider)` → `"dtk hook <provider>"`, and `internal static string FailOpen(string provider)` → `"dtk hook <provider>; exit 0"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class HookPayloadsTests
{
    [Theory]
    [InlineData("claude", HookPayloadKind.ClaudeCode)]
    [InlineData("gemini", HookPayloadKind.GeminiCli)]
    [InlineData("copilot-cli", HookPayloadKind.CopilotCli)]
    public void TryGetKind_KnownProvider_Resolves(string provider, HookPayloadKind expected)
    {
        HookPayloads.TryGetKind(provider, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("Claude")]
    [InlineData("copilot")]
    [InlineData("")]
    public void TryGetKind_UnknownProvider_Fails(string provider)
    {
        HookPayloads.TryGetKind(provider, out _).Should().BeFalse();
    }

    [Fact]
    public void HookCommands_RenderTheRegisteredCommands()
    {
        HookCommands.Invocation("claude").Should().Be("dtk hook claude");
        HookCommands.FailOpen("gemini").Should().Be("dtk hook gemini; exit 0");
    }

    [Fact]
    public void Claude_Rewrite_ReturnsTheWholeToolInputWithTheCommandReplaced()
    {
        var reply = Reply(HookPayloadKind.ClaudeCode,
            """{"tool_name":"Bash","tool_input":{"command":"dotnet build","description":"Build","timeout":120000}}""");

        var root = JsonNode.Parse(reply!)!;
        root["hookSpecificOutput"]!["hookEventName"]!.GetValue<string>().Should().Be("PreToolUse");
        var updated = root["hookSpecificOutput"]!["updatedInput"]!;
        updated["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        updated["description"]!.GetValue<string>().Should().Be("Build");
        updated["timeout"]!.GetValue<int>().Should().Be(120000);
    }

    [Theory]
    [InlineData("""{"tool_input":{"command":"ls -la"}}""")]
    [InlineData("""{"tool_input":{"command":""}}""")]
    [InlineData("""{"tool_input":{"command":42}}""")]
    [InlineData("""{"tool_input":{}}""")]
    [InlineData("""{}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""null""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public void Claude_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.ClaudeCode, payload).Should().BeNull();
    }

    [Fact]
    public void Claude_NonAsciiCommand_RoundTripsExactly()
    {
        var reply = Reply(HookPayloadKind.ClaudeCode, """{"tool_input":{"command":"dotnet build # répertoire ’ok’"}}""");

        JsonNode.Parse(reply!)!["hookSpecificOutput"]!["updatedInput"]!["command"]!.GetValue<string>()
            .Should().Be("dtk dotnet build # répertoire ’ok’");
    }

    [Fact]
    public void Claude_LeadingUtf8Bom_IsIgnored()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("""{"tool_input":{"command":"dotnet test"}}""")).ToArray();

        HookPayloads.Reply(HookPayloadKind.ClaudeCode, bytes).Should().Contain("dtk dotnet test");
    }

    [Fact]
    public void Gemini_Rewrite_AllowsWithTheRewrittenToolInput()
    {
        var reply = Reply(HookPayloadKind.GeminiCli, """{"tool_name":"run_shell_command","tool_input":{"command":"dotnet test"}}""");

        var root = JsonNode.Parse(reply!)!;
        root["decision"]!.GetValue<string>().Should().Be("allow");
        root["hookSpecificOutput"]!["tool_input"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet test");
    }

    [Theory]
    [InlineData("""{"tool_input":{"command":"ls"}}""")]
    [InlineData("""{"tool_input":{}}""")]
    [InlineData("""[]""")]
    public void Gemini_ParsedButNothingToRewrite_AllowsWithoutChange(string payload)
    {
        Reply(HookPayloadKind.GeminiCli, payload).Should().Be("""{"decision":"allow"}""");
    }

    [Fact]
    public void Gemini_InvalidJson_PrintsNothing()
    {
        Reply(HookPayloadKind.GeminiCli, "{").Should().BeNull();
    }

    [Theory]
    [InlineData("""{"toolName":"bash","toolArgs":{"command":"dotnet build","description":"d"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"{\"command\":\"dotnet build\",\"description\":\"d\"}"}""")]
    public void Copilot_SimpleCommand_AllowsWithModifiedArgs(string payload)
    {
        var root = JsonNode.Parse(Reply(HookPayloadKind.CopilotCli, payload)!)!;

        root["permissionDecision"]!.GetValue<string>().Should().Be("allow");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build");
        root["modifiedArgs"]!["description"]!.GetValue<string>().Should().Be("d");
    }

    [Fact]
    public void Copilot_CompoundCommand_AsksInsteadOfAllowing()
    {
        var root = JsonNode.Parse(Reply(HookPayloadKind.CopilotCli,
            """{"toolName":"bash","toolArgs":{"command":"dotnet build && rm -rf x"}}""")!)!;

        root["permissionDecision"]!.GetValue<string>().Should().Be("ask");
        root["modifiedArgs"]!["command"]!.GetValue<string>().Should().Be("dtk dotnet build && rm -rf x");
    }

    [Theory]
    [InlineData("""{"toolName":"powershell","toolArgs":{"command":"dotnet build"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":{"command":"ls"}}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"not json"}""")]
    [InlineData("""{"toolName":"bash","toolArgs":"[1]"}""")]
    [InlineData("""{"toolName":"bash"}""")]
    [InlineData("""[]""")]
    [InlineData("""{""")]
    public void Copilot_NothingToRewrite_PrintsNothing(string payload)
    {
        Reply(HookPayloadKind.CopilotCli, payload).Should().BeNull();
    }

    private static string? Reply(HookPayloadKind kind, string payload) =>
        HookPayloads.Reply(kind, Encoding.UTF8.GetBytes(payload));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Expected: build error, `HookPayloads` and `HookCommands` do not exist.

- [ ] **Step 3: Implement `HookCommands`**

```csharp
namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>The <c>dtk hook</c> verb and the commands <c>dtk init</c> registers with each harness.</summary>
internal static class HookCommands
{
    /// <summary>The verb a harness runs: <c>dtk hook &lt;provider&gt;</c>.</summary>
    internal const string Verb = "hook";

    /// <summary>The bare hook command, e.g. <c>dtk hook claude</c>.</summary>
    /// <param name="provider">The provider name, as <c>dtk init</c> spells it.</param>
    internal static string Invocation(string provider) => $"dtk {Verb} {provider}";

    /// <summary>
    /// The hook command followed by <c>; exit 0</c>, for harnesses that block the tool call when the hook
    /// exits non-zero (Gemini CLI on any code but 0 or 1, Copilot CLI on any code). It keeps a missing
    /// <c>dtk</c> from blocking every shell command, and parses the same under bash, PowerShell 7 and
    /// Windows PowerShell 5.1, which has no <c>||</c>.
    /// </summary>
    /// <param name="provider">The provider name, as <c>dtk init</c> spells it.</param>
    internal static string FailOpen(string provider) => $"{Invocation(provider)}; exit 0";
}
```

- [ ] **Step 4: Implement `HookPayloads`**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>Turns a harness's pre-tool payload into the reply that makes it run <c>dtk dotnet …</c>.</summary>
/// <remarks>
/// The replies are the ones the Python hooks printed. Other fields of the tool input round-trip untouched.
/// An unexpected payload shape yields no rewrite rather than an exception, because a hook that fails must
/// never block the tool call.
/// </remarks>
internal static class HookPayloads
{
    /// <summary>Resolves a provider name (<c>claude</c>, <c>gemini</c>, <c>copilot-cli</c>) to its payload shape.</summary>
    /// <param name="provider">The name passed to <c>dtk hook</c>.</param>
    /// <param name="kind">The payload shape, when the name is known.</param>
    internal static bool TryGetKind(string provider, out HookPayloadKind kind)
    {
        (var known, kind) = provider switch
        {
            "claude" => (true, HookPayloadKind.ClaudeCode),
            "gemini" => (true, HookPayloadKind.GeminiCli),
            "copilot-cli" => (true, HookPayloadKind.CopilotCli),
            _ => (false, default)
        };
        return known;
    }

    /// <summary>Builds the reply for one payload.</summary>
    /// <param name="kind">The harness that sent the payload.</param>
    /// <param name="payload">The UTF-8 payload, with or without a byte order mark.</param>
    /// <returns>The JSON to print, or <see langword="null"/> when the hook should print nothing.</returns>
    internal static string? Reply(HookPayloadKind kind, ReadOnlySpan<byte> payload)
    {
        if (payload.StartsWith("﻿"u8))
        {
            payload = payload[3..];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        return kind switch
        {
            HookPayloadKind.ClaudeCode => ReplyToClaude(root),
            HookPayloadKind.GeminiCli => ReplyToGemini(root),
            HookPayloadKind.CopilotCli => ReplyToCopilot(root),
            _ => null
        };
    }

    private static string? ReplyToClaude(JsonNode? root)
    {
        if (root is not JsonObject payload
            || payload["tool_input"] is not JsonObject toolInput
            || !TryRewrite(toolInput, out _, out var rewritten))
        {
            return null;
        }

        var updatedInput = (JsonObject)toolInput.DeepClone();
        updatedInput["command"] = rewritten;
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["updatedInput"] = updatedInput
            }
        }.ToJsonString();
    }

    private static string ReplyToGemini(JsonNode? root)
    {
        var reply = new JsonObject { ["decision"] = "allow" };
        if (root is JsonObject payload
            && payload["tool_input"] is JsonObject toolInput
            && TryRewrite(toolInput, out _, out var rewritten))
        {
            reply["hookSpecificOutput"] = new JsonObject
            {
                ["tool_input"] = new JsonObject { ["command"] = rewritten }
            };
        }

        return reply.ToJsonString();
    }

    private static string? ReplyToCopilot(JsonNode? root)
    {
        if (root is not JsonObject payload
            || payload["toolName"] is not JsonValue toolName
            || !toolName.TryGetValue<string>(out var name)
            || name != "bash")
        {
            return null;
        }

        var toolArgs = payload["toolArgs"] switch
        {
            JsonObject obj => (JsonObject)obj.DeepClone(),
            JsonValue value when value.TryGetValue<string>(out var text) => ParseObject(text),
            _ => null
        };

        if (toolArgs is null || !TryRewrite(toolArgs, out var command, out var rewritten))
        {
            return null;
        }

        toolArgs["command"] = rewritten;
        return new JsonObject
        {
            ["permissionDecision"] = DotnetCommandRewriter.IsSimpleCommand(command) ? "allow" : "ask",
            ["modifiedArgs"] = toolArgs
        }.ToJsonString();
    }

    private static bool TryRewrite(JsonObject arguments, out string command, out string rewritten)
    {
        command = string.Empty;
        rewritten = string.Empty;
        if (arguments["command"] is not JsonValue value || !value.TryGetValue(out string? text) || text.Length == 0)
        {
            return false;
        }

        command = text;
        rewritten = DotnetCommandRewriter.Rewrite(text);
        return !ReferenceEquals(rewritten, text);
    }

    private static JsonObject? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

`Rewrite` returns the same instance when nothing changed (Task 1's `BeSameAs` tests pin that), so `ReferenceEquals` is the change test.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs src/DotnetTokenKiller.Application/Integration/Hooks/HookCommands.cs tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs
git commit -m "feat: build Claude, Gemini and Copilot CLI hook replies in C#

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 4: `dtk hook <provider>` entry point

**Files:**
- Create: `src/DotnetTokenKiller.Cli/HookEntryPoint.cs`
- Modify: `src/DotnetTokenKiller.Cli/Program.cs` (top of file)
- Modify: `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs` (add one method)
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs`

**Interfaces:**
- Consumes: `HookPayloads.TryGetKind`, `HookPayloads.Reply`, `HookCommands.Verb` (Task 3).
- Produces: `internal static class HookEntryPoint` with `internal static int Run(IReadOnlyList<string> args)` and the test seam `internal static int Run(IReadOnlyList<string> args, bool isInputRedirected, Func<Stream> openInput, Func<Stream> openOutput, TextWriter error)`; `IntegrationTestHelper.RunDtkSeparatingStreamsAsync(string stdin, params string[] args)` returning `Task<(string StdOut, string StdErr, int ExitCode)>`.

- [ ] **Step 1: Write the failing in-process tests**

```csharp
using System.Text;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class HookEntryPointTests
{
    private const string ClaudePayload = """{"tool_input":{"command":"dotnet build"}}""";

    [Fact]
    public void Run_RewritingPayload_PrintsTheReplyAndANewline()
    {
        var (exitCode, stdout, stderr) = Run(["claude"], ClaudePayload);

        exitCode.Should().Be(0);
        stdout.Should().Contain("dtk dotnet build").And.EndWith("\n");
        stderr.Should().BeEmpty();
    }

    [Fact]
    public void Run_NothingToRewrite_PrintsNothing()
    {
        var (exitCode, stdout, _) = Run(["claude"], """{"tool_input":{"command":"ls"}}""");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
    }

    [Theory]
    [InlineData(new string[0])]
    [InlineData(new[] { "cursor" })]
    [InlineData(new[] { "claude", "extra" })]
    public void Run_BadArguments_PrintsUsageToStderrAndExitsZero(string[] args)
    {
        var (exitCode, stdout, stderr) = Run(args, ClaudePayload);

        exitCode.Should().Be(0, "a harness blocks the tool call on a non-zero hook exit");
        stdout.Should().BeEmpty();
        stderr.Should().Contain("dtk hook <claude|gemini|copilot-cli>");
    }

    [Fact]
    public void Run_StdinIsATerminal_DoesNotWaitForInput()
    {
        var opened = false;
        using var output = new MemoryStream();
        using var error = new StringWriter();

        var exitCode = HookEntryPoint.Run(["claude"], isInputRedirected: false,
            () => { opened = true; return new MemoryStream(); }, () => output, error);

        exitCode.Should().Be(0);
        opened.Should().BeFalse("a person who types 'dtk hook claude' must not be left waiting on stdin");
        error.ToString().Should().Contain("dtk hook <claude|gemini|copilot-cli>");
    }

    [Fact]
    public void Run_InputStreamThrows_ExitsZeroWithNoOutput()
    {
        using var output = new MemoryStream();
        using var error = new StringWriter();

        var exitCode = HookEntryPoint.Run(["gemini"], isInputRedirected: true,
            () => throw new IOException("broken pipe"), () => output, error);

        exitCode.Should().Be(0);
        output.Length.Should().Be(0);
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string[] args, string stdin)
    {
        using var output = new MemoryStream();
        using var error = new StringWriter();
        var exitCode = HookEntryPoint.Run(args, isInputRedirected: true,
            () => new MemoryStream(Encoding.UTF8.GetBytes(stdin)), () => output, error);
        return (exitCode, Encoding.UTF8.GetString(output.ToArray()), error.ToString());
    }
}
```

`HookEntryPoint.Run` must not dispose the stream returned by `openOutput` before the test reads it; `MemoryStream.ToArray()` works after disposal, so disposing is fine either way.

- [ ] **Step 2: Write the failing process-level tests**

Add to `IntegrationTestHelper` (next to `RunDtkWithStdinInDirAsync`):

```csharp
    /// <summary>Runs dtk with <paramref name="stdin"/> piped in, keeping stdout and stderr apart, for
    /// commands whose stdout is read by a program (<c>dtk hook</c>).</summary>
    /// <param name="stdin">The text to write to the process's standard input, as UTF-8.</param>
    /// <param name="args">The arguments to pass to dtk.</param>
    internal static Task<(string StdOut, string StdErr, int ExitCode)> RunDtkSeparatingStreamsAsync(
        string stdin, params string[] args) =>
        RunDtkSeparatingStreamsInDirAsync(NewIsolatedDir(), stdin, args);

    /// <summary>As <see cref="RunDtkSeparatingStreamsAsync"/>, against an explicit isolated directory.</summary>
    /// <param name="isolatedDir">A directory from <see cref="NewIsolatedDir"/>; dtk's tracking, tee and config paths point inside it.</param>
    /// <param name="stdin">The text to write to the process's standard input, as UTF-8.</param>
    /// <param name="args">The arguments to pass to dtk.</param>
    internal static async Task<(string StdOut, string StdErr, int ExitCode)> RunDtkSeparatingStreamsInDirAsync(
        string isolatedDir, string stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(Launcher.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Environment =
            {
                ["DTK_DB_PATH"] = Path.Combine(isolatedDir, "tracking.db"),
                ["DTK_TEE_DIR"] = Path.Combine(isolatedDir, "tee"),
                ["DTK_CONFIG_PATH"] = Path.Combine(isolatedDir, "config.json")
            }
        };
        foreach (var arg in Launcher.PrefixArguments.Concat(args))
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dtk");
        await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return (await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), process.ExitCode);
    }
```

Then create `HookIntegrationTests.cs`:

```csharp
using System.Text.Json.Nodes;
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
    public async Task Hook_UnknownProvider_ExitsZeroWithUsageOnStderrAsync()
    {
        var (stdout, stderr, exitCode) = await IntegrationTestHelper.RunDtkSeparatingStreamsAsync("{}", "hook", "cursor");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("dtk hook <claude|gemini|copilot-cli>");
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests"`
Expected: build error, `HookEntryPoint` does not exist.

- [ ] **Step 4: Implement the entry point**

```csharp
using System.Text;
using DotnetTokenKiller.Application.Integration.Hooks;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Answers a harness's pre-tool hook, <c>dtk hook &lt;provider&gt;</c>, without building the DI container.
/// </summary>
/// <remarks>
/// Harnesses run this on every shell tool call, so like <see cref="PassthroughEntryPoint"/> it skips Spectre
/// and the service container, and it never loads config, tracking or the tokenizer. It reads stdin as raw
/// bytes, because <see cref="Console.In"/> decodes with the OEM code page on Windows and would corrupt a
/// non-ASCII command on the way back out. It always exits 0: Gemini CLI and Copilot CLI block the tool call
/// when a hook exits non-zero, so every failure here means "no rewrite".
/// </remarks>
internal static class HookEntryPoint
{
    private const string Usage =
        "usage: dtk hook <claude|gemini|copilot-cli> (run by an AI agent's pre-tool hook; reads the payload on stdin)";

    /// <summary>Handles <c>dtk hook</c> with the process's own standard streams.</summary>
    /// <param name="args">The arguments after <c>hook</c>.</param>
    /// <returns>Always 0.</returns>
    internal static int Run(IReadOnlyList<string> args) =>
        Run(args, Console.IsInputRedirected, Console.OpenStandardInput, Console.OpenStandardOutput, Console.Error);

    /// <summary>Handles <c>dtk hook</c> with the given streams.</summary>
    /// <param name="args">The arguments after <c>hook</c>.</param>
    /// <param name="isInputRedirected">Whether stdin is a pipe or file rather than a terminal.</param>
    /// <param name="openInput">Opens stdin; not called when <paramref name="isInputRedirected"/> is false.</param>
    /// <param name="openOutput">Opens stdout; only called when there is a reply to print.</param>
    /// <param name="error">Receives the usage line.</param>
    /// <returns>Always 0.</returns>
    internal static int Run(
        IReadOnlyList<string> args,
        bool isInputRedirected,
        Func<Stream> openInput,
        Func<Stream> openOutput,
        TextWriter error)
    {
        try
        {
            if (args.Count != 1 || !HookPayloads.TryGetKind(args[0], out var kind) || !isInputRedirected)
            {
                error.WriteLine(Usage);
                return 0;
            }

            using var buffer = new MemoryStream();
            using (var input = openInput())
            {
                input.CopyTo(buffer);
            }

            var reply = HookPayloads.Reply(kind, buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            if (reply is null)
            {
                return 0;
            }

            using var output = openOutput();
            output.Write(Encoding.UTF8.GetBytes(reply + "\n"));
            output.Flush();
        }
        catch (Exception)
        {
            // A failed hook must never block the tool call: print nothing and let the command run unrewritten.
        }

        return 0;
    }
}
```

- [ ] **Step 5: Route `dtk hook` in `Program.cs`**

Insert above `Console.OutputEncoding = Encoding.UTF8;` (and add `using DotnetTokenKiller.Application.Integration.Hooks;` to the usings):

```csharp
// A harness's pre-tool hook runs this on every shell tool call, so it skips everything below: encoding
// setup, argument normalization, the service container and Spectre.
if (args is [HookCommands.Verb, ..])
{
    return HookEntryPoint.Run(args[1..]);
}

```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk dotnet build DotnetTokenKiller.slnx` then `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests|FullyQualifiedName~HookIntegrationTests|FullyQualifiedName~AotParitySkipTests|FullyQualifiedName~SpectreBuiltInCommandTests|FullyQualifiedName~CommandSettingsAotGuardTests"`
Expected: all pass. `AotParityTests` itself skips locally without `DTK_AOT_BINARY`.

- [ ] **Step 7: Smoke-test the JIT build by hand**

Run (the PreToolUse hook rewrites a leading `dotnet <sub>` only, so `dotnet <path>/dtk.dll hook` is safe):

```bash
printf '%s' '{"tool_input":{"command":"dotnet build"}}' | dotnet src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk.dll hook claude; echo "exit=$?"
```

Expected: `{"hookSpecificOutput":{"hookEventName":"PreToolUse","updatedInput":{"command":"dtk dotnet build"}}}` then `exit=0`.

- [ ] **Step 8: Commit**

```bash
git add src/DotnetTokenKiller.Cli/HookEntryPoint.cs src/DotnetTokenKiller.Cli/Program.cs tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/IntegrationTestHelper.cs
git commit -m "feat: add dtk hook, answering harness payloads before DI and Spectre

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 5: Rename `integrate` to `init`, keeping `integrate` as an alias

**Files:**
- Rename: `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs` → `InitCommand.cs`; `src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs` → `InitCommandSettings.cs`; `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs` → `InitCommandTests.cs`; `tests/DotnetTokenKiller.Cli.IntegrationTests/Snapshots/CliConfiguratorTests.Configure_IntegrateHelp_MatchesSnapshot.verified.txt` → `...Configure_InitHelp_MatchesSnapshot.verified.txt`
- Modify: `src/DotnetTokenKiller.Cli/CliConfigurator.cs`, `src/DotnetTokenKiller.Cli/Commands/CompletionCommand.cs`, `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (remedy strings only), `src/DotnetTokenKiller.Application/Integration/IntegrateUseCase.cs`, `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs` (note string only), `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` (doc comment only), `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs` (doc comment only)
- Modify tests: `CliConfiguratorTests.cs` and its root-help snapshot, `CompletionCommandTests.cs`, `CommandDispatchIntegrationTests.cs`, `Aot/ParityCases.cs`, `HookHealthCheckerTests.cs`, `SubcommandBindingTests.cs` (message only), `IntegrateUseCaseTests.cs` (if it asserts the message)

**Interfaces:**
- Consumes: nothing new.
- Produces: `CliConfigurator.InitCommand = "init"`, `CliConfigurator.IntegrateAlias = "integrate"`; classes `InitCommand : AsyncCommand<InitCommandSettings>` (same `RunAsync(InitCommandSettings, CancellationToken)`), `InitCommandSettings` (same properties). `HookHealthChecker` remedies read `dtk init <provider>[ --global][ --force]`. Task 6 adds `removed` output to `InitCommand`; Task 7 rewrites the rest of `HookHealthChecker`.

- [ ] **Step 1: Write the failing tests**

In `CliConfiguratorTests.cs`, replace `Configure_IntegrateHelp_MatchesSnapshot` with:

```csharp
    [Fact]
    public Task Configure_InitHelp_MatchesSnapshot()
    {
        return Verify(RunHelp("init", "--help"));
    }
```

`git mv` the old snapshot to `CliConfiguratorTests.Configure_InitHelp_MatchesSnapshot.verified.txt` and edit it: `dtk integrate` → `dtk init` in the USAGE line and all ten EXAMPLES lines; everything else stays.

In `CommandDispatchIntegrationTests.cs`, add beside `Integrate_RunsThroughTheRealCliAndWritesIntoTheGivenDirectoryAsync`:

```csharp
    [Fact(Timeout = IntegrationTestHelper.DefaultTimeoutMs)]
    public async Task Init_AndTheIntegrateAlias_WriteTheSameFilesAsync()
    {
        var dir = IntegrationTestHelper.NewIsolatedDir();
        var viaInit = Path.Combine(dir, "init");
        var viaAlias = Path.Combine(dir, "alias");
        Directory.CreateDirectory(viaInit);
        Directory.CreateDirectory(viaAlias);

        var (initOutput, initExit) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "init", "cursor", "--dir", viaInit);
        var (aliasOutput, aliasExit) = await IntegrationTestHelper.RunDtkInDirAsync(dir, "integrate", "cursor", "--dir", viaAlias);

        initExit.Should().Be(0);
        aliasExit.Should().Be(initExit);
        aliasOutput.Should().Be(initOutput, "the alias must behave exactly like init");
        Directory.EnumerateFiles(viaAlias, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(viaAlias, f))
            .Should().BeEquivalentTo(Directory.EnumerateFiles(viaInit, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(viaInit, f)));
    }
```

(Output lines are relative to `--dir`, so the two runs print identical text.)

In `CompletionCommandTests.cs`: the four `script.Should().Contain("integrate")` become `Contain("init")`; the bash `InlineData` fragments become `"local top_cmds=\"dotnet pipe init config doctor completion gain log reset --version --help\""`; the powershell fragments become `"$topCmds = @('dotnet', 'pipe', 'init', 'config', 'doctor', 'completion', 'gain', 'log', 'reset')"`. Add:

```csharp
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("powershell")]
    public async Task ExecuteAsync_EveryShell_CompletesProvidersAfterInitAndTheAliasIncludingCopilotCli(string shell)
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new CompletionCommandSettings { Shell = shell }, CancellationToken.None);

        var script = writer.ToString();
        script.Should().Contain("init").And.Contain("integrate", "the alias still completes providers");
        script.Should().Contain("copilot-cli");
    }
```

In `HookHealthCheckerTests.cs`, replace every expected `"dtk integrate"` / `"dtk integrate gemini"` with `"dtk init"` / `"dtk init gemini"` (five places).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~CompletionCommandTests|FullyQualifiedName~CommandDispatchIntegrationTests"` and `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookHealthCheckerTests"`
Expected: failures on the new names.

- [ ] **Step 3: Rename the command**

```bash
git mv src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs src/DotnetTokenKiller.Cli/Commands/InitCommand.cs
git mv src/DotnetTokenKiller.Cli/Commands/Settings/IntegrateCommandSettings.cs src/DotnetTokenKiller.Cli/Commands/Settings/InitCommandSettings.cs
git mv tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs
```

Rename the types `IntegrateCommand` → `InitCommand`, `IntegrateCommandSettings` → `InitCommandSettings` and the test class `IntegrateCommandTests` → `InitCommandTests` (and every use inside those three files). In `InitCommand.cs` change the summary to `/// <summary>Installs dtk integration artifacts for the given AI assistant provider (<c>dtk init</c>, alias <c>dtk integrate</c>).</summary>`. In `InitCommandSettings.cs` the `<provider>` description becomes `"AI assistant provider to set up (claude, copilot, copilot-cli, gemini, cursor, windsurf, aider, jetbrains)"` — then update the ARGUMENTS text in the new `Configure_InitHelp` snapshot to match (Spectre wraps at 100 columns; let Verify show the received file and copy it if only wrapping differs).

In `CliConfigurator.cs` replace the `IntegrateCommand` constant and registration:

```csharp
    /// <summary>The <c>init</c> command name.</summary>
    public const string InitCommand = "init";

    /// <summary>The <c>init</c> command's former name, kept as an alias so existing scripts and docs keep working.</summary>
    public const string IntegrateAlias = "integrate";
```

```csharp
        config.AddCommand<Commands.InitCommand>(InitCommand)
            .WithAlias(IntegrateAlias)
            .WithDescription("Install dtk integration artifacts for an AI assistant provider")
            .WithExample(InitCommand, "claude")
            .WithExample(InitCommand, "claude", "--dir", "/path/to/project", "--force")
            .WithExample(InitCommand, "copilot")
            .WithExample(InitCommand, "copilot-cli")
            .WithExample(InitCommand, "copilot-cli", "--global")
            .WithExample(InitCommand, "gemini")
            .WithExample(InitCommand, "cursor")
            .WithExample(InitCommand, "windsurf")
            .WithExample(InitCommand, "aider")
            .WithExample(InitCommand, "jetbrains");
```

- [ ] **Step 4: Update completion scripts**

In `CompletionCommand.cs`:

- bash: `local integrate_providers="claude copilot gemini cursor windsurf aider jetbrains"` → `local init_providers="claude copilot copilot-cli gemini cursor windsurf aider jetbrains"`; `top_cmds` lists `init` instead of `integrate`; the case label `integrate)` → `init|integrate)` and `$integrate_providers` → `$init_providers`; the option list `"--dir --force --help"` → `"--dir --force --global --help"`.
- zsh: `'integrate:Install dtk integration artifacts'` → `'init:Install dtk integration artifacts'`; rename the array `integrate_providers` → `init_providers` and add `'copilot-cli:Install dtk hook and instructions for GitHub Copilot CLI'` after the `copilot` entry; case label `integrate)` → `init|integrate)` using `init_providers`.
- fish: `-a integrate   -d 'Install dtk integration artifacts'` → `-a init        -d 'Install dtk integration artifacts'`; the comment `# integrate subcommands` → `# init providers (also after the integrate alias)`; every `'__fish_seen_subcommand_from integrate'` → `'__fish_seen_subcommand_from init integrate'`; add after the copilot line: `complete -c dtk -f -n '__fish_seen_subcommand_from init integrate' -a copilot-cli -d 'Install dtk hook and instructions for GitHub Copilot CLI'`.
- powershell: `$topCmds` lists `'init'` instead of `'integrate'`; `$providers` adds `'copilot-cli'` after `'copilot'`; the switch line `'integrate' { if ($count -eq 1) { $providers } }` becomes two lines, `'init'      { if ($count -eq 1) { $providers } }` and `'integrate' { if ($count -eq 1) { $providers } }`.

- [ ] **Step 5: Update user-visible strings**

- `HookHealthChecker.cs`: `"Run 'dtk integrate <provider>' to install one."` → `"Run 'dtk init <provider>' to install one."`; `RemedyCommand` returns `$"dtk init {installation.ProviderName}{scopeFlag}{trailingFlags}"`; its summary says `<c>dtk init</c>`.
- `IntegrateUseCase.cs`: `$"Run 'dtk integrate {providerName}' inside a project."` → `$"Run 'dtk init {providerName}' inside a project."`; the comment naming `IntegrateCommand` → `InitCommand`.
- `CopilotCliIntegrator.cs`: `"Run 'dtk integrate copilot-cli' inside a project…"` → `"Run 'dtk init copilot-cli' inside a project…"`.
- `ClaudeCodeIntegrator.cs` doc comment and `DotnetSubcommands.cs` remark: `dtk integrate claude` → `dtk init claude`.
- `SubcommandBindingTests.cs` message: `'dtk integrate copilot-cli'` → `'dtk init copilot-cli'`.
- `ParityCases.cs`: `integrate-project` steps use `"init"` for every provider, keep the final `new ParityStep(["integrate", "claude", "--dir", "{project}"])` (it exercises the alias), and rename the case keys to `init-project` / `init-global` with `init` in both global steps.

Then run `rtk proxy grep -rn "dtk integrate" src tests --include=*.cs` and confirm the only hits are deliberate alias references.

- [ ] **Step 6: Accept the root-help snapshot**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CliConfiguratorTests"`
Expected: `Configure_RootHelp_MatchesSnapshot` fails with a `.received.txt`. Inspect it: the only differences must be `dtk integrate` → `dtk init` in EXAMPLES and the COMMANDS row `init <provider>` (column padding may shrink). If so, move it over the `.verified.txt`; otherwise fix the code.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~CliConfiguratorTests|FullyQualifiedName~CompletionCommandTests|FullyQualifiedName~CommandDispatchIntegrationTests|FullyQualifiedName~InitCommandTests|FullyQualifiedName~DocsBindingTests|FullyQualifiedName~AotParitySkipTests"` and `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all pass. `DocsBindingTests` checks a substring, so it passes before the docs say `dtk init`; Task 9 updates them.

- [ ] **Step 8: Commit**

```bash
git add -A src tests
git commit -m "feat: rename dtk integrate to dtk init, keeping integrate as an alias

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 6: Migration helpers and the `removed` result

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs`
- Modify: `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/InitCommand.cs`
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/LegacyHookFixtures.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/InitCommandTests.cs`

**Interfaces:**
- Consumes: `ArtifactStamping.IsAuthentic(string)`, `ArtifactStamping.HasStamp(string)`, `IntegratorHelpers.HookLegacySignature` (all existing).
- Produces (Task 7 calls these):
  - `internal sealed record HookRegistrationSpec(string SettingsPath, string EventKey, string Matcher, string Command);`
  - `IntegratorHelpers.LegacyHookScriptName` = `"dotnet-to-dtk.py"`.
  - `static Task WriteHookRegistrationAsync(HookRegistrationSpec spec, IntegrationContext context, CancellationToken cancellationToken)` — merges only, writes no script.
  - `static Task RemoveLegacyHookScriptAsync(string scriptPath, IntegrationContext context, CancellationToken cancellationToken)`.
  - `static Task WriteOwnedFileAsync(string path, string content, bool replaceExisting, IntegrationContext context, CancellationToken cancellationToken)` — `WriteFileAsync` that also overwrites without `--force` when `replaceExisting`.
  - `IntegrationContext.Removed` (`List<string>`), `IntegrationResult.RemovedFiles` (`IReadOnlyList<string>`, init, default empty).
  - `LegacyHookFixtures.WriteStampedScript(string path)`, `WriteEditedScript(string path)`, `WriteUnstampedScript(string path)`, `PythonCommand(string scriptPath)` for tests.
- `MergeJsonSettingsAsync` keeps its signature; its "equivalent entry" rule becomes: identical after removing `"`, **or** the command contains `dotnet-to-dtk.py`. `DeriveLegacyCommand` is deleted.

- [ ] **Step 1: Add the test fixtures**

```csharp
using DotnetTokenKiller.Application.Integration;

namespace DotnetTokenKiller.Application.Tests.Integration;

/// <summary>Writes the Python-era hook installs that <c>dtk init</c> must migrate.</summary>
internal static class LegacyHookFixtures
{
    /// <summary>A stand-in body: every generation of the Python hook carried the legacy signature.</summary>
    private const string Body = "#!/usr/bin/env python3\n_DTK_SUBCOMMANDS = (\"build\", \"test\")\n";

    /// <summary>A script exactly as a stamped dtk generation wrote it.</summary>
    internal static void WriteStampedScript(string path) =>
        Write(path, ArtifactStamping.Apply(Body, StampStyle.HashComment));

    /// <summary>A stamped script someone edited afterwards, so its stamp no longer verifies.</summary>
    internal static void WriteEditedScript(string path) =>
        Write(path, ArtifactStamping.Apply(Body, StampStyle.HashComment).Replace("\"test\"", "\"publish\"", StringComparison.Ordinal));

    /// <summary>A script from dtk 0.6.0 or earlier, before stamping.</summary>
    internal static void WriteUnstampedScript(string path) => Write(path, Body);

    /// <summary>The command dtk registered for a Python hook at <paramref name="scriptPath"/>.</summary>
    internal static string PythonCommand(string scriptPath) => $"python3 \"{scriptPath}\"";

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
```

- [ ] **Step 2: Write the failing helper tests**

Add to `IntegratorHelpersTests.cs` (it already has `_tempDir`, `HooksProperty` and `ReadInnerCommandsAsync`):

```csharp
    [Theory]
    [InlineData("python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py")]
    [InlineData("python3 .claude/hooks/dotnet-to-dtk.py")]
    [InlineData("python \"C:\\Users\\me\\.claude\\hooks\\dotnet-to-dtk.py\"")]
    [InlineData("\"C:\\Program Files\\Python\\python.exe\" \"$HOME\"/.claude/hooks/dotnet-to-dtk.py")]
    public async Task MergeJsonSettingsAsync_PythonHookRegistration_IsUpgradedInPlaceToTheDtkHook(string legacyCommand)
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, $$"""
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":{{System.Text.Json.JsonSerializer.Serialize(legacyCommand)}},"timeout":30}]}]}}
            """);

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude"), context, CancellationToken.None);

        (await ReadInnerCommandsAsync(path, "PreToolUse")).Should().ContainSingle().Which.Should().Be("dtk hook claude");
        (await File.ReadAllTextAsync(path)).Should().Contain("\"timeout\": 30", "an upgrade in place keeps the entry's other properties");
        context.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeJsonSettingsAsync_HookRunningAnotherScript_IsLeftAloneAndTheDtkHookAppended()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, """
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"python3 .claude/hooks/rtk-rewrite.py"}]}]}}
            """);

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "PreToolUse", "Bash", "dtk hook claude"), context, CancellationToken.None);

        (await ReadInnerCommandsAsync(path, "PreToolUse")).Should().Equal("python3 .claude/hooks/rtk-rewrite.py", "dtk hook claude");
    }

    [Fact]
    public async Task WriteHookRegistrationAsync_NewFile_WritesOnlyTheRegistration()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "settings.json");

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(path, "BeforeTool", "run_shell_command", "dtk hook gemini; exit 0"), context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be(
            """
            {
              "hooks": {
                "BeforeTool": [
                  {
                    "matcher": "run_shell_command",
                    "hooks": [
                      {
                        "type": "command",
                        "command": "dtk hook gemini; exit 0"
                      }
                    ]
                  }
                ]
              }
            }

            """);
        context.Created.Should().Equal(path);
        Directory.EnumerateFiles(_tempDir).Should().Equal(path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveLegacyHookScriptAsync_ScriptDtkWrote_IsDeletedWithItsEmptyDirectory(bool stamped)
    {
        var context = new IntegrationContext(false);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        if (stamped)
        {
            LegacyHookFixtures.WriteStampedScript(script);
        }
        else
        {
            LegacyHookFixtures.WriteUnstampedScript(script);
        }

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeFalse("an emptied hooks directory is removed too");
        context.Removed.Should().Equal(script);
        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_DirectoryWithOtherFiles_IsKept()
    {
        var context = new IntegrationContext(false);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "hooks", "dtk-dotnet.json"), "{}");

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_EditedScript_IsKeptWithANote()
    {
        var context = new IntegrationContext(false);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteEditedScript(script);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeTrue();
        context.Removed.Should().BeEmpty();
        context.Notes.Should().ContainSingle().Which.Should().Contain(script).And.Contain("no longer used").And.Contain("--force");
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_EditedScriptWithForce_IsDeleted()
    {
        var context = new IntegrationContext(true);
        var script = Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteEditedScript(script);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(script, context, CancellationToken.None);

        File.Exists(script).Should().BeFalse();
        context.Removed.Should().Equal(script);
    }

    [Fact]
    public async Task RemoveLegacyHookScriptAsync_NoScript_DoesNothing()
    {
        var context = new IntegrationContext(false);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(Path.Combine(_tempDir, "hooks", "dotnet-to-dtk.py"), context, CancellationToken.None);

        context.Removed.Should().BeEmpty();
        context.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteOwnedFileAsync_ReplaceExisting_OverwritesWithoutForce()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "dtk-dotnet.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "old");

        await IntegratorHelpers.WriteOwnedFileAsync(path, "new", replaceExisting: true, context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("new");
        context.Updated.Should().Equal(path);
    }

    [Fact]
    public async Task WriteOwnedFileAsync_NotReplaceable_SkipsWithoutForce()
    {
        var context = new IntegrationContext(false);
        var path = Path.Combine(_tempDir, "dtk-dotnet.json");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(path, "old");

        await IntegratorHelpers.WriteOwnedFileAsync(path, "new", replaceExisting: false, context, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("old");
        context.Skipped.Should().Equal(path);
    }

    [Fact]
    public void IntegrationContext_ToResult_CarriesRemovedFiles()
    {
        var context = new IntegrationContext(false);
        context.Removed.Add("/p/.claude/hooks/dotnet-to-dtk.py");

        context.ToResult().RemovedFiles.Should().Equal("/p/.claude/hooks/dotnet-to-dtk.py");
    }
```

Delete these tests, which pin the `$…_PROJECT_DIR` derivation being removed: `MergeJsonSettingsAsync_CommandStartingWithQuotedEnvVar_StillReplacesLegacyEntry`, `MergeJsonSettingsAsync_EmptyEnvVarName_StillReplacesLegacyEntry`, `MergeJsonSettingsAsync_CommandEndingWithClosingQuote_DerivesNoLegacyAndAppends`, `MergeJsonSettingsAsync_UnterminatedQuotedEnvVar_DerivesNoLegacyAndAppends`, `MergeJsonSettingsAsync_QuotedSegmentWithoutEnvVarSigil_DerivesNoLegacyAndAppends`. Keep every other test; the three `…Legacy…` tests that use `dotnet-to-dtk.py` commands still describe true behavior.

In `InitCommandTests.cs` add:

```csharp
    [Fact]
    public async Task ExecuteAsync_RemovedFiles_ArePrintedAndCountAsAChange()
    {
        const string dir = "/project";
        var result = new IntegrationResult([], [], []) { RemovedFiles = [$"{dir}/.claude/hooks/dotnet-to-dtk.py"] };
        var (command, console) = Create("claude", result);

        var exitCode = await command.RunAsync(new InitCommandSettings { Provider = "claude", Directory = dir }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("removed").And.Contain(".claude/hooks/dotnet-to-dtk.py");
        console.Output.Should().Contain("Done.").And.NotContain("Already integrated");
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~IntegratorHelpersTests"` and `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests"`
Expected: build errors on the new members.

- [ ] **Step 4: Add `RemovedFiles` and `Removed`**

In `IntegrationResult.cs`, after `UnchangedFiles`:

```csharp
    /// <summary>Gets the files dtk deleted because nothing uses them any more, such as a Python hook script.</summary>
    public IReadOnlyList<string> RemovedFiles { get; init; } = [];
```

In `IntegrationContext.cs`, after `Unchanged`:

```csharp
    /// <summary>Gets the list of file paths deleted during this integration run.</summary>
    internal List<string> Removed { get; } = [];
```

and in `ToResult`: `UnchangedFiles = Unchanged, RemovedFiles = Removed`.

- [ ] **Step 5: Implement the helpers**

In `IntegratorHelpers.cs`:

1. Next to `HookSpec`, add `internal sealed record HookRegistrationSpec(string SettingsPath, string EventKey, string Matcher, string Command);` with a `<summary>` ("A hook registration merged into a harness's settings file.") and `<param>` docs.
2. Add below `HookLegacySignature`:

```csharp
    /// <summary>File name of the Python hook script dtk installed before <c>dtk hook</c> replaced it.</summary>
    internal const string LegacyHookScriptName = "dotnet-to-dtk.py";
```

3. Split `WriteFileAsync` so both public shapes share one body:

```csharp
    internal static Task WriteFileAsync(string path, string content, IntegrationContext context, CancellationToken cancellationToken)
        => WriteOwnedFileAsync(path, content, replaceExisting: false, context, cancellationToken);

    /// <summary>
    /// <see cref="WriteFileAsync"/> for a file dtk owns outright: when <paramref name="replaceExisting"/> is
    /// <see langword="true"/>, a differing existing copy is overwritten without <c>--force</c>.
    /// </summary>
    /// <param name="path">Path to the target file.</param>
    /// <param name="content">The full content to write.</param>
    /// <param name="replaceExisting">Whether the caller has verified the existing copy is dtk's own.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteOwnedFileAsync(
        string path, string content, bool replaceExisting, IntegrationContext context, CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists)
        {
            var existing = await TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false);

            if (existing is not null
                && string.Equals(existing, content.ReplaceLineEndings("\n"), StringComparison.Ordinal))
            {
                context.Unchanged.Add(path);
                return;
            }
        }

        if (ShouldSkipWrite(exists, context.Force || replaceExisting))
        {
            context.Skipped.Add(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        (exists ? context.Updated : context.Created).Add(path);
    }
```

This is the existing `WriteFileAsync` body with `context.Force || replaceExisting` in place of `context.Force`. Keep the existing `<remarks>` on `WriteFileAsync`.

4. Add:

```csharp
    /// <summary>Merges a hook registration into the provider's settings JSON. No script is written.</summary>
    /// <param name="spec">Where and what to register.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static Task WriteHookRegistrationAsync(
        HookRegistrationSpec spec, IntegrationContext context, CancellationToken cancellationToken) =>
        MergeJsonSettingsAsync(
            spec.SettingsPath,
            spec.EventKey,
            new JsonObject
            {
                ["matcher"] = spec.Matcher,
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = spec.Command })
            },
            spec.Command,
            context,
            cancellationToken);

    /// <summary>
    /// Deletes the Python hook script dtk installed before <c>dtk hook</c>, when dtk can prove it wrote it.
    /// </summary>
    /// <remarks>
    /// Proof is the same as for refreshing a generated artifact: the provenance stamp verifies, or the file is
    /// unstamped and carries <see cref="HookLegacySignature"/>. Anything else may hold the user's own changes,
    /// so it is kept with a note unless <c>--force</c> is set. The directory is removed when this empties it.
    /// Call it only after the registration was written, so a settings file that fails to parse never costs
    /// the user a working hook.
    /// </remarks>
    /// <param name="scriptPath">Where the Python script would be.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task RemoveLegacyHookScriptAsync(
        string scriptPath, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            return;
        }

        var existing = await TryReadExistingAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        var writtenByDtk = existing is not null
                           && (ArtifactStamping.IsAuthentic(existing)
                               || (!ArtifactStamping.HasStamp(existing)
                                   && existing.Contains(HookLegacySignature, StringComparison.Ordinal)));

        if (!writtenByDtk && !context.Force)
        {
            context.Notes.Add(
                $"{scriptPath} is no longer used: the hook now runs dtk directly. It differs from what dtk "
                + "wrote, so it was left in place; delete it, or re-run with --force to remove it.");
            return;
        }

        File.Delete(scriptPath);
        context.Removed.Add(scriptPath);

        var directory = Path.GetDirectoryName(scriptPath);
        if (directory is not null && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
```

5. In `MergeJsonSettingsAsync`: delete `var legacyCommand = DeriveLegacyCommand(hookCommand);`, call `FindEquivalentEntries(hookArray, hookCommand)`, and change `FindEquivalentEntries` to take two parameters and match `AreEquivalentIgnoringQuotes(command, hookCommand) || command.Contains(LegacyHookScriptName, StringComparison.Ordinal)`. Delete `DeriveLegacyCommand`. Rewrite the `<summary>` passages that describe the `$…_PROJECT_DIR` legacy form: an entry is dtk's own when its command equals `hookCommand` ignoring `"` characters, or runs `dotnet-to-dtk.py` (any interpreter, any path — including a Windows user's hand edit to `python`), which `dtk hook` replaces.

- [ ] **Step 6: Print removed files in `InitCommand`**

After the `UnchangedFiles` loop:

```csharp
        foreach (var file in result.RemovedFiles)
        {
            console.MarkupLine($"[red]removed[/]  {Markup.Escape(RelativePath(directory, file))}");
        }
```

In `PrintSummary`, change the change test to `if (result.CreatedFiles.Count > 0 || result.UpdatedFiles.Count > 0 || result.RemovedFiles.Count > 0)`, and in its `<summary>` replace "The Python hook" with "a generated skill file".

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests` and `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~InitCommandTests"`
Expected: all pass. The Claude, Gemini and Copilot integrator tests still pass: their Python commands contain `dotnet-to-dtk.py`, so they still match themselves.

- [ ] **Step 8: Commit**

```bash
git add -A src tests
git commit -m "feat: add hook migration helpers and report removed files

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 7: Integrators register `dtk hook`; `doctor` checks it

`HookInstallation` is the contract between the installers and `doctor`, so both change in this task.

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`, `GeminiCliIntegrator.cs`, `CopilotCliIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (delete `HookSpec` and `WriteHookAndSettingsAsync`)
- Modify: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` (rewrite)
- Modify: `.github/copilot-instructions.md` (regenerated section)
- Modify: `docs/superpowers/specs/2026-09-14-native-hook-and-init-design.md` (presence rule)
- Test: `ClaudeCodeIntegratorTests.cs`, `GeminiCliIntegratorTests.cs`, `CopilotCliIntegratorTests.cs`, `HookDescriptionTests.cs`, `IntegratorHelpersTests.cs`, `HookHealthCheckerTests.cs` (rewrite)

**Interfaces:**
- Consumes: `HookCommands.Invocation`, `HookCommands.FailOpen`, `HookCommands.Verb` (Task 3); `IntegratorHelpers.WriteHookRegistrationAsync`, `RemoveLegacyHookScriptAsync`, `WriteOwnedFileAsync`, `LegacyHookScriptName`, `HookRegistrationSpec` (Task 6); `LegacyHookFixtures` (Task 6).
- Produces:

```csharp
/// <summary>One installed (or installable) rewrite hook, described once for both installer and diagnostics.</summary>
/// <param name="ProviderName">The provider this hook belongs to, which is also the <c>dtk hook</c> argument (e.g. "claude").</param>
/// <param name="Scope">Whether this describes the project or the home-config install.</param>
/// <param name="RegistrationPath">
/// The JSON file that registers the hook with the host CLI — a merged <c>settings.json</c> for Claude Code and
/// Gemini CLI, a dedicated <c>dtk-dotnet.json</c> for Copilot CLI.
/// </param>
/// <param name="Command">The exact command dtk registers, e.g. <c>dtk hook gemini; exit 0</c>.</param>
/// <param name="LegacyScriptPath">Where dtk installed the Python hook this registration replaces.</param>
/// <param name="PayloadKind">Payload shape to use when probing this hook.</param>
internal sealed record HookInstallation(
    string ProviderName,
    HookScope Scope,
    string RegistrationPath,
    string Command,
    string LegacyScriptPath,
    HookPayloadKind PayloadKind);
```

#### Part A — installers

- [ ] **Step 1: Write the failing integrator tests**

`ClaudeCodeIntegratorTests.cs`:
- `IntegrateAsync_FreshDirectory_CreatesAllThreeFiles` → rename `…_CreatesSkillAndSettings`; expect `CreatedFiles` count 2 and `File.Exists(<.claude/hooks/dotnet-to-dtk.py>)` false.
- `IntegrateAsync_SecondRun_NoForce_ReportsAllFilesUnchanged` and `…_WithForce_…`: expected unchanged count 3 → 2; drop assertions naming the hook script.
- Delete `IntegrateAsync_HookScript_ContainsPythonRewriteLogic`, `IntegrateAsync_WritesHookEmittingUpdatedInputSchemaAsync`, `IntegrateAsync_HookScript_MatchesCommittedRepoHook` (payload behavior is covered by `HookPayloadsTests`).
- `IntegrateAsync_SettingsJson_ContainsHookEntry`, `IntegrateAsync_SettingsJson_RegistersHookViaClaudeProjectDirEnvVar` → one test, `IntegrateAsync_SettingsJson_RegistersTheDtkHook`, asserting the single `PreToolUse` → `Bash` inner command is exactly `dtk hook claude`.
- `IntegrateAsync_SkillFile_DocumentsWindowsPythonCaveat` → `IntegrateAsync_SkillFile_DoesNotMentionPython`: `content.Should().NotContain("python", "the hook no longer needs an interpreter")` using a case-insensitive check (`content.ToLowerInvariant()`).
- `IntegrateGlobalAsync_FreshHome_CreatesAllThreeFilesUnderHomeClaudeDir` → two files; `IntegrateGlobalAsync_RegistersHomeRootedHookCommand` → `IntegrateGlobalAsync_RegistersTheSameDtkHookInHomeSettings` asserting `dtk hook claude` in `~/.claude/settings.json`.
- `IntegrateAsync_ReposCommittedSettingsWithWholePathQuotedHookVariant_UpgradesToSingleEntry`: expect the single surviving command to be `dtk hook claude`.
- Add:

```csharp
    [Fact]
    public async Task IntegrateAsync_PythonInstall_MigratesTheRegistrationAndRemovesTheScript()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        var settings = Path.Combine(_tempDir, ".claude", "settings.json");
        await File.WriteAllTextAsync(settings, $$"""
            {"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py"}]}]}}
            """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(settings)).Should().Contain("\"command\": \"dtk hook claude\"").And.NotContain("python3");
        File.Exists(script).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(script)).Should().BeFalse();
        result.RemovedFiles.Should().Equal(script);
        result.UpdatedFiles.Should().Contain(settings);
    }

    [Fact]
    public async Task IntegrateAsync_EditedPythonScript_IsKeptWithANoteUnlessForced()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteEditedScript(script);

        var kept = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);
        File.Exists(script).Should().BeTrue();
        kept.Notes.Should().Contain(note => note.Contains(script, StringComparison.Ordinal));

        var forced = await _sut.IntegrateAsync(_tempDir, true, CancellationToken.None);
        File.Exists(script).Should().BeFalse();
        forced.RemovedFiles.Should().Equal(script);
    }

    [Fact]
    public async Task IntegrateAsync_MalformedSettings_ThrowsAndKeepsThePythonScript()
    {
        var script = Path.Combine(_tempDir, ".claude", "hooks", "dotnet-to-dtk.py");
        LegacyHookFixtures.WriteStampedScript(script);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"), "{ not json");

        var act = () => _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(script).Should().BeTrue("the working Python hook must survive a settings file dtk could not update");
    }
```

(`LegacyHookFixtures` creates `.claude/hooks`, so `.claude` exists for the settings writes.)

`GeminiCliIntegratorTests.cs`: apply the same pattern — fresh directory creates 2 files (`GEMINI.md`, `settings.json`); second-run counts drop the script; delete `IntegrateAsync_HookScript_ContainsPythonRewriteLogic` and `IntegrateAsync_HookScript_KeepsGeminiSchemaDistinctFromClaude`; `IntegrateAsync_SettingsJson_ContainsBeforeToolHook` and `…RegistersHookViaGeminiProjectDirEnvVar` → `IntegrateAsync_SettingsJson_RegistersTheFailOpenDtkHook` asserting exactly `dtk hook gemini; exit 0`; `IntegrateAsync_GeminiMd_DocumentsWindowsPythonCaveat` → `IntegrateAsync_GeminiMd_DoesNotMentionPython`; `IntegrateGlobalAsync_RegistersHomeRootedHookCommand` → asserts `dtk hook gemini; exit 0` in `~/.gemini/settings.json`; `…WholePathQuotedHookVariant_UpgradesToSingleEntry` → survivor `dtk hook gemini; exit 0`. Add `IntegrateAsync_PythonInstall_MigratesTheRegistrationAndRemovesTheScript` exactly as the Claude one with `.gemini`, `BeforeTool`, `run_shell_command`, `$GEMINI_PROJECT_DIR`, and `dtk hook gemini; exit 0`.

`CopilotCliIntegratorTests.cs`:
- `IntegrateAsync_FreshDirectory_CreatesAllThreeFiles` → `…_CreatesRegistrationAndInstructions`: 2 created, `HookScriptPath` absent.
- Delete `IntegrateAsync_HookScript_ContainsCopilotDecisionSchema`.
- `IntegrateAsync_HookJson_HasCopilotPreToolUseShape`: the single entry has `type` `command`, `matcher` `bash`, `bash` and `powershell` both `dtk hook copilot-cli; exit 0`, `timeoutSec` 10, and no `cwd` key (`entry.AsObject().ContainsKey("cwd").Should().BeFalse()`).
- `IntegrateGlobalAsync_HookJson_UsesAbsoluteHooksDirCwd` → `IntegrateGlobalAsync_HookJson_IsTheSameRegistration`: global JSON equals the project JSON byte for byte.
- `…SecondRun_NoForce_SkipsInstructionsAndReportsRestUnchanged` / `…WithForce…` / `IntegrateGlobalAsync_FreshHome_CreatesHookArtifactsOnly`: drop the script from expected sets and counts.
- Add:

```csharp
    [Fact]
    public async Task IntegrateAsync_PythonEraRegistration_IsReplacedWithoutForceAndTheScriptRemoved()
    {
        LegacyHookFixtures.WriteStampedScript(HookScriptPath);
        await File.WriteAllTextAsync(HookJsonPath, """
            {"version":1,"hooks":{"preToolUse":[{"type":"command","matcher":"bash","bash":"python3 dotnet-to-dtk.py","cwd":".github/hooks","timeoutSec":10}]}}
            """);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Contain("dtk hook copilot-cli; exit 0").And.NotContain("python3");
        result.UpdatedFiles.Should().Contain(HookJsonPath);
        result.RemovedFiles.Should().Equal(HookScriptPath);
        Directory.Exists(Path.GetDirectoryName(HookJsonPath)).Should().BeTrue("the registration still lives there");
    }

    [Fact]
    public async Task IntegrateAsync_ForeignHookJson_IsSkippedWithoutForce()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HookJsonPath)!);
        const string foreign = """{"version":1,"hooks":{"preToolUse":[{"type":"command","bash":"./my-own-check.sh"}]}}""";
        await File.WriteAllTextAsync(HookJsonPath, foreign);

        var result = await _sut.IntegrateAsync(_tempDir, false, CancellationToken.None);

        (await File.ReadAllTextAsync(HookJsonPath)).Should().Be(foreign);
        result.SkippedFiles.Should().Contain(HookJsonPath);
    }
```

`HookDescriptionTests.cs` — replace the three tests:

```csharp
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
            new CopilotCliIntegrator(home)
        };

        foreach (var integrator in integrators)
        {
            var projectDir = Path.Combine(_tempDir, ((IProviderIntegrator)integrator).ProviderName);
            await ((IProviderIntegrator)integrator).IntegrateAsync(projectDir, force: false, default);

            foreach (var installation in integrator.DescribeHooks(projectDir, HookScope.Project))
            {
                (await File.ReadAllTextAsync(installation.RegistrationPath)).Should().Contain(installation.Command);
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
            ["copilot-cli"] = "dtk hook copilot-cli; exit 0"
        };
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home.ClaudeDir, rtkConfigPath), home),
            new GeminiCliIntegrator(home),
            new CopilotCliIntegrator(home)
        };

        foreach (var installation in integrators.SelectMany(i => i.DescribeHooks(_tempDir, HookScope.Project)))
        {
            installation.Command.Should().Be(expected[installation.ProviderName]);
            Path.GetFileName(installation.LegacyScriptPath).Should().Be(IntegratorHelpers.LegacyHookScriptName);
        }
    }
```

In `IntegratorHelpersTests.cs`, delete `WriteHookAndSettingsAsync_NewFiles_WritesScriptAndExactSettingsJson` (Task 6's `WriteHookRegistrationAsync_NewFile_WritesOnlyTheRegistration` replaces it).

- [ ] **Step 2: Run them to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~ClaudeCodeIntegratorTests|FullyQualifiedName~GeminiCliIntegratorTests|FullyQualifiedName~CopilotCliIntegratorTests|FullyQualifiedName~HookDescriptionTests"`
Expected: build errors (`installation.Command`, `LegacyScriptPath`).

- [ ] **Step 3: Reshape `HookInstallation`**

Replace the record in `IHookIntegrator.cs` with the one under **Interfaces** above.

- [ ] **Step 4: Switch the Claude Code integrator**

In `ClaudeCodeIntegrator.cs` (add `using DotnetTokenKiller.Application.Integration.Hooks;`):
- Delete the `HookCommand` and `GlobalHookCommand` constants.
- In `SkillMarkdown` delete the bullet ``- The PreToolUse hook shells out to `python3`; …``.
- In the class `<remarks>` list, replace the `.claude/hooks/dotnet-to-dtk.py` item with `<c>.claude/settings.json</c> registering <c>dtk hook claude</c> (merged, never overwritten); a Python hook left by an older dtk is migrated`, and drop the separate settings item.
- Replace `DescribeHooks`, the two public entry points and `IntegrateCoreAsync`:

```csharp
    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var baseDirectory = scope == HookScope.Global ? home.ClaudeDir : Path.Combine(directory, ".claude");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(baseDirectory, "settings.json"),
                HookCommands.Invocation(ProviderName),
                Path.Combine(baseDirectory, "hooks", IntegratorHelpers.LegacyHookScriptName),
                HookPayloadKind.ClaudeCode)
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(Path.Combine(directory, ".claude"), directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.ClaudeDir, home.Home, HookScope.Global, force, cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string baseDirectory,
        string hookDirectory,
        HookScope scope,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteGeneratedFileAsync(
            new GeneratedArtifact(
                Path.Combine(baseDirectory, "skills", "dotnet-token-killer", "SKILL.md"),
                SkillMarkdown,
                StampStyle.HtmlComment,
                SkillLegacySignature),
            context, cancellationToken).ConfigureAwait(false);

        var hook = DescribeHooks(hookDirectory, scope)[0];

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(hook.RegistrationPath, "PreToolUse", "Bash", hook.Command),
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(hook.LegacyScriptPath, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileAsync(hookDirectory, cancellationToken).ConfigureAwait(false);
        if (rtkOutcome.CreatedConfigPath is not null)
        {
            context.Created.Add(rtkOutcome.CreatedConfigPath);
        }

        if (rtkOutcome.UpdatedConfigPath is not null)
        {
            context.Updated.Add(rtkOutcome.UpdatedConfigPath);
        }

        context.Notes.AddRange(rtkOutcome.Notes);

        return context.ToResult();
    }
```

(The old code passed the same value as `hookDirectory` and `reconcileDir` in both scopes, so they merge into one parameter.)

- [ ] **Step 5: Switch the Gemini CLI integrator**

In `GeminiCliIntegrator.cs` (add the `Hooks` using):
- Delete `HookCommand` and `GlobalHookCommand`.
- In `GeminiSection` delete the two-line paragraph starting ``The `BeforeTool` hook shells out to `python3`;``, leaving `{IntegrationInstructions.Markdown}` followed directly by `{SectionEndMarker}`.
- `<remarks>` list: replace the `.gemini/hooks/dotnet-to-dtk.py` item with `<c>.gemini/settings.json</c> registering <c>dtk hook gemini; exit 0</c> (merged, never overwritten); a Python hook left by an older dtk is migrated`, and drop the separate settings item.
- `DescribeHooks` returns `new HookInstallation(ProviderName, scope, Path.Combine(geminiDir, "settings.json"), HookCommands.FailOpen(ProviderName), Path.Combine(geminiDir, "hooks", IntegratorHelpers.LegacyHookScriptName), HookPayloadKind.GeminiCli)`.
- Remove the `hookCommand` parameter from `IntegrateCoreAsync` and both callers; after the section write:

```csharp
        var hook = DescribeHooks(hookDirectory, scope)[0];

        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(hook.RegistrationPath, "BeforeTool", "run_shell_command", hook.Command),
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(hook.LegacyScriptPath, context, cancellationToken)
            .ConfigureAwait(false);
```

- [ ] **Step 6: Switch the Copilot CLI integrator**

In `CopilotCliIntegrator.cs` (add the `Hooks` using):
- Delete `HookScriptName`, `HookCwdRelative`.
- `CopilotSection` paragraph becomes:

```
        A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
        `dotnet {IntegrationInstructions.SubcommandAlternation}` to `dtk dotnet ...` automatically.
```

- `<remarks>` list: drop the script item; the JSON item reads `<c>.github/hooks/dtk-dotnet.json</c> — a dtk-owned registration running <c>dtk hook copilot-cli</c>; a Python hook left by an older dtk is migrated`.
- Replace `DescribeHooks`, the hook-writing methods and `BuildHookJson`:

```csharp
    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var hooksDir = scope == HookScope.Global ? home.CopilotHooksDir : Path.Combine(directory, ".github", "hooks");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(hooksDir, HookJsonName),
                HookCommands.FailOpen(ProviderName),
                Path.Combine(hooksDir, IntegratorHelpers.LegacyHookScriptName),
                HookPayloadKind.CopilotCli)
        ];
    }

    private async Task WriteHookArtifactsAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var hook = DescribeHooks(directory, scope)[0];
        var replaceable = await IsDtkRegistrationAsync(hook.RegistrationPath, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteOwnedFileAsync(
            hook.RegistrationPath, BuildHookJson(hook.Command), replaceable, context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.RemoveLegacyHookScriptAsync(hook.LegacyScriptPath, context, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether an existing <c>dtk-dotnet.json</c> holds only dtk's own hooks — the Python-era
    /// <c>dotnet-to-dtk.py</c> command or <c>dtk hook copilot-cli</c> — so it can be replaced without <c>--force</c>.
    /// </summary>
    /// <param name="path">The registration file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<bool> IsDtkRegistrationAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            if (root?["hooks"]?["preToolUse"] is not JsonArray { Count: > 0 } entries)
            {
                return false;
            }

            return entries.All(IsDtkHookEntry);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Whether a <c>preToolUse</c> entry runs a command, and every command it runs is dtk's.</summary>
    /// <param name="entry">One element of <c>hooks.preToolUse</c>.</param>
    private bool IsDtkHookEntry(JsonNode? entry)
    {
        if (entry is not JsonObject hook)
        {
            return false;
        }

        var invocation = HookCommands.Invocation(ProviderName);
        var sawCommand = false;
        foreach (var key in new[] { "bash", "powershell", "command" })
        {
            if (hook[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                continue;
            }

            if (!text.Contains(IntegratorHelpers.LegacyHookScriptName, StringComparison.Ordinal)
                && !text.Contains(invocation, StringComparison.Ordinal))
            {
                return false;
            }

            sawCommand = true;
        }

        return sawCommand;
    }

    private static string BuildHookJson(string command)
    {
        var root = new JsonObject
        {
            ["version"] = 1,
            ["hooks"] = new JsonObject
            {
                ["preToolUse"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["matcher"] = "bash",
                        ["bash"] = command,
                        ["powershell"] = command,
                        ["timeoutSec"] = 10
                    })
            }
        };

        // Normalize to '\n'; WriteIndented emits '\r\n' on Windows.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n");
    }
```

`InvalidOperationException` covers `root["hooks"]` when the root is a JSON array. `BuildHookJson` keeps its existing per-call `JsonSerializerOptions`, which the analyzers already accept.

`IntegrateAsync` calls `WriteHookArtifactsAsync(directory, HookScope.Project, context, cancellationToken)`; `IntegrateGlobalAsync` calls `WriteHookArtifactsAsync(home.Home, HookScope.Global, context, cancellationToken)` and drops the comment about an absolute `cwd`.

- [ ] **Step 7: Delete the old hook writer**

In `IntegratorHelpers.cs` delete `HookSpec` and `WriteHookAndSettingsAsync`. The build must now reference `HookScriptTemplates` nowhere under `src/` except its own file: `rtk proxy grep -rn "HookScriptTemplates" src --include=*.cs`.

- [ ] **Step 8: Regenerate this repo's Copilot instructions**

`SubcommandBindingTests.RepoCopilotInstructions_MatchesTheGeneratedSection` pins `.github/copilot-instructions.md` to `CopilotSection`. Regenerate it from the JIT build into a scratch directory and copy the file over:

```bash
dtk dotnet build DotnetTokenKiller.slnx
scratch=$(mktemp -d)
dotnet src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk.dll init copilot-cli --dir "$scratch"
cp "$scratch/.github/copilot-instructions.md" .github/copilot-instructions.md
git diff .github/copilot-instructions.md
```

Expected diff: only the `preToolUse` paragraph changes.

#### Part B — `doctor`

- [ ] **Step 9: Rewrite the health checker tests**

Replace the test methods of `HookHealthCheckerTests.cs` (keep the class fields, constructor, `Dispose`, `RewrittenPayload`, `Home`, `Integrators`, `IntegrateAsync`, `ScopeLabel`; delete `ReplaceStringValue` and `TryReplace` if nothing else uses them; add `using DotnetTokenKiller.Application.Tests.Integration;`):

```csharp
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
            Arg.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "hook", "gemini" })),
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
```

Deleted by this rewrite: `RunAsync_ScriptStale_…`, `RunAsync_ScriptEditedLocally_…`, `RunAsync_ScriptPresentButNotRegistered_…`, `RunAsync_ScriptUnreadable_…`, `RunAsync_HookDoesNotRewrite_ProbeFailsAndNamesTheSubcommand`, `RunAsync_InterpreterMissing_…`, `RunAsync_EditedInterpreterInRegistration_…`, `RunAsync_QuotedInterpreterPathWithSpace_…`, `RunAsync_ScriptMovedOrDeletedButStillRegistered_…`.

- [ ] **Step 10: Run them to verify they fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookHealthCheckerTests"`
Expected: build errors or failures in the old checker (it reads `installation.Script`).

- [ ] **Step 11: Rewrite `HookHealthChecker`**

Replace the body of `HookHealthChecker.cs` from the class `<summary>` down (keep the usings; add `using DotnetTokenKiller.Application.Integration.Hooks;`, drop `using System.Runtime.CompilerServices;` if unused):

```csharp
/// <summary>
/// Checks that the rewrite hooks dtk installed are registered and that the <c>dtk</c> on <c>PATH</c> answers them.
/// </summary>
/// <remarks>
/// A registration is only a command string, <c>dtk hook &lt;provider&gt;</c>; what breaks silently is the
/// <c>dtk</c> it names — missing from <c>PATH</c>, or too old to know <c>hook</c>. The probe runs exactly that.
/// A registration still naming the Python <c>dotnet-to-dtk.py</c> script is an install from before
/// <c>dtk hook</c>, reported with the command that migrates it.
/// </remarks>
/// <param name="runner">Runs the installed hook for the probe.</param>
internal sealed class HookHealthChecker(ICommandRunner runner)
{
    /// <summary>How long the probe waits before declaring the hook wedged.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private const string UpdateCommand = "dotnet tool update -g DotnetTokenKiller";

    /// <summary>Runs the status check and probe for every hook installed in either scope.</summary>
    /// <param name="integrators">The hook-installing providers to inspect.</param>
    /// <param name="projectDirectory">The directory to treat as the project root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(
        IReadOnlyList<IHookIntegrator> integrators,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrators);

        var checks = new List<DiagnosticCheck>();

        foreach (var integrator in integrators)
        {
            foreach (var scope in new[] { HookScope.Project, HookScope.Global })
            {
                foreach (var installation in integrator.DescribeHooks(projectDirectory, scope))
                {
                    var registration = await ReadRegistrationAsync(installation, cancellationToken).ConfigureAwait(false);
                    if (registration.Kind == RegistrationKind.Absent && !File.Exists(installation.LegacyScriptPath))
                    {
                        continue;
                    }

                    checks.AddRange(await CheckAsync(installation, registration, cancellationToken).ConfigureAwait(false));
                }
            }
        }

        if (checks.Count == 0)
        {
            checks.Add(new DiagnosticCheck(
                "hook integration",
                true,
                "No rewrite hook found in this directory or your home config. "
                + "Run 'dtk init <provider>' to install one."));
        }

        return checks;
    }

    private async Task<IReadOnlyList<DiagnosticCheck>> CheckAsync(
        HookInstallation installation, Registration registration, CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook");

        return registration.Kind switch
        {
            RegistrationKind.Legacy =>
            [
                new DiagnosticCheck(
                    name,
                    false,
                    $"legacy Python hook — {installation.RegistrationPath} still runs {IntegratorHelpers.LegacyHookScriptName}. "
                    + $"Run '{RemedyCommand(installation)}' to migrate it to '{installation.Command}'.")
            ],
            RegistrationKind.Current =>
            [
                new DiagnosticCheck(name, true, "registered"),
                await ProbeAsync(installation, cancellationToken).ConfigureAwait(false)
            ],
            _ => [new DiagnosticCheck(name, false, $"{registration.Problem}. Run '{RemedyCommand(installation)}'.")]
        };
    }

    /// <summary>What the registration file says about this hook.</summary>
    private enum RegistrationKind
    {
        /// <summary>No file, or a file with no dtk hook in it.</summary>
        Absent,

        /// <summary>A file dtk could not read or parse.</summary>
        Unreadable,

        /// <summary>A registration still running the Python script.</summary>
        Legacy,

        /// <summary>A registration running <c>dtk hook &lt;provider&gt;</c>.</summary>
        Current
    }

    /// <summary>The classification of one registration file.</summary>
    /// <param name="Kind">What the file holds.</param>
    /// <param name="Problem">Why no current registration was found, for <see cref="RegistrationKind.Absent"/> and <see cref="RegistrationKind.Unreadable"/>.</param>
    private sealed record Registration(RegistrationKind Kind, string Problem);

    /// <summary>
    /// Classifies the registration by searching every string in its JSON — which spans Claude Code's and
    /// Gemini CLI's nested <c>hooks[event][].hooks[].command</c> and Copilot CLI's <c>hooks.preToolUse[].bash</c>
    /// with no per-provider branching.
    /// </summary>
    private static async Task<Registration> ReadRegistrationAsync(
        HookInstallation installation, CancellationToken cancellationToken)
    {
        var path = installation.RegistrationPath;
        if (!File.Exists(path))
        {
            return new Registration(RegistrationKind.Absent, $"not registered — {path} does not exist");
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Registration(RegistrationKind.Unreadable, $"{path} could not be read: {ex.Message}");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            return new Registration(RegistrationKind.Unreadable, $"{path} could not be read as JSON: {ex.Message}");
        }

        if (FindStringContaining(root, IntegratorHelpers.LegacyHookScriptName) is not null)
        {
            return new Registration(RegistrationKind.Legacy, string.Empty);
        }

        return FindStringContaining(root, HookCommands.Invocation(installation.ProviderName)) is not null
            ? new Registration(RegistrationKind.Current, string.Empty)
            : new Registration(RegistrationKind.Absent, $"not registered — no entry in {path} runs '{installation.Command}'");
    }
```

`RunAsync` skips an installation only when the registration is `Absent` and no legacy script exists, so an unreadable or malformed file is reported (spec amendment in Step 13) while a settings file with no dtk hook is not. Keep `FindStringContaining`, `CheckName` and `RemedyCommand` (already `dtk init` from Task 5) as they are. Replace `ProbeAsync`, and delete `BuildStatusCheckAsync`, `ResolveInterpreter`, `EnumerateCandidatesAsync`, `CheckInstallationAsync` and `HookInstallationCandidate`:

```csharp
    /// <summary>Feeds a payload through the <c>dtk</c> on <c>PATH</c> and asserts every subcommand is rewritten.</summary>
    /// <param name="installation">The installation to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<DiagnosticCheck> ProbeAsync(HookInstallation installation, CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook probe");
        var hook = HookCommands.Invocation(installation.ProviderName);
        var command = string.Join("; ", DotnetSubcommands.Ordered.Select(sub => $"dotnet {sub}"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var result = await runner
                .RunCapturedWithInputAsync(
                    "dtk",
                    [HookCommands.Verb, installation.ProviderName],
                    BuildPayload(installation.PayloadKind, command),
                    timeout.Token)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new DiagnosticCheck(
                    name,
                    false,
                    $"'{hook}' exited with code {result.ExitCode}: {result.StdErr.Trim()}. "
                    + $"A dtk older than 'dtk hook' cannot answer it; run '{UpdateCommand}'.");
            }

            var missing = DotnetSubcommands.Ordered
                .Where(sub => !result.StdOut.Contains($"dtk dotnet {sub}", StringComparison.Ordinal))
                .ToList();

            return missing.Count == 0
                ? new DiagnosticCheck(name, true, $"rewrites all {DotnetSubcommands.Ordered.Count} subcommands")
                : new DiagnosticCheck(
                    name,
                    false,
                    $"does not rewrite: {string.Join(", ", missing)}. The dtk on PATH is older than this one; run '{UpdateCommand}'.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DiagnosticCheck(name, false, $"'{hook}' did not respond within {ProbeTimeout.TotalSeconds:F0}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck(
                name,
                false,
                $"could not run dtk: {ex.Message}. The harness runs 'dtk' from PATH; add the .NET tools directory (~/.dotnet/tools) to it.");
        }
    }
```

Keep `BuildPayload` unchanged.

- [ ] **Step 12: Run the Application tests**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`
Expected: all pass, including `SubcommandBindingTests.RepoCopilotInstructions_MatchesTheGeneratedSection`. Also run `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DoctorCommandTests|FullyQualifiedName~InitCommandTests|FullyQualifiedName~CommandDispatchIntegrationTests"`; if a `DoctorCommandTests` assertion names the old script or `python3`, update it to the new messages.

- [ ] **Step 13: Amend the spec's presence rule**

In the spec, section 5, replace the **Presence** bullet with:

```markdown
- **Presence.** An installation is reported when its registration names `dtk hook <provider>` or
  `dotnet-to-dtk.py`, when the legacy script exists, or when the registration file exists but cannot be
  read or parsed (a broken settings file is worth reporting, and dtk cannot tell whether it holds a hook).
  A readable settings file with no dtk hook is not reported.
```

- [ ] **Step 14: Commit**

```bash
git add -A src tests .github/copilot-instructions.md docs/superpowers/specs/2026-09-14-native-hook-and-init-design.md
git commit -m "feat: register dtk hook instead of a Python script, migrate old installs, probe the dtk on PATH

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 8: Delete the Python hook generator

**Files:**
- Delete: `src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptTemplatesTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptExecutionTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterDifferentialTests.cs`
- Modify: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`, `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`, `src/DotnetTokenKiller.Application/Integration/ArtifactStamping.cs`, `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (doc comments)

**Interfaces:**
- Consumes: Task 7 left no `src/` reference to `HookScriptTemplates` outside its own file.
- Produces: no Python anywhere under `src/`. `.claude/hooks/dotnet-to-dtk.py` stays committed and frozen (spec section 7).

- [ ] **Step 1: Delete the files**

```bash
git rm src/DotnetTokenKiller.Application/Integration/HookScriptTemplates.cs \
  tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptTemplatesTests.cs \
  tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptExecutionTests.cs \
  tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterDifferentialTests.cs
```

- [ ] **Step 2: Remove the tests that pinned the generator**

In `SubcommandBindingTests.cs` delete `GeneratedHooks_DeclareExactlyTheCanonicalSubcommands`, `GeneratedHooks_DocumentEveryCanonicalSubcommand`, `RepoClaudeHook_MatchesTheGeneratedHook` and `RepoClaudeHook_CarriesAVerifiableStamp`, then remove usings that become unused. In the comment inside `RepoCopilotInstructions_MatchesTheGeneratedSection`, replace "Unlike '.claude/hooks/dotnet-to-dtk.py', this file has no test binding it to its generator, so" with "Nothing else binds this file to its generator, so".

Add one test in their place, so the rewrite stays bound to the canonical list:

```csharp
    [Fact]
    public void HookRewrite_CoversExactlyTheCanonicalSubcommands()
    {
        // Pinned to literals rather than derived from DotnetSubcommands.Ordered, so a subcommand added there
        // must be added here too — and this test proves the hook rewrites it.
        string[] expected = ["build", "test", "restore", "clean", "format", "list package"];

        DotnetSubcommands.Ordered.Should().Equal(expected);
        foreach (var sub in expected)
        {
            DotnetCommandRewriter.Rewrite($"dotnet {sub}").Should().Be($"dtk dotnet {sub}");
        }
    }
```

(add `using DotnetTokenKiller.Application.Integration.Hooks;`).

- [ ] **Step 3: Fix the doc comments that describe the generator**

- `DotnetSubcommands.cs` class summary: "the generated agent hook scripts all derive from it" → "and the `dtk hook` rewrite all derive from it". In the `<remarks>` list, delete the item "Regenerate `.claude/hooks/dotnet-to-dtk.py` from `HookScriptTemplates.ClaudeHook` (e.g. run `dtk init claude` …)". In the item listing pinned literals, add `SubcommandBindingTests.HookRewrite_CoversExactlyTheCanonicalSubcommands`. `Sorted`'s summary: "Generated artifacts (the Python hook's subcommand tuple) use this so their bytes stay stable…" → "Ordinal alphabetical order. The hook rewrite tries subcommands longest first and, among equal lengths, in this order."
- `ArtifactStamping.cs`: `StampStyle.HashComment` summary → "A <c>#</c> line comment, as the Python hook scripts dtk installed before <c>dtk hook</c> carried."; in the class `<remarks>`, replace the sentence about "this repo's committed `.claude/hooks/dotnet-to-dtk.py`" with "a version would change a generated file on every release for reasons unrelated to its content."; in the paragraph about the stamp being the last line, replace "below a Python shebang in one artifact and below YAML frontmatter in another" with "below YAML frontmatter".
- `IntegratorHelpers.cs`: `HookLegacySignature` summary → "Substring present in every generation of the Python hook dtk installed before <c>dtk hook</c>, used to prove an unstamped copy is dtk's before deleting it."; `WriteGeneratedFileAsync` `<remarks>` "hand-edited an unstamped hook" → "hand-edited an unstamped artifact".

Then run `rtk proxy grep -rn -i "python" src --include=*.cs` and fix any remaining sentence that describes current behavior (mentions of the legacy script as a migration source are correct and stay).

- [ ] **Step 4: Build and test**

Run: `dtk dotnet build DotnetTokenKiller.slnx` and `dtk dotnet test tests/DotnetTokenKiller.Application.Tests` and `dtk dotnet test tests/DotnetTokenKiller.Domain.Tests`
Expected: build clean, all pass.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "refactor: delete the Python hook generator

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 9: Documentation

**Files:**
- Modify: `README.md`, `src/DotnetTokenKiller.Cli/README.md`, `docfx/index.md`, `docfx/articles/getting-started.md`, `docfx/articles/usage.md`, `docfx/articles/ai-agent-setup.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: the behavior of Tasks 4–7.
- Produces: docs that say `dtk init`, need no Python, and show `dtk hook` registrations. `DocsBindingTests` must stay green.

- [ ] **Step 1: README and the packaged README**

In `README.md` and `src/DotnetTokenKiller.Cli/README.md`, replace every `dtk integrate` with `dtk init` (keep the column alignment in the code blocks: `dtk init claude      --global   # ~/.claude`). In the README provider table:
- Claude Code row: `Skill file, PreToolUse hook running dtk hook claude, settings merge`.
- Copilot CLI row: `preToolUse hook in .github/hooks/ running dtk hook copilot-cli + instructions section`.
- Gemini row: `BeforeTool hook running dtk hook gemini, settings merge, GEMINI.md section`.
- JetBrains row: the line carries a duplicated cell (`| \`dtk integrate jetbrains\`   | Section in \`.junie/guidelines.md\`   |` twice); leave one.

After the table, add:

```markdown
`dtk integrate` still works as an alias of `dtk init`. The hooks run `dtk` itself, so they need nothing
else on `PATH` — no Python, no `jq`. Re-running `dtk init <provider>` on a project set up by an older dtk
replaces its Python hook registration and deletes the `dotnet-to-dtk.py` script dtk wrote there.
```

- [ ] **Step 2: `docfx/index.md`, `getting-started.md`, `usage.md`**

- `index.md`: `<code>dtk integrate claude</code>` → `<code>dtk init claude</code>`.
- `getting-started.md`: delete the prerequisite bullet "**Python 3** (optional): …"; the commands table row becomes `| \`dtk init\`           | Install AI agent integration artifacts (alias: \`dtk integrate\`) |`; replace the troubleshooting section "### `dtk integrate claude` / `dtk integrate gemini` fails" (heading through its code block) with:

````markdown
### The agent's hook does not rewrite `dotnet` commands

The hooks run `dtk hook <provider>`, so the agent must find `dtk` on its `PATH`. Check with:

```sh
dtk doctor
```

A failed `hook probe` names the cause: `dtk` missing from `PATH` (add `~/.dotnet/tools`), or a `dtk`
too old to know `hook` (`dotnet tool update -g DotnetTokenKiller`). A `legacy Python hook` means the
project was set up by an older dtk; run `dtk init <provider>` to migrate it.
````

- `usage.md`: heading `### \`dtk integrate\`` → `### \`dtk init\``; every example line `dtk integrate …` → `dtk init …` with aligned comments; after the examples add "`dtk integrate` is an alias of `dtk init`."

- [ ] **Step 3: `ai-agent-setup.md`**

- Intro: "One `dtk integrate` command installs it." → "One `dtk init` command installs it (`dtk integrate` is an alias)."
- Replace the `[!IMPORTANT]` Python block with:

```markdown
> [!NOTE]
> The hooks run `dtk hook <provider>`, so the agent only needs `dtk` on its `PATH` — no Python, no `jq`,
> and the same registration works from sh, bash, Git Bash, PowerShell and cmd.
```

- Every `dtk integrate` → `dtk init`.
- Claude Code "This creates three files" list becomes two files: `SKILL.md`, and `.claude/settings.json — registers \`dtk hook claude\` under \`PreToolUse\` (merges with any existing settings)`. Replace the following paragraph ("Re-running the command without `--force` …" through the `--force` code block) with:

````markdown
Re-running the command refreshes `SKILL.md` if dtk wrote it and leaves an edited copy alone unless you
pass `--force`. `.claude/settings.json` is always merged: the hook entry is added if missing, and a
registration from an older dtk that ran `dotnet-to-dtk.py` is replaced in place — never duplicated. The
old `.claude/hooks/dotnet-to-dtk.py` is deleted when dtk can prove it wrote it; an edited copy is kept
and reported, and `--force` deletes it too:

```sh
dtk init claude --force
```
````

- Claude "Manual Installation": replace the curl paragraph and code block and the paragraph about `$CLAUDE_PROJECT_DIR` with "Add the following to `.claude/settings.json`:" and change the JSON command to `"command": "dtk hook claude"`.
- Gemini: same two-file list (`GEMINI.md`, `.gemini/settings.json — registers \`dtk hook gemini; exit 0\` under \`BeforeTool\``); the same re-run paragraph adapted to `GEMINI.md` (its dtk section is replaced with `--force`) and `.gemini/hooks/dotnet-to-dtk.py`; "Manual Installation" becomes "Add the following to `.gemini/settings.json`. Gemini CLI blocks the shell command when a hook exits with a code other than 0 or 1, so `; exit 0` keeps a missing `dtk` from blocking every command:" with `"command": "dtk hook gemini; exit 0"`, followed by the existing `GEMINI.md` snippet.
- Add a `## GitHub Copilot CLI` section after `## GitHub Copilot (VS Code)`:

````markdown
## GitHub Copilot CLI

### Installation

```sh
dtk init copilot-cli
```

This creates `.github/hooks/dtk-dotnet.json`, which registers the `preToolUse` hook, and a dtk section in
`.github/copilot-instructions.md`. `dtk init copilot-cli --global` writes the hook to `~/.copilot/hooks/`.

### Manual Installation

Create `.github/hooks/dtk-dotnet.json`. Copilot CLI denies the tool call when a hook exits non-zero, so
`; exit 0` keeps a missing `dtk` from blocking every command:

```json
{
  "version": 1,
  "hooks": {
    "preToolUse": [
      {
        "type": "command",
        "matcher": "bash",
        "bash": "dtk hook copilot-cli; exit 0",
        "powershell": "dtk hook copilot-cli; exit 0",
        "timeoutSec": 10
      }
    ]
  }
}
```
````

- Cursor and Windsurf "How It Works": "No hook or Python dependency is needed" → "No hook is needed". Aider: "No hook or Python dependency is needed beyond Aider's own Python runtime." → "No hook is needed."
- Replace the "## Upgrading dtk" section body with:

````markdown
The hooks run the installed `dtk`, so upgrading the tool upgrades the rewrite — a new subcommand is
covered as soon as `dotnet tool update -g DotnetTokenKiller` finishes. Re-run `dtk init <provider>` after
upgrading to refresh the skill and instruction files, and once to migrate a project set up by a dtk that
installed a Python hook:

```sh
dotnet tool update -g DotnetTokenKiller
dtk init claude          # refreshes the skill; migrates a Python hook if one is left
```

dtk stamps the files it generates, so an artifact you have not edited is refreshed without `--force`; one
you have edited is left alone and reported. To check an installation without changing anything, run
`dtk doctor`.
````

- [ ] **Step 4: `CLAUDE.md`**

Replace the paragraph starting "`dtk integrate copilot-cli` installs a GitHub Copilot CLI `preToolUse` hook" with:

```markdown
`dtk init copilot-cli` (alias `dtk integrate`) installs a GitHub Copilot CLI `preToolUse` hook (`.github/hooks/`)
that runs `dtk hook copilot-cli`, rewriting `dotnet …` to `dtk dotnet …`. Supports `--global` (`~/.copilot/hooks/`).
Distinct from `dtk init copilot` (instruction-only, Copilot IDE). Every hook is `dtk hook <provider>`; this repo's own
`.claude/settings.json` still runs the frozen `.claude/hooks/dotnet-to-dtk.py` until a released dtk has `hook`.
```

- [ ] **Step 5: Verify**

Run: `rtk proxy grep -rn "dtk integrate" README.md src/DotnetTokenKiller.Cli/README.md docfx/index.md docfx/articles CLAUDE.md` — every hit must be an alias mention. Run `rtk proxy grep -rn -i "python" README.md src/DotnetTokenKiller.Cli/README.md docfx/articles docfx/index.md` — only migration mentions remain. Then `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DocsBindingTests"`.
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add README.md src/DotnetTokenKiller.Cli/README.md docfx/index.md docfx/articles CLAUDE.md
git commit -m "docs: document dtk init and the dtk hook registrations

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 10: Check the hook in every shell the harnesses use

**Files:**
- Create: `eng/hooks/check-hook-shells.sh`
- Modify: `eng/aot/test-windows.sh`, `eng/aot/test-package.sh`, `.github/workflows/fallback-package.yml`

**Interfaces:**
- Consumes: an installed `dtk` (Task 4 behavior) in a tools directory.
- Produces: `sh eng/hooks/check-hook-shells.sh <directory containing dtk>` — exits 1 when any check fails. It is the gate for the one question the spec could not settle off Windows: whether Windows PowerShell 5.1 passes the hook payload to a native command.

- [ ] **Step 1: Write the script**

```sh
#!/bin/sh
# Pipes a payload through `dtk hook` exactly as each harness runs its hook command, in every shell present,
# and checks the rewrite and the fail-open exit when dtk is missing. sh on Linux and macOS, Git Bash on
# Windows. The powershell.exe runs are the gate for Windows PowerShell 5.1 passing stdin to a native
# command (docs/superpowers/specs/2026-09-14-native-hook-and-init-design.md, resolved question 1).
#
# Usage: sh eng/hooks/check-hook-shells.sh <directory containing dtk>
set -eu

usage="usage: sh eng/hooks/check-hook-shells.sh <directory containing dtk>"
dtk_dir=${1:?$usage}
dtk_dir=$(cd "$dtk_dir" && pwd)
[ -x "$dtk_dir/dtk" ] || [ -x "$dtk_dir/dtk.exe" ] || { echo "check-hook-shells.sh: no dtk in $dtk_dir" >&2; exit 1; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

# Git Bash rewrites arguments that look like POSIX paths, and `;` lists, before a Windows program sees them.
MSYS_NO_PATHCONV=1
MSYS2_ARG_CONV_EXCL='*'
export MSYS_NO_PATHCONV MSYS2_ARG_CONV_EXCL

printf '%s' '{"tool_input":{"command":"dotnet build"}}' > "$work/tool-input.json"
printf '%s' '{"toolName":"bash","toolArgs":{"command":"dotnet build"}}' > "$work/copilot.json"

# Gemini CLI appends this to every command it runs through PowerShell.
gemini_suffix='; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }'

with_dtk="$dtk_dir:$PATH"
# PATH without any directory holding a dtk, so the fail-open checks cannot find one installed elsewhere.
without_dtk=$(printf '%s' "$PATH" | tr ':' '\n' | while IFS= read -r entry; do
    [ -n "$entry" ] && { [ -x "$entry/dtk" ] || [ -x "$entry/dtk.exe" ]; } || printf '%s:' "$entry"
done)
without_dtk=${without_dtk%:}

bash_cmd=$(command -v bash || command -v sh)
pwsh_cmd=$(command -v pwsh || true)
powershell_cmd=$(command -v powershell.exe || true)

# check <label> <payload file> <rewrite|no-rewrite> <PATH> <program> <args...>
check() {
    label=$1 payload=$2 expect=$3 path=$4
    shift 4
    status=0
    env PATH="$path" "$@" < "$payload" > "$work/out" 2> "$work/err" || status=$?
    rewritten=no
    grep -q 'dtk dotnet build' "$work/out" && rewritten=yes
    ok=no
    if [ "$status" -eq 0 ]; then
        if [ "$expect" = rewrite ] && [ "$rewritten" = yes ]; then ok=yes; fi
        if [ "$expect" = no-rewrite ] && [ "$rewritten" = no ]; then ok=yes; fi
    fi
    if [ "$ok" = yes ]; then
        echo "ok   $label"
        return
    fi
    echo "FAIL $label (exit $status, rewritten: $rewritten, expected: $expect)"
    sed 's/^/  stdout: /' "$work/out"
    sed 's/^/  stderr: /' "$work/err"
    failures=$((failures + 1))
}

# Claude Code: sh -c on Unix, Git Bash on Windows; the bare command.
check "claude, bash" "$work/tool-input.json" rewrite "$with_dtk" "$bash_cmd" -c 'dtk hook claude'

# Gemini CLI: bash -c on Unix; pwsh -NoProfile -Command, else powershell.exe -NoProfile -NonInteractive -Command.
check "gemini, bash" "$work/tool-input.json" rewrite "$with_dtk" "$bash_cmd" -c 'dtk hook gemini; exit 0'
check "gemini, bash, dtk missing" "$work/tool-input.json" no-rewrite "$without_dtk" "$bash_cmd" -c 'dtk hook gemini; exit 0'
if [ -n "$pwsh_cmd" ]; then
    check "gemini, pwsh" "$work/tool-input.json" rewrite "$with_dtk" "$pwsh_cmd" -NoProfile -Command "dtk hook gemini; exit 0$gemini_suffix"
    check "gemini, pwsh, dtk missing" "$work/tool-input.json" no-rewrite "$without_dtk" "$pwsh_cmd" -NoProfile -Command "dtk hook gemini; exit 0$gemini_suffix"
fi
if [ -n "$powershell_cmd" ]; then
    check "gemini, powershell.exe" "$work/tool-input.json" rewrite "$with_dtk" "$powershell_cmd" -NoProfile -NonInteractive -Command "dtk hook gemini; exit 0$gemini_suffix"
    check "gemini, powershell.exe, dtk missing" "$work/tool-input.json" no-rewrite "$without_dtk" "$powershell_cmd" -NoProfile -NonInteractive -Command "dtk hook gemini; exit 0$gemini_suffix"
fi

# Copilot CLI: the bash field, and the powershell field on Windows.
check "copilot-cli, bash" "$work/copilot.json" rewrite "$with_dtk" "$bash_cmd" -c 'dtk hook copilot-cli; exit 0'
check "copilot-cli, bash, dtk missing" "$work/copilot.json" no-rewrite "$without_dtk" "$bash_cmd" -c 'dtk hook copilot-cli; exit 0'
for ps in "$pwsh_cmd" "$powershell_cmd"; do
    [ -n "$ps" ] || continue
    check "copilot-cli, $(basename "$ps")" "$work/copilot.json" rewrite "$with_dtk" "$ps" -NoProfile -Command 'dtk hook copilot-cli; exit 0'
    check "copilot-cli, $(basename "$ps"), dtk missing" "$work/copilot.json" no-rewrite "$without_dtk" "$ps" -NoProfile -Command 'dtk hook copilot-cli; exit 0'
done

if [ "$failures" -ne 0 ]; then
    echo "check-hook-shells.sh: $failures check(s) failed" >&2
    exit 1
fi
```

- [ ] **Step 2: Run it locally against the JIT build and a missing-dtk PATH**

The script needs a directory holding an executable named `dtk`. Publish one:

```bash
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot
sh eng/hooks/check-hook-shells.sh artifacts/aot
```

Expected: every line `ok` (no pwsh or powershell.exe lines on this machine unless pwsh is installed), exit 0. If `artifacts/aot` already holds an older publish, the rewrite checks fail with "Unknown command 'hook'": republish.

Then check it can fail: `mkdir -p /tmp/fake-dtk && printf '#!/bin/sh\nexit 3\n' > /tmp/fake-dtk/dtk && chmod +x /tmp/fake-dtk/dtk && sh eng/hooks/check-hook-shells.sh /tmp/fake-dtk; echo "exit=$?"`
Expected: `FAIL claude, bash` among others, and `exit=1`.

- [ ] **Step 3: Wire it into the package tests**

- `eng/aot/test-windows.sh`: after the first `check_installed` and `"$shim" --version`, add

```sh
# The hook as Claude Code (Git Bash), Gemini CLI (pwsh, else powershell.exe) and Copilot CLI run it.
sh "$repo/eng/hooks/check-hook-shells.sh" "$tools"
```

- `eng/aot/test-package.sh`: after the store check that follows `dotnet tool install` (before the parity tests), add `sh "$repo/eng/hooks/check-hook-shells.sh" "$tools"`. It runs inside the musl image too; the script falls back to `sh` when `bash` is absent.
- `.github/workflows/fallback-package.yml`: after "Install the fallback from the local feed", add a step for both runners:

```yaml
      # The hook as each harness runs it; on Windows this includes Windows PowerShell 5.1.
      - name: Check dtk hook in every shell
        run: |
          tools="$RUNNER_TEMP/dtk-tools"
          if [ "$RUNNER_OS" = "Windows" ]; then
            tools="$(cygpath -u "$tools")"
          fi
          sh eng/hooks/check-hook-shells.sh "$tools"
```

Confirm the job's default shell is bash on both runners (the neighboring steps use `$RUNNER_OS` and `cygpath` without `shell:`, so it is).

- [ ] **Step 4: Commit**

```bash
git add eng/hooks/check-hook-shells.sh eng/aot/test-windows.sh eng/aot/test-package.sh .github/workflows/fallback-package.yml
git commit -m "ci: check dtk hook through sh, bash, pwsh and powershell.exe as the harnesses run it

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

### Task 11: Measure the hook's latency and verify the branch

**Files:**
- Modify: `CLAUDE.md` (Benchmarks section)

**Interfaces:**
- Consumes: the AOT publish from Task 10 (`artifacts/aot/dtk`), the frozen `.claude/hooks/dotnet-to-dtk.py`.
- Produces: one recorded measurement; a green local verification.

- [ ] **Step 1: Publish and measure**

```bash
dotnet publish src/DotnetTokenKiller.Cli -c Release -r linux-x64 -o artifacts/aot
cat > /tmp/measure-hook.sh <<'EOF'
#!/bin/sh
# median_ms <runs> <payload file> <command...>
set -eu
runs=$1 payload=$2
shift 2
i=0
while [ "$i" -lt "$runs" ]; do
    start=$(date +%s%N)
    "$@" < "$payload" > /dev/null 2>&1 || true
    end=$(date +%s%N)
    echo $(( (end - start) / 1000 ))
    i=$((i + 1))
done | sort -n | awk '{ a[NR] = $1 } END { printf "%.1f\n", ((NR % 2) ? a[(NR + 1) / 2] : (a[NR / 2] + a[NR / 2 + 1]) / 2) / 1000 }'
EOF
printf '%s' '{"tool_input":{"command":"ls -la"}}' > /tmp/payload-none.json
printf '%s' '{"tool_input":{"command":"dotnet build"}}' > /tmp/payload-rewrite.json
for p in none rewrite; do
  echo "dtk hook claude ($p): $(sh /tmp/measure-hook.sh 55 /tmp/payload-$p.json artifacts/aot/dtk hook claude) ms"
  echo "python3 hook ($p):    $(sh /tmp/measure-hook.sh 55 /tmp/payload-$p.json python3 .claude/hooks/dotnet-to-dtk.py) ms"
done
echo "dtk --version:        $(sh /tmp/measure-hook.sh 55 /dev/null artifacts/aot/dtk --version) ms"
echo "fork baseline (true): $(sh /tmp/measure-hook.sh 55 /dev/null /bin/true) ms"
```

Expected: `dtk hook claude` medians within 3 ms of `dtk --version` (the spec's target). If they are not, profile before recording: the entry point must run before `Console.OutputEncoding` and must not touch config or tracking.

- [ ] **Step 2: Record it in `CLAUDE.md`**

In the Benchmarks section, after the "Windows x64 packaged shim" paragraph, add a paragraph in the file's style with the measured figures filled in, e.g.:

```markdown
`dtk hook`, measured 2026-09-14, 55 runs each, local AOT publish (linux-x64) against this repo's Python hook, medians
including a <baseline> ms `/bin/true` fork-and-exec baseline: `dtk hook claude` <x> ms (no rewrite) and <y> ms
(rewrite); `python3 .claude/hooks/dotnet-to-dtk.py` <p> ms and <q> ms; `dtk --version` <v> ms. A harness runs the
hook on every shell tool call, so this is a per-call cost; on the `any` fallback it is the JIT start-up instead.
```

Replace every `<…>` with the numbers printed in Step 1 before committing; do not commit the placeholders.

- [ ] **Step 3: Verify the whole branch locally**

Run, in order:

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
dtk dotnet test tests/DotnetTokenKiller.Domain.Tests
dtk dotnet test tests/DotnetTokenKiller.Application.Tests
dtk dotnet test tests/DotnetTokenKiller.Infrastructure.Tests
dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~Hook|FullyQualifiedName~Init|FullyQualifiedName~CliConfigurator|FullyQualifiedName~Completion|FullyQualifiedName~CommandDispatch|FullyQualifiedName~DocsBinding|FullyQualifiedName~Doctor|FullyQualifiedName~Aot|FullyQualifiedName~SpectreBuiltIn"
DTK_AOT_BINARY="$PWD/artifacts/aot/dtk" dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot.AotParityTests"
sh eng/hooks/check-hook-shells.sh artifacts/aot
```

Expected: build and format clean; every listed test run green; the parity tests compare the AOT publish with the JIT build including `init-project` and `init-global`; every shell check `ok`. `SavingsBaselineTests` is untouched by this branch. Report any failure with its output rather than working around it.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "bench: record dtk hook latency against the Python hook

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Differential check

Task 2 appends its result here.

Measured 2026-09-14: `DotnetCommandRewriterDifferentialTests` ran 5000 seeded-generated commands plus the
61 Task 1 `InlineData` cases (5061 total) through both the Python `CopilotCliHook` template and the C# port —
0 mismatches.

## Follow-up (not in this plan)

After the first release containing `dtk hook`: run `dtk init claude` in this repository, commit the migrated `.claude/settings.json` and the deletion of `.claude/hooks/dotnet-to-dtk.py`, and drop the frozen-hook sentence from `CLAUDE.md`. Harness expansion (Cursor, Codex CLI, Factory Droid, Crush, VS Code Copilot, Junie CLI) is sub-project 2 with its own spec.
