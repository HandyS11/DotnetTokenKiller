# Integration Artifact Freshness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make dtk-generated integration artifacts self-refreshing on upgrade, make `dtk doctor` prove the installed hook actually fires, and stop the docs from drifting behind the CLI.

**Architecture:** Every artifact dtk generates in full gains a trailing SHA-256 provenance line. A new write path refreshes an artifact whose stamp proves it is untouched dtk output, and leaves a user-edited one alone. The three hook integrators expose one description of their installation, consumed by both the installer and a new `HookHealthChecker` that `doctor` runs — including a live payload probe through the installed script. A binding test then pins the command surface to the docs.

**Tech Stack:** .NET 10, C# 14, Spectre.Console.Cli, xunit 2.9.3, FluentAssertions 8, NSubstitute 6, `System.Text.Json.Nodes`, `System.Security.Cryptography.SHA256`.

**Spec:** [2026-08-07-integration-freshness-design.md](../specs/2026-08-07-integration-freshness-design.md)

## Global Constraints

- `TreatWarningsAsErrors` is on. Every analyzer warning is a build failure. `GenerateDocumentationFile` is on — give every new type and member an XML doc comment, including internal ones (the codebase documents internals throughout).
- File-scoped namespaces. `var` throughout. Private fields `_camelCase`. Async methods end in `Async`. Interfaces `IPascalCase`.
- LF line endings only, no trailing whitespace, no BOM, 4-space indent for `.cs`, 2-space for XML/JSON/YAML/Markdown.
- All version numbers live in `Directory.Packages.props`; `.csproj` files never carry versions.
- Build: `dtk dotnet build DotnetTokenKiller.slnx`. Test: `dtk dotnet test DotnetTokenKiller.slnx`. Format check: `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`.
- **Known tooling quirk:** an installed `dtk` at or below 0.6.0 can falsely report "0 tests found" for `dtk dotnet test <slnx> --filter …`. If a filtered run reports zero tests, re-run the whole suite without `--filter` before believing it.
- Never write into the developer's real `~/.claude`, `~/.gemini`, or `~/.copilot` from a test. Use a temp directory and the internal `HomePaths(string home)` seam.
- Commit after each task. Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`).

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/DotnetTokenKiller.Application/Integration/ArtifactStamping.cs` | Apply, parse, and verify the provenance line; `StampStyle`; `GeneratedArtifact` |
| `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs` | `HookScope`, `HookPayloadKind`, `HookInstallation`, `IHookIntegrator` |
| `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs` | Discovery, status check, and live probe → `DiagnosticCheck` list |
| `tests/DotnetTokenKiller.Application.Tests/Integration/ArtifactStampingTests.cs` | Stamp round-trip, tampering, legacy recognition |
| `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs` | Every health state, against temp dirs and a substituted runner |
| `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptExecutionTests.cs` | Runs the generated hooks through a real Python |
| `tests/DotnetTokenKiller.Cli.IntegrationTests/DocsBindingTests.cs` | Command surface ↔ README + docfx |

**Modified**

| File | Change |
|---|---|
| `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs` | `UnchangedFiles` init property |
| `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs` | `RunCapturedWithInputAsync` |
| `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs` | Implement it; `RunRedirectedAsync` takes optional stdin |
| `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs` | `Unchanged` list |
| `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` | `WriteGeneratedFileAsync`; hook writes routed through it |
| `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` | `SKILL.md` via the generated path; `DescribeHooks` |
| `src/DotnetTokenKiller.Application/Integration/GeminiCliIntegrator.cs` | `DescribeHooks` |
| `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs` | Script via the generated path; `DescribeHooks` |
| `src/DotnetTokenKiller.Application/UseCases/DoctorUseCase.cs` | Becomes internal; runs the hook checks |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | Register `HookHealthChecker` |
| `src/DotnetTokenKiller.Cli/Commands/DoctorCommand.cs` | Pass the project directory |
| `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs` | Report unchanged files |
| `.claude/hooks/dotnet-to-dtk.py` | Regenerated with its stamp |
| `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs` | Committed hook compared against stamped output |
| `README.md`, `docfx/index.md`, `docfx/articles/*.md` | Documented surface |

---

### Task 1: Provenance stamping

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/ArtifactStamping.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/ArtifactStampingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum StampStyle { HashComment, HtmlComment }`; `sealed record GeneratedArtifact(string Path, string Body, StampStyle Style, string LegacySignature)`; `static class ArtifactStamping` with `string Apply(string body, StampStyle style)`, `bool TryParse(string content, out string body, out string hash)`, `bool IsAuthentic(string content)`, `string ComputeHash(string body)`, and `const string StampPrefix = "dtk-generated sha256:"`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Application.Tests/Integration/ArtifactStampingTests.cs`:

```csharp
using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class ArtifactStampingTests
{
    [Fact]
    public void Apply_HashComment_AppendsStampAsLastLine()
    {
        var stamped = ArtifactStamping.Apply("print('hi')\n", StampStyle.HashComment);

        stamped.Should().StartWith("print('hi')\n");
        stamped.Split('\n')[^2].Should().StartWith("# " + ArtifactStamping.StampPrefix);
        stamped.Should().EndWith("\n");
    }

    [Fact]
    public void Apply_HtmlComment_WrapsStampInAnHtmlComment()
    {
        var stamped = ArtifactStamping.Apply("# Title\n", StampStyle.HtmlComment);

        stamped.Split('\n')[^2].Should().StartWith("<!-- " + ArtifactStamping.StampPrefix)
            .And.EndWith(" -->");
    }

    [Fact]
    public void Apply_BodyWithoutTrailingNewline_AddsOne()
    {
        var stamped = ArtifactStamping.Apply("no newline", StampStyle.HashComment);

        stamped.Should().StartWith("no newline\n");
    }

    [Theory]
    [InlineData(StampStyle.HashComment)]
    [InlineData(StampStyle.HtmlComment)]
    public void IsAuthentic_UnmodifiedStampedContent_ReturnsTrue(StampStyle style)
    {
        var stamped = ArtifactStamping.Apply("body line one\nbody line two\n", style);

        ArtifactStamping.IsAuthentic(stamped).Should().BeTrue();
    }

    [Fact]
    public void IsAuthentic_BodyEdited_ReturnsFalse()
    {
        var stamped = ArtifactStamping.Apply("original\n", StampStyle.HashComment);
        var tampered = stamped.Replace("original", "edited", StringComparison.Ordinal);

        ArtifactStamping.IsAuthentic(tampered).Should().BeFalse();
    }

    [Fact]
    public void IsAuthentic_CrlfCheckout_StillReturnsTrue()
    {
        // A Windows checkout can rewrite LF to CRLF. That is not tampering, so hashing
        // normalizes line endings first; without this the whole feature misfires on Windows.
        var stamped = ArtifactStamping.Apply("line one\nline two\n", StampStyle.HashComment);
        var crlf = stamped.Replace("\n", "\r\n", StringComparison.Ordinal);

        ArtifactStamping.IsAuthentic(crlf).Should().BeTrue();
    }

    [Fact]
    public void IsAuthentic_NoStamp_ReturnsFalse()
    {
        ArtifactStamping.IsAuthentic("just a file\n").Should().BeFalse();
    }

    [Fact]
    public void IsAuthentic_TruncatedHash_ReturnsFalse()
    {
        var stamped = ArtifactStamping.Apply("body\n", StampStyle.HashComment);
        var truncated = stamped[..^10] + "\n";

        ArtifactStamping.IsAuthentic(truncated).Should().BeFalse();
    }

    [Fact]
    public void TryParse_StampedContent_ReturnsBodyWithoutTheStampLine()
    {
        var stamped = ArtifactStamping.Apply("alpha\nbeta\n", StampStyle.HashComment);

        ArtifactStamping.TryParse(stamped, out var body, out var hash).Should().BeTrue();
        body.Should().Be("alpha\nbeta\n");
        hash.Should().Be(ArtifactStamping.ComputeHash("alpha\nbeta\n"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `ArtifactStamping` and `StampStyle` do not exist.

- [ ] **Step 3: Implement**

Create `src/DotnetTokenKiller.Application/Integration/ArtifactStamping.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Comment syntax used to carry the provenance stamp in a generated artifact.</summary>
internal enum StampStyle
{
    /// <summary>A <c>#</c> line comment, for Python hook scripts.</summary>
    HashComment,

    /// <summary>An HTML comment, for Markdown artifacts such as <c>SKILL.md</c>.</summary>
    HtmlComment
}

/// <summary>
/// A file dtk generates in full and therefore owns end to end.
/// </summary>
/// <param name="Path">Absolute path the artifact is written to.</param>
/// <param name="Body">The generated content, before stamping.</param>
/// <param name="Style">Comment syntax for the stamp line.</param>
/// <param name="LegacySignature">
/// A substring present in every generation of this artifact, used to recognize an unstamped copy
/// left behind by dtk 0.6.0 or earlier. See <c>IntegratorHelpers.WriteGeneratedFileAsync</c>.
/// </param>
internal sealed record GeneratedArtifact(
    string Path,
    string Body,
    StampStyle Style,
    string LegacySignature);

/// <summary>
/// Applies and verifies the provenance line dtk appends to artifacts it generates in full.
/// </summary>
/// <remarks>
/// The stamp answers one question: <em>is this body exactly what some dtk wrote, or did someone
/// edit it?</em> That is what lets an upgrade refresh an untouched artifact without asking while
/// leaving an edited one alone.
/// <para>
/// It carries no version number deliberately. Freshness is decided by comparing the installed body
/// against the current template, never against a version — and a version would make this repo's
/// committed <c>.claude/hooks/dotnet-to-dtk.py</c> change on every release, breaking the test that
/// locks it to the generator for reasons unrelated to the hook's content.
/// </para>
/// <para>
/// The stamp is the last line rather than the first so it need not be positioned below a Python
/// shebang in one artifact and below YAML frontmatter in another; the only per-artifact difference
/// is the comment syntax.
/// </para>
/// </remarks>
internal static class ArtifactStamping
{
    /// <summary>The text introducing the digest on the stamp line.</summary>
    internal const string StampPrefix = "dtk-generated sha256:";

    /// <summary>Length of the lowercase hex SHA-256 digest carried by the stamp.</summary>
    private const int HashLength = 64;

    /// <summary>Appends the provenance line to <paramref name="body"/>.</summary>
    /// <param name="body">The generated content to stamp.</param>
    /// <param name="style">Comment syntax for the stamp line.</param>
    /// <returns>The content as it should be written to disk, stamp included.</returns>
    internal static string Apply(string body, StampStyle style)
    {
        var normalized = Normalize(body);
        var stamp = StampPrefix + ComputeHash(normalized);

        var line = style switch
        {
            StampStyle.HtmlComment => $"<!-- {stamp} -->",
            _ => $"# {stamp}"
        };

        return normalized + line + "\n";
    }

    /// <summary>
    /// Splits stamped content into the body that was hashed and the digest recorded for it.
    /// </summary>
    /// <param name="content">File content to inspect.</param>
    /// <param name="body">Set to the content above the stamp line, line endings normalized.</param>
    /// <param name="hash">Set to the digest recorded on the stamp line.</param>
    /// <returns><see langword="true"/> when a well-formed stamp line is present.</returns>
    internal static bool TryParse(string content, out string body, out string hash)
    {
        body = string.Empty;
        hash = string.Empty;

        var normalized = (content ?? string.Empty).ReplaceLineEndings("\n");
        var prefixIndex = normalized.LastIndexOf(StampPrefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            return false;
        }

        var hashStart = prefixIndex + StampPrefix.Length;
        if (normalized.Length < hashStart + HashLength)
        {
            return false;
        }

        var candidate = normalized.Substring(hashStart, HashLength);
        if (!candidate.All(char.IsAsciiHexDigitLower))
        {
            return false;
        }

        body = normalized[..(normalized.LastIndexOf('\n', prefixIndex) + 1)];
        hash = candidate;
        return true;
    }

    /// <summary>
    /// Whether the content carries a stamp whose digest still matches the body above it — i.e. the
    /// body is untouched output of some dtk version.
    /// </summary>
    /// <param name="content">File content to verify.</param>
    internal static bool IsAuthentic(string content)
        => TryParse(content, out var body, out var hash) && ComputeHash(body) == hash;

    /// <summary>Computes the lowercase hex SHA-256 of the normalized body.</summary>
    /// <param name="body">The content to digest.</param>
    internal static string ComputeHash(string body)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(body))));

    /// <summary>
    /// Normalizes to LF and guarantees a trailing newline, so a CRLF checkout does not read as
    /// tampering and the stamp always lands on its own line.
    /// </summary>
    /// <param name="body">The content to normalize.</param>
    private static string Normalize(string body)
    {
        var normalized = (body ?? string.Empty).ReplaceLineEndings("\n");
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~ArtifactStampingTests"`
Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/ArtifactStamping.cs tests/DotnetTokenKiller.Application.Tests/Integration/ArtifactStampingTests.cs
git commit -m "feat: add provenance stamping for dtk-generated artifacts"
```

---

### Task 2: The generated-artifact write path

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs`

**Interfaces:**
- Consumes: `GeneratedArtifact`, `StampStyle`, `ArtifactStamping.Apply`, `ArtifactStamping.IsAuthentic`, `ArtifactStamping.TryParse` (Task 1).
- Produces: `IntegrationResult.UnchangedFiles` (init property, defaults to `[]`); `IntegrationContext.Unchanged` (`List<string>`); `IntegratorHelpers.WriteGeneratedFileAsync(GeneratedArtifact artifact, IntegrationContext context, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs` (match the existing class's temp-directory setup; if it exposes a `_tempDir` field, reuse it, otherwise add one following the pattern in `DoctorUseCaseTests`):

```csharp
    private static GeneratedArtifact Artifact(string path, string body = "print('v2')\n")
        => new(path, body, StampStyle.HashComment, "_DTK_SUBCOMMANDS");

    [Fact]
    public async Task WriteGeneratedFileAsync_FileMissing_CreatesItStamped()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Created.Should().ContainSingle().Which.Should().Be(path);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(path)).Should().BeTrue();
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_AlreadyCurrent_ReportsUnchangedNotSkipped()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var artifact = Artifact(path);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(artifact.Body, artifact.Style));
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(artifact, context, default);

        context.Unchanged.Should().ContainSingle().Which.Should().Be(path);
        context.Skipped.Should().BeEmpty();
        context.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_StampedOlderGeneration_RefreshesWithoutForce()
    {
        // The whole point of the feature: an untouched artifact from an older dtk is dtk's own
        // output, so overwriting it destroys nothing.
        var path = Path.Combine(_tempDir, "hook.py");
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment));
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle().Which.Should().Be(path);
        (await File.ReadAllTextAsync(path)).Should().Contain("print('v2')");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_UnstampedButRecognized_RefreshesAndNotes()
    {
        // Nothing installed by dtk 0.6.0 or earlier carries a stamp; without this branch the fix
        // would not fire for a single existing user in the release that ships it.
        var path = Path.Combine(_tempDir, "hook.py");
        await File.WriteAllTextAsync(path, "_DTK_SUBCOMMANDS = (\"build\",)\n");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle().Which.Should().Be(path);
        context.Notes.Should().ContainSingle().Which.Should().Contain("older dtk");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_StampedButEdited_SkipsWithoutForce()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var stamped = ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment);
        await File.WriteAllTextAsync(path, stamped.Replace("v1", "mine", StringComparison.Ordinal));
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Skipped.Should().ContainSingle().Which.Should().Be(path);
        (await File.ReadAllTextAsync(path)).Should().Contain("mine");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_UnrecognizedForeignFile_SkipsWithoutForce()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        await File.WriteAllTextAsync(path, "# someone else's script\n");
        var context = new IntegrationContext(force: false);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Skipped.Should().ContainSingle();
        (await File.ReadAllTextAsync(path)).Should().Contain("someone else");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_StampedButEditedWithForce_Overwrites()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var stamped = ArtifactStamping.Apply("print('v1')\n", StampStyle.HashComment);
        await File.WriteAllTextAsync(path, stamped.Replace("v1", "mine", StringComparison.Ordinal));
        var context = new IntegrationContext(force: true);

        await IntegratorHelpers.WriteGeneratedFileAsync(Artifact(path), context, default);

        context.Updated.Should().ContainSingle();
        (await File.ReadAllTextAsync(path)).Should().Contain("print('v2')");
    }

    [Fact]
    public async Task WriteGeneratedFileAsync_AlreadyCurrentWithForce_StillReportsUnchanged()
    {
        var path = Path.Combine(_tempDir, "hook.py");
        var artifact = Artifact(path);
        await File.WriteAllTextAsync(path, ArtifactStamping.Apply(artifact.Body, artifact.Style));
        var context = new IntegrationContext(force: true);

        await IntegratorHelpers.WriteGeneratedFileAsync(artifact, context, default);

        context.Unchanged.Should().ContainSingle();
        context.Updated.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `WriteGeneratedFileAsync` and `IntegrationContext.Unchanged` do not exist.

- [ ] **Step 3: Add `UnchangedFiles` to the result**

In `src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs`, add inside the record body (an init property rather than a fifth positional parameter, so both existing constructors keep working):

```csharp
    /// <summary>
    /// Gets the dtk-generated files that were already current, so nothing was written.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SkippedFiles"/>, which means "left alone because dtk could not
    /// prove it wrote this". Merging the two would make the CLI advise <c>--force</c> for a file
    /// that is already up to date, where the flag would change nothing.
    /// </remarks>
    public IReadOnlyList<string> UnchangedFiles { get; init; } = [];
```

- [ ] **Step 4: Add the accumulator**

In `src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs`, add the list and thread it through `ToResult`:

```csharp
    /// <summary>Gets the list of generated files that were already current during this run.</summary>
    internal List<string> Unchanged { get; } = [];
```

```csharp
    internal IntegrationResult ToResult()
    {
        return new IntegrationResult(Created, Updated, Skipped, Notes)
        {
            UnchangedFiles = Unchanged
        };
    }
```

- [ ] **Step 5: Implement the write path**

In `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs`, add:

```csharp
    /// <summary>
    /// Substring present in every generation of the Python hooks, used to recognize an unstamped
    /// copy installed by dtk 0.6.0 or earlier.
    /// </summary>
    internal const string HookLegacySignature = "_DTK_SUBCOMMANDS";

    /// <summary>
    /// Writes an artifact dtk generates in full, refreshing it when dtk can prove it wrote the
    /// copy already on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule, in order: a missing file is created; a file already equal to the stamped current
    /// template is reported unchanged; a file whose stamp still verifies came from an older dtk
    /// and is refreshed; an unstamped file carrying
    /// <see cref="GeneratedArtifact.LegacySignature"/> predates stamping and is refreshed with a
    /// note; anything else is left alone unless <c>--force</c> is passed.
    /// </para>
    /// <para>
    /// The legacy branch exists because no artifact installed before stamping carries a stamp, so
    /// without it every existing user would fall through to the skip branch and the refresh would
    /// only begin working one release after the one that adds it. It costs a one-time overwrite
    /// for anyone who hand-edited an unstamped hook, which is why the overwrite is reported rather
    /// than silent, and it becomes unreachable once one stamped generation is installed.
    /// </para>
    /// </remarks>
    /// <param name="artifact">The artifact to write.</param>
    /// <param name="context">Integration context carrying the force flag and result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task WriteGeneratedFileAsync(
        GeneratedArtifact artifact,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var content = ArtifactStamping.Apply(artifact.Body, artifact.Style);

        if (!File.Exists(artifact.Path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(artifact.Path)!);
            await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
            context.Created.Add(artifact.Path);
            return;
        }

        var existing = (await File.ReadAllTextAsync(artifact.Path, cancellationToken).ConfigureAwait(false))
            .ReplaceLineEndings("\n");

        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            context.Unchanged.Add(artifact.Path);
            return;
        }

        var isLegacy = !ArtifactStamping.TryParse(existing, out _, out _)
                       && existing.Contains(artifact.LegacySignature, StringComparison.Ordinal);

        if (!ArtifactStamping.IsAuthentic(existing) && !isLegacy && !context.Force)
        {
            context.Skipped.Add(artifact.Path);
            return;
        }

        await File.WriteAllTextAsync(artifact.Path, content, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(artifact.Path);

        if (isLegacy)
        {
            context.Notes.Add(
                $"{artifact.Path} was written by an older dtk and carried no provenance stamp; it was "
                + "regenerated. Any local edits to it are recoverable from version control.");
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~IntegratorHelpersTests"`
Expected: PASS — the eight new tests plus the existing ones.

- [ ] **Step 7: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Integration/IntegrationResult.cs src/DotnetTokenKiller.Application/Integration/IntegrationContext.cs src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs tests/DotnetTokenKiller.Application.Tests/Integration/IntegratorHelpersTests.cs
git commit -m "feat: refresh dtk-generated artifacts that dtk can prove it wrote"
```

---

### Task 3: Route the four owned artifacts through the generated path

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs` (`WriteHookAndSettingsAsync`)
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs`
- Modify: `.claude/hooks/dotnet-to-dtk.py`
- Test: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`

**Interfaces:**
- Consumes: `WriteGeneratedFileAsync`, `HookLegacySignature`, `GeneratedArtifact`, `StampStyle` (Tasks 1–2).
- Produces: `ClaudeCodeIntegrator.SkillLegacySignature` (`internal const string`, value `"name: dotnet-token-killer"`). The Gemini integrator needs no edit here — it installs its hook through `WriteHookAndSettingsAsync`, which this task converts.

- [ ] **Step 1: Write the failing test**

In `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs`, change `RepoClaudeHook_MatchesTheGeneratedHook` to compare against stamped output, and add a companion assertion:

```csharp
    [Fact]
    public void RepoClaudeHook_MatchesTheGeneratedHook()
    {
        var repoRoot = FindRepoRoot();
        var hookPath = Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py");

        File.Exists(hookPath).Should().BeTrue("this repo ships its own copy of the Claude hook at {0}", hookPath);

        var committed = File.ReadAllText(hookPath).ReplaceLineEndings("\n");

        // The committed copy is compared against the *stamped* form, because that is what a user
        // receives. Comparing against the bare template would let this repo's copy and the
        // installed one diverge in exactly the field that decides whether dtk will refresh it.
        committed.Should().Be(
            ArtifactStamping.Apply(HookScriptTemplates.ClaudeHook, StampStyle.HashComment),
            "the committed hook must be regenerated whenever the template changes, or this repo's own "
            + "agent sessions silently stop rewriting the newest subcommand");
    }

    [Fact]
    public void RepoClaudeHook_CarriesAVerifiableStamp()
    {
        var repoRoot = FindRepoRoot();
        var committed = File.ReadAllText(Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py"));

        ArtifactStamping.IsAuthentic(committed).Should().BeTrue(
            "an unverifiable stamp would make dtk treat this repo's own hook as user-edited and refuse "
            + "to refresh it");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~SubcommandBindingTests"`
Expected: FAIL — the committed hook has no stamp.

- [ ] **Step 3: Route the hook writes through the generated path**

In `IntegratorHelpers.WriteHookAndSettingsAsync`, replace the `WriteFileAsync` call with:

```csharp
        await WriteGeneratedFileAsync(
            new GeneratedArtifact(spec.ScriptPath, spec.Script, StampStyle.HashComment, HookLegacySignature),
            context,
            cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 4: Route `SKILL.md` and the Copilot CLI script**

In `ClaudeCodeIntegrator`, add the signature constant next to `SkillMarkdown`:

```csharp
    /// <summary>
    /// Substring present in every generation of the skill file, used to recognize an unstamped copy
    /// installed by dtk 0.6.0 or earlier. It is the frontmatter <c>name:</c> line, which has never
    /// changed and cannot without breaking Claude Code's skill lookup.
    /// </summary>
    internal const string SkillLegacySignature = "name: dotnet-token-killer";
```

and replace the `SKILL.md` write in `IntegrateCoreAsync`:

```csharp
        await IntegratorHelpers.WriteGeneratedFileAsync(
            new GeneratedArtifact(
                Path.Combine(baseDirectory, "skills", "dotnet-token-killer", "SKILL.md"),
                SkillMarkdown,
                StampStyle.HtmlComment,
                SkillLegacySignature),
            context, cancellationToken).ConfigureAwait(false);
```

In `CopilotCliIntegrator.WriteHookArtifactsAsync`, replace the script write (leave the `dtk-dotnet.json` write on `WriteFileAsync` — it is a registration file, not a generated artifact in scope):

```csharp
        await IntegratorHelpers.WriteGeneratedFileAsync(
            new GeneratedArtifact(
                Path.Combine(hooksDir, HookScriptName),
                HookScriptTemplates.CopilotCliHook,
                StampStyle.HashComment,
                IntegratorHelpers.HookLegacySignature),
            context, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 5: Regenerate this repo's committed hook**

```bash
rm -rf /tmp/dtk-regen && mkdir -p /tmp/dtk-regen
dotnet run --project src/DotnetTokenKiller.Cli -- integrate claude --dir /tmp/dtk-regen
cp /tmp/dtk-regen/.claude/hooks/dotnet-to-dtk.py .claude/hooks/dotnet-to-dtk.py
tail -2 .claude/hooks/dotnet-to-dtk.py
```

Expected: the last line reads `# dtk-generated sha256:<64 hex chars>`.

- [ ] **Step 6: Fix the one existing test this changes**

`ClaudeCodeIntegratorTests.IntegrateAsync_ForceOverwritesExistingFiles` asserts `result.UpdatedFiles.Should().HaveCount(2)` after a `--force` re-run. That expectation is now wrong for the right reason: with identical content, `--force` has nothing to write, so both artifacts arrive in `UnchangedFiles`. Update it:

```csharp
        // With generated-artifact stamping, a --force re-run over identical content writes nothing:
        // the skill and hook are already current, so they report unchanged rather than updated.
        result.CreatedFiles.Should().BeEmpty();
        result.UpdatedFiles.Should().BeEmpty();
        result.UnchangedFiles.Should().HaveCount(2);
        result.SkippedFiles.Should().ContainSingle();
```

The other integrator assertions are `Contain`-based and are unaffected by the appended stamp line. `IntegrateAsync_HookScript_MatchesCommittedRepoHook` compares written against committed — both stamped identically — and keeps passing once Step 5 has run.

- [ ] **Step 7: Run the full suite**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS. If any other test fails on exact content, wrap its expectation in `ArtifactStamping.Apply(..., StampStyle.HashComment)` rather than loosening the assertion to `Contain`.

- [ ] **Step 8: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/ .claude/hooks/dotnet-to-dtk.py tests/DotnetTokenKiller.Application.Tests/
git commit -m "feat: stamp the hook scripts and Claude skill on install"
```

---

### Task 4: Report unchanged files in `dtk integrate`

**Files:**
- Modify: `src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs`

**Interfaces:**
- Consumes: `IntegrationResult.UnchangedFiles` (Task 2), the refresh behaviour (Task 3).
- Produces: no new API.

- [ ] **Step 1: Write the failing test**

Add to `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs`, following the existing tests' console and temp-directory setup:

```csharp
    [Fact]
    public async Task RunAsync_SecondRunWithNoChanges_ReportsAlreadyIntegratedRatherThanAdvisingForce()
    {
        // Before generated-artifact stamping, a repeat run put the hook and skill in the skipped
        // bucket and told the user to re-run with --force — advice that would have changed nothing.
        var settings = new IntegrateCommandSettings { Provider = "claude", Directory = _tempDir };

        (await _sut.RunAsync(settings, default)).Should().Be(0);
        _console.Output.Should().Contain("created");

        _console.Clear();
        (await _sut.RunAsync(settings, default)).Should().Be(0);

        _console.Output.Should().Contain("Already integrated");
        _console.Output.Should().NotContain("--force");
    }

    [Fact]
    public async Task RunAsync_SecondRunWithNoChanges_ListsTheUnchangedArtifacts()
    {
        var settings = new IntegrateCommandSettings { Provider = "claude", Directory = _tempDir };
        await _sut.RunAsync(settings, default);
        _console.Clear();

        await _sut.RunAsync(settings, default);

        _console.Output.Should().Contain("unchanged");
        _console.Output.Should().Contain("dotnet-to-dtk.py");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~IntegrateCommandTests"`
Expected: FAIL — no "unchanged" output.

- [ ] **Step 3: Implement**

In `IntegrateCommand.RunAsync`, after the `UpdatedFiles` loop:

```csharp
        foreach (var file in result.UnchangedFiles)
        {
            console.MarkupLine($"[grey]unchanged[/] {Markup.Escape(RelativePath(directory, file))}");
        }
```

Update the `PrintSummary` XML doc to state that generated artifacts that were already current arrive in `UnchangedFiles` and therefore no longer trigger the skipped-files warning. No branch change is needed: with those files out of `SkippedFiles`, a repeat run falls through to "Already integrated".

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~IntegrateCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Cli/Commands/IntegrateCommand.cs tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/IntegrateCommandTests.cs
git commit -m "feat: distinguish already-current artifacts from skipped ones in integrate output"
```

---

### Task 5: Run a child process with stdin

**Files:**
- Modify: `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs`
- Modify: `src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs`
- Test: `tests/DotnetTokenKiller.Infrastructure.Tests/Execution/ProcessCommandRunnerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Task<CommandResult> ICommandRunner.RunCapturedWithInputAsync(string command, IReadOnlyList<string> args, string standardInput, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test**

Add to `tests/DotnetTokenKiller.Infrastructure.Tests/Execution/ProcessCommandRunnerTests.cs` (match the file's existing cross-platform command selection; if it has none, use the `cmd`/`sh` split shown here):

```csharp
    [Fact]
    public async Task RunCapturedWithInputAsync_WritesStdinAndCapturesTheEcho()
    {
        var sut = new ProcessCommandRunner();

        var result = OperatingSystem.IsWindows()
            ? await sut.RunCapturedWithInputAsync("cmd.exe", ["/c", "findstr", "x"], "axb\n", default)
            : await sut.RunCapturedWithInputAsync("sh", ["-c", "cat"], "axb\n", default);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("axb");
    }

    [Fact]
    public async Task RunCapturedWithInputAsync_EmptyInput_ClosesStdinSoTheChildExits()
    {
        var sut = new ProcessCommandRunner();

        var result = OperatingSystem.IsWindows()
            ? await sut.RunCapturedWithInputAsync("cmd.exe", ["/c", "more"], string.Empty, default)
            : await sut.RunCapturedWithInputAsync("sh", ["-c", "cat"], string.Empty, default);

        result.ExitCode.Should().Be(0);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `RunCapturedWithInputAsync` is not defined.

- [ ] **Step 3: Declare it on the interface**

In `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs`:

```csharp
    /// <summary>
    /// Runs the command, writes <paramref name="standardInput"/> to its stdin, closes stdin, and
    /// captures stdout/stderr.
    /// </summary>
    /// <param name="command">The executable to run.</param>
    /// <param name="args">Arguments to pass to the executable.</param>
    /// <param name="standardInput">Text to write to the child's standard input before closing it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CommandResult> RunCapturedWithInputAsync(
        string command,
        IReadOnlyList<string> args,
        string standardInput,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it**

In `ProcessCommandRunner`, add the method and thread an optional payload through the shared runner:

```csharp
    /// <inheritdoc/>
    public Task<CommandResult> RunCapturedWithInputAsync(
        string command,
        IReadOnlyList<string> args,
        string standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardInput);

        return RunRedirectedAsync(
            command,
            args,
            static (reader, token) => reader.ReadToEndAsync(token),
            static (reader, token) => reader.ReadToEndAsync(token),
            cancellationToken,
            standardInput);
    }
```

Change `RunRedirectedAsync`'s signature to take a trailing `string? standardInput = null`, and replace the unconditional stdin close with:

```csharp
        // Write the payload (if any) and close stdin either way, so a child that reads it sees EOF
        // and exits instead of hanging forever waiting for input.
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        process.StandardInput.Close();
```

`RunRedirectedAsync` is already `async`, so the added `await` needs no signature change beyond the new parameter.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~ProcessCommandRunnerTests"`
Expected: PASS. Any test double implementing `ICommandRunner` by hand must gain the member; `NSubstitute` substitutes pick it up automatically.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs src/DotnetTokenKiller.Infrastructure/Execution/ProcessCommandRunner.cs tests/DotnetTokenKiller.Infrastructure.Tests/
git commit -m "feat: add a command runner path that writes to a child's stdin"
```

---

### Task 6: Describe hook installations once

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/GeminiCliIntegrator.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/CopilotCliIntegrator.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs`

**Interfaces:**
- Consumes: `GeneratedArtifact`, `StampStyle` (Task 1).
- Produces: `enum HookScope { Project, Global }`; `enum HookPayloadKind { ClaudeCode, GeminiCli, CopilotCli }`; `sealed record HookInstallation(string ProviderName, HookScope Scope, GeneratedArtifact Script, string RegistrationPath, HookPayloadKind PayloadKind)`; `interface IHookIntegrator { IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope); }` implemented by `ClaudeCodeIntegrator`, `GeminiCliIntegrator`, `CopilotCliIntegrator`.

- [ ] **Step 1: Write the failing test**

Create `tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs`:

```csharp
using DotnetTokenKiller.Application.Integration;
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
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home), home),
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
        var integrators = new IHookIntegrator[]
        {
            new ClaudeCodeIntegrator(new RtkHookCoexistence(home), home),
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
```

If `RtkHookCoexistence`'s constructor takes something other than `HomePaths`, match its actual signature — check `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs` for how it is constructed there and copy that.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `IHookIntegrator` does not exist.

- [ ] **Step 3: Define the abstraction**

Create `src/DotnetTokenKiller.Application/Integration/IHookIntegrator.cs`:

```csharp
namespace DotnetTokenKiller.Application.Integration;

/// <summary>Where a hook is installed.</summary>
internal enum HookScope
{
    /// <summary>Installed under the current project directory.</summary>
    Project,

    /// <summary>Installed under the user's home configuration.</summary>
    Global
}

/// <summary>Shape of the pre-tool-execution payload a host CLI feeds its hook on stdin.</summary>
internal enum HookPayloadKind
{
    /// <summary>Claude Code's <c>PreToolUse</c> payload.</summary>
    ClaudeCode,

    /// <summary>Gemini CLI's <c>BeforeTool</c> payload.</summary>
    GeminiCli,

    /// <summary>GitHub Copilot CLI's <c>preToolUse</c> payload.</summary>
    CopilotCli
}

/// <summary>One installed (or installable) rewrite hook, described once for both installer and diagnostics.</summary>
/// <param name="ProviderName">The provider this hook belongs to (e.g. "claude").</param>
/// <param name="Scope">Whether this describes the project or the home-config install.</param>
/// <param name="Script">The hook script as a generated artifact, including its current template body.</param>
/// <param name="RegistrationPath">
/// The JSON file that registers the hook with the host CLI — a merged <c>settings.json</c> for most
/// providers, a dedicated <c>dtk-dotnet.json</c> for Copilot CLI.
/// </param>
/// <param name="PayloadKind">Payload shape to use when probing this hook.</param>
internal sealed record HookInstallation(
    string ProviderName,
    HookScope Scope,
    GeneratedArtifact Script,
    string RegistrationPath,
    HookPayloadKind PayloadKind);

/// <summary>
/// Implemented by provider integrators that install a rewrite hook, so a diagnostic can find that
/// hook without keeping its own copy of where the installer put it.
/// </summary>
internal interface IHookIntegrator
{
    /// <summary>Describes the hooks this provider installs at the given scope.</summary>
    /// <param name="directory">Project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to describe.</param>
    IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope);
}
```

- [ ] **Step 4: Implement it on the three integrators**

In `ClaudeCodeIntegrator`, add `IHookIntegrator` to the base list and:

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
                new GeneratedArtifact(
                    Path.Combine(baseDirectory, "hooks", "dotnet-to-dtk.py"),
                    HookScriptTemplates.ClaudeHook,
                    StampStyle.HashComment,
                    IntegratorHelpers.HookLegacySignature),
                Path.Combine(baseDirectory, "settings.json"),
                HookPayloadKind.ClaudeCode)
        ];
    }
```

Then rewrite `IntegrateCoreAsync`'s `WriteHookAndSettingsAsync` call to build its `HookSpec` from `DescribeHooks(...)[0]` rather than repeating the paths, so the two can never disagree.

In `GeminiCliIntegrator`, the same shape with `home.GeminiDir` / `Path.Combine(directory, ".gemini")`, `HookScriptTemplates.GeminiHook`, and `HookPayloadKind.GeminiCli`.

In `CopilotCliIntegrator`, the same shape with `home.CopilotHooksDir` / `Path.Combine(directory, ".github", "hooks")`, `HookScriptTemplates.CopilotCliHook`, `HookPayloadKind.CopilotCli`, and a `RegistrationPath` of `Path.Combine(hooksDir, HookJsonName)`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HookDescriptionTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/Integration/ tests/DotnetTokenKiller.Application.Tests/Integration/HookDescriptionTests.cs
git commit -m "refactor: describe hook installations once for installer and diagnostics"
```

---

### Task 7: Hook status check

**Files:**
- Create: `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Consumes: `IHookIntegrator`, `HookInstallation`, `HookScope` (Task 6); `ArtifactStamping` (Task 1); `DiagnosticCheck` (existing, in `DotnetTokenKiller.Application.UseCases`); `ICommandRunner` (Task 5).
- Produces: `internal sealed class HookHealthChecker(ICommandRunner runner)` with `Task<IReadOnlyList<DiagnosticCheck>> RunAsync(IReadOnlyList<IHookIntegrator> integrators, string projectDirectory, CancellationToken cancellationToken)`. Check names: `"<provider> hook (<scope>)"` and `"<provider> hook probe (<scope>)"`, scope rendered lowercase.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — `HookHealthChecker` does not exist.

- [ ] **Step 3: Implement**

Create `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Checks that the rewrite hooks dtk installed are present, registered, current, and actually
/// firing.
/// </summary>
/// <remarks>
/// These are the checks <c>doctor</c> was missing. The SDK, config, database, and tee directory it
/// already verified rarely break; the hook — a generated Python script, registered by an absolute
/// command string, invoked by an interpreter that may not exist under that name — breaks silently
/// and takes the whole integration with it.
/// </remarks>
/// <param name="runner">Runs the installed hook for the probe.</param>
internal sealed class HookHealthChecker(ICommandRunner runner)
{
    /// <summary>How long the probe waits before declaring the interpreter wedged.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

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
                    if (!File.Exists(installation.Script.Path))
                    {
                        continue;
                    }

                    var registered = ReadRegisteredCommand(installation, out var registrationMessage);
                    checks.Add(BuildStatusCheck(installation, registered, registrationMessage));

                    if (registered is not null)
                    {
                        checks.Add(await ProbeAsync(installation, registered, cancellationToken)
                            .ConfigureAwait(false));
                    }
                }
            }
        }

        if (checks.Count == 0)
        {
            checks.Add(new DiagnosticCheck(
                "hook integration",
                true,
                "No rewrite hook found in this directory or your home config. "
                + "Run 'dtk integrate <provider>' to install one."));
        }

        return checks;
    }

    /// <summary>
    /// Finds the command the host CLI is configured to run for this hook, by locating any string in
    /// the registration JSON that names the hook script.
    /// </summary>
    /// <remarks>
    /// Matching on the script's file name rather than on dtk's exact command string is deliberate:
    /// the SKILL and instruction files tell Windows users to change <c>python3</c> to <c>python</c>
    /// in the registration, and an exact-command match would report that working install as broken.
    /// It also spans both registration shapes — the nested <c>hooks[event][].hooks[].command</c>
    /// used by Claude Code and Gemini CLI, and Copilot CLI's <c>hooks.preToolUse[].bash</c> — with
    /// no per-provider branching.
    /// </remarks>
    /// <param name="installation">The installation whose registration to read.</param>
    /// <param name="message">Set to a human-readable reason when no command is found.</param>
    /// <returns>The registered command, or <see langword="null"/>.</returns>
    private static string? ReadRegisteredCommand(HookInstallation installation, out string message)
    {
        message = string.Empty;

        if (!File.Exists(installation.RegistrationPath))
        {
            message = $"not registered — {installation.RegistrationPath} does not exist";
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(installation.RegistrationPath));
        }
        catch (JsonException ex)
        {
            message = $"{installation.RegistrationPath} could not be read as JSON: {ex.Message}";
            return null;
        }

        var scriptFileName = Path.GetFileName(installation.Script.Path);
        var command = FindStringContaining(root, scriptFileName);

        if (command is null)
        {
            message = $"not registered — no entry in {installation.RegistrationPath} runs {scriptFileName}";
        }

        return command;
    }

    /// <summary>Depth-first search for a string value containing <paramref name="needle"/>.</summary>
    /// <param name="node">The JSON node to search.</param>
    /// <param name="needle">The substring to look for.</param>
    private static string? FindStringContaining(JsonNode? node, string needle)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                return text.Contains(needle, StringComparison.Ordinal) ? text : null;
            case JsonArray array:
                return array.Select(item => FindStringContaining(item, needle)).FirstOrDefault(m => m is not null);
            case JsonObject obj:
                return obj.Select(pair => FindStringContaining(pair.Value, needle))
                    .FirstOrDefault(m => m is not null);
            default:
                return null;
        }
    }

    /// <summary>Builds the presence/registration/freshness check for one installation.</summary>
    /// <param name="installation">The installation being checked.</param>
    /// <param name="registeredCommand">The command found in the registration, or <see langword="null"/>.</param>
    /// <param name="registrationMessage">The reason no command was found, when applicable.</param>
    private static DiagnosticCheck BuildStatusCheck(
        HookInstallation installation,
        string? registeredCommand,
        string registrationMessage)
    {
        var name = CheckName(installation, "hook");

        if (registeredCommand is null)
        {
            return new DiagnosticCheck(
                name,
                false,
                $"{registrationMessage}. Run 'dtk integrate {installation.ProviderName}'.");
        }

        var installed = File.ReadAllText(installation.Script.Path).ReplaceLineEndings("\n");
        var current = ArtifactStamping.Apply(installation.Script.Body, installation.Script.Style);

        if (string.Equals(installed, current, StringComparison.Ordinal))
        {
            return new DiagnosticCheck(name, true, "installed, registered, up to date");
        }

        var refreshable = ArtifactStamping.IsAuthentic(installed)
                          || (!ArtifactStamping.TryParse(installed, out _, out _)
                              && installed.Contains(installation.Script.LegacySignature, StringComparison.Ordinal));

        return refreshable
            ? new DiagnosticCheck(
                name,
                false,
                $"stale — written by an older dtk. Run 'dtk integrate {installation.ProviderName}' to refresh.")
            : new DiagnosticCheck(
                name,
                false,
                $"modified locally — dtk will not overwrite it. Run 'dtk integrate "
                + $"{installation.ProviderName} --force' to regenerate.");
    }

    /// <summary>Feeds a payload through the installed hook and asserts every subcommand is rewritten.</summary>
    /// <param name="installation">The installation to probe.</param>
    /// <param name="registeredCommand">The command the host CLI runs, used to resolve the interpreter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<DiagnosticCheck> ProbeAsync(
        HookInstallation installation,
        string registeredCommand,
        CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook probe");
        var interpreter = ResolveInterpreter(registeredCommand);
        var command = string.Join("; ", DotnetSubcommands.Ordered.Select(sub => $"dotnet {sub}"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var result = await runner
                .RunCapturedWithInputAsync(
                    interpreter,
                    [installation.Script.Path],
                    BuildPayload(installation.PayloadKind, command),
                    timeout.Token)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new DiagnosticCheck(
                    name, false, $"{interpreter} exited with code {result.ExitCode}: {result.StdErr.Trim()}");
            }

            var missing = DotnetSubcommands.Ordered
                .Where(sub => !result.StdOut.Contains($"dtk dotnet {sub}", StringComparison.Ordinal))
                .ToList();

            return missing.Count == 0
                ? new DiagnosticCheck(name, true, $"rewrites all {DotnetSubcommands.Ordered.Count} subcommands")
                : new DiagnosticCheck(
                    name,
                    false,
                    $"does not rewrite: {string.Join(", ", missing)}. "
                    + $"Run 'dtk integrate {installation.ProviderName}' to refresh the hook.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DiagnosticCheck(
                name, false, $"{interpreter} did not respond within {ProbeTimeout.TotalSeconds:F0}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck(name, false, $"could not run {interpreter}: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes the interpreter from the registered command's first token, so an install whose command
    /// was edited is probed the way the host CLI will actually run it.
    /// </summary>
    /// <param name="registeredCommand">The command found in the registration file.</param>
    private static string ResolveInterpreter(string registeredCommand)
    {
        var first = registeredCommand.Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        return string.IsNullOrEmpty(first) ? "python3" : first.Trim('"');
    }

    /// <summary>Builds the stdin payload in the shape the provider's host CLI sends.</summary>
    /// <param name="kind">Which host CLI's payload shape to build.</param>
    /// <param name="command">The shell command the payload should carry.</param>
    private static string BuildPayload(HookPayloadKind kind, string command)
    {
        JsonNode payload = kind switch
        {
            HookPayloadKind.CopilotCli => new JsonObject
            {
                ["toolName"] = "bash",
                ["toolArgs"] = new JsonObject { ["command"] = command }
            },
            _ => new JsonObject
            {
                ["tool_input"] = new JsonObject { ["command"] = command }
            }
        };

        return payload.ToJsonString();
    }

    /// <summary>Builds a check name such as <c>claude hook probe (project)</c>.</summary>
    /// <param name="installation">The installation the check belongs to.</param>
    /// <param name="label">The check label.</param>
    private static string CheckName(HookInstallation installation, string label)
        => $"{installation.ProviderName} {label} ({installation.Scope.ToString().ToLowerInvariant()})";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HookHealthCheckerTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs
git commit -m "feat: check installed hooks for registration, freshness, and firing"
```

---

### Task 8: Wire the hook checks into `doctor`

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/DoctorUseCase.cs`
- Modify: `src/DotnetTokenKiller.Application/DependencyInjection.cs`
- Modify: `src/DotnetTokenKiller.Cli/Commands/DoctorCommand.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/DoctorUseCaseTests.cs`
- Test: `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/DoctorCommandTests.cs`

**Interfaces:**
- Consumes: `HookHealthChecker.RunAsync` (Task 7); `IHookIntegrator` (Task 6).
- Produces: `DoctorUseCase` becomes `internal sealed class DoctorUseCase(ICommandRunner runner, IConfigProvider configProvider, HookHealthChecker hookHealth, IEnumerable<IProviderIntegrator> integrators)` with `RunAsync(string dbPath, string teeDirectory, string projectDirectory, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test**

Add to `tests/DotnetTokenKiller.Application.Tests/UseCases/DoctorUseCaseTests.cs`:

```csharp
    [Fact]
    public async Task RunAsync_NoHooksInstalled_StillReportsTheFourBaselineChecksPlusHookStatus()
    {
        var checks = await _sut.RunAsync("/tmp/test.db", _tempDir, _tempDir);

        checks.Select(c => c.Name).Should().Contain(
            ["dotnet SDK", "config file", "tracking database", "tee directory", "hook integration"]);
    }
```

Update every existing `RunAsync(...)` call in this file to pass `_tempDir` as the new third argument, and construct the SUT with the two new dependencies:

```csharp
        _sut = new DoctorUseCase(
            _runner,
            _configProvider,
            new HookHealthChecker(_runner),
            [new GeminiCliIntegrator(new HomePaths(_tempDir))]);
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet build DotnetTokenKiller.slnx`
Expected: FAIL — the constructor and `RunAsync` overload do not match.

- [ ] **Step 3: Implement**

In `DoctorUseCase`, change the declaration to `internal sealed class` and take the new dependencies. Add the accessibility rationale as a remark, mirroring `ClaudeCodeIntegrator`'s:

```csharp
/// <summary>Runs a series of self-diagnostic checks to verify dtk is set up correctly.</summary>
/// <remarks>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the internal <see cref="HookHealthChecker"/>:
/// a primary constructor is as accessible as its containing type, and the compiler rejects (CS0051)
/// a public constructor exposing a less-accessible parameter type. Its only consumer,
/// <c>DoctorCommand</c>, is internal and resolves it through DI.
/// </remarks>
/// <param name="runner">The command runner used to probe the dotnet SDK.</param>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="hookHealth">Checks installed rewrite hooks.</param>
/// <param name="integrators">All provider integrators; the hook-installing ones are inspected.</param>
internal sealed class DoctorUseCase(
    ICommandRunner runner,
    IConfigProvider configProvider,
    HookHealthChecker hookHealth,
    IEnumerable<IProviderIntegrator> integrators)
```

and extend `RunAsync`:

```csharp
    internal async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(
        string dbPath,
        string teeDirectory,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DiagnosticCheck>
        {
            await CheckDotnetSdkAsync(cancellationToken).ConfigureAwait(false),
            await CheckConfigAsync(cancellationToken).ConfigureAwait(false),
            CheckDbAccessible(dbPath),
            CheckTeeWritable(teeDirectory)
        };

        checks.AddRange(await hookHealth
            .RunAsync([.. integrators.OfType<IHookIntegrator>()], projectDirectory, cancellationToken)
            .ConfigureAwait(false));

        return checks;
    }
```

Register the checker in `src/DotnetTokenKiller.Application/DependencyInjection.cs`, next to `AddTransient<DoctorUseCase>()`:

```csharp
        services.AddTransient<HookHealthChecker>();
```

In `DoctorCommand.RunAsync`, pass the project directory:

```csharp
        var checks = await doctorUseCase.RunAsync(dbPath, teeDirectory, Environment.CurrentDirectory, cancellationToken)
            .ConfigureAwait(false);
```

- [ ] **Step 4: Add the CLI-level test**

Add to `tests/DotnetTokenKiller.Cli.IntegrationTests/Commands/DoctorCommandTests.cs`, matching the file's existing SUT construction:

```csharp
    [Fact]
    public async Task RunAsync_NoHookInstalled_PrintsTheHookIntegrationRow()
    {
        await _sut.RunAsync(default);

        _console.Output.Should().Contain("hook integration");
    }
```

- [ ] **Step 5: Run the full suite**

Run: `dtk dotnet test DotnetTokenKiller.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DotnetTokenKiller.Application/ src/DotnetTokenKiller.Cli/Commands/DoctorCommand.cs tests/
git commit -m "feat: run hook health checks from dtk doctor"
```

---

### Task 9: Execute the generated hooks with a real interpreter

**Files:**
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptExecutionTests.cs`

**Interfaces:**
- Consumes: `HookScriptTemplates.ClaudeHook`, `.GeminiHook`, `.CopilotCliHook`; `DotnetSubcommands.Ordered`.
- Produces: no production API.

**Why this task exists:** no test currently executes the hooks at all — they are asserted only as text. A generated script that Python refuses to parse would pass every test in the suite today.

- [ ] **Step 1: Write the test**

Create `tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptExecutionTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run it**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~HookScriptExecutionTests"`
Expected: PASS, 3 tests. If Python is genuinely absent locally the tests pass trivially; verify the real path once with `python3 --version`.

- [ ] **Step 3: Commit**

```bash
git add tests/DotnetTokenKiller.Application.Tests/Integration/HookScriptExecutionTests.cs
git commit -m "test: execute the generated hooks through a real Python"
```

---

### Task 10: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docfx/index.md`
- Modify: `docfx/articles/getting-started.md`
- Modify: `docfx/articles/token-analytics.md`
- Modify: `docfx/articles/ai-agent-setup.md`

**Interfaces:**
- Consumes: the doctor output from Task 8, the integrate output from Task 4.
- Produces: documented surface for `pipe`, `log`, `gain --coverage`, the refreshed doctor, and upgrade guidance.

- [ ] **Step 1: README "Features at a Glance"**

Add three bullets after the `list package` bullet and extend the diagnostics one:

```markdown
- **Pipe mode** — `dtk pipe <subcommand>` filters output dtk did not produce: CI logs, or any
  invocation the hook missed
- **Log retrieval** — `dtk log` returns a previous run's full output instead of re-running the build
- **Filter coverage** — `dtk gain --coverage` ranks every command by unfiltered tokens at stake, so
  the next filter is chosen from data
- **Self-diagnostics** — `dtk doctor` validates your setup, including feeding a sample payload
  through your installed hook to prove it still fires
```

- [ ] **Step 2: README Diagnostics section**

Replace the sample output under `## Diagnostics` with a run that includes the hook rows:

```sh
$ dtk doctor
  ✔  dotnet SDK: Found dotnet 10.0.100
  ✔  config file: Loaded successfully (or using defaults)
  ✔  tracking database: Found at ~/.local/share/dtk/dtk.db
  ✔  tee directory: Writable at /tmp/dtk
  ✔  claude hook (project): installed, registered, up to date
  ✔  claude hook probe (project): rewrites all 6 subcommands

All checks passed.
```

and add below it:

```markdown
The probe runs your installed hook script through the interpreter your `settings.json` actually
names, feeding it a command that exercises every subcommand dtk filters. A hook left over from an
older dtk fails here, naming the subcommands it no longer rewrites and the command that fixes it.
```

- [ ] **Step 3: `docfx/articles/token-analytics.md`**

Add this section, after the export/JSON material:

````markdown
## Filter coverage

`dtk gain` measures what dtk saved. `dtk gain --coverage` measures what it did **not** — every
command that ran unfiltered, ranked by the tokens at stake:

```sh
dtk gain --coverage
```

Each row carries:

- **Command** — the `dotnet` subcommand as invoked.
- **Outcome** — `filtered` when a filter produced the output; `passthrough` when no filter is
  registered for that subcommand; `degraded` when a filter ran but fell back to the raw tail; and
  `filter faulted` when the filter threw and dtk fell back rather than failing the run.
- **Source** — `run` for output dtk produced itself, `pipe` for output supplied through
  `dtk pipe`. Piped runs are tracked separately because dtk did not control the invocation and
  cannot vouch for the exit code it was not given.

The ranking is what makes the next filter a data-driven choice rather than a guess: the command at
the top of the unfiltered list is the one costing the most tokens today.
````

- [ ] **Step 4: `docfx/articles/getting-started.md` and `docfx/index.md`**

Add `dtk pipe` and `dtk log` to the command list each page carries, with one line each:

```markdown
- `dtk pipe build` — filter output dtk did not produce (CI logs, missed invocations)
- `dtk log` — retrieve a previous run's full output without re-running it
```

- [ ] **Step 5: `docfx/articles/ai-agent-setup.md`**

Add an `## Upgrading dtk` section:

```markdown
## Upgrading dtk

The hook installed in your project carries the list of subcommands dtk filters, so a dtk release
that adds one leaves your installed hook a version behind. Re-run the integration after upgrading:

```sh
dotnet tool update -g DotnetTokenKiller
dtk integrate claude          # refreshes the hook and skill in place
```

dtk stamps the files it generates, so an artifact you have not edited is refreshed without
`--force`; one you have edited is left alone and reported. To check the state of an installation
without changing anything, run `dtk doctor`.
```

- [ ] **Step 6: Verify the docs build**

Run: `dtk dotnet build DotnetTokenKiller.slnx` then check markdown lint if configured locally.
Expected: no build change; the docs are content-only.

- [ ] **Step 7: Commit**

```bash
git add README.md docfx/
git commit -m "docs: document pipe, log, gain --coverage, and the hook diagnostics"
```

---

### Task 11: Bind the command surface to the docs

**Files:**
- Create: `tests/DotnetTokenKiller.Cli.IntegrationTests/DocsBindingTests.cs`

**Interfaces:**
- Consumes: `CliConfigurator.Configure` (internal, visible via `InternalsVisibleTo`); the command-tree walk technique in `SubcommandRegistrationTests`; the repo-root locator in `SubcommandBindingTests`.
- Produces: no production API.

- [ ] **Step 1: Write the test**

Create `tests/DotnetTokenKiller.Cli.IntegrationTests/DocsBindingTests.cs`:

```csharp
using System.Reflection;
using DotnetTokenKiller.Cli.Commands.Settings;
using FluentAssertions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Binds the registered command surface to the documentation. Four commands shipped after v0.6.0
/// reached users undocumented because nothing failed when a command was added without docs; this is
/// the thing that fails.
/// </summary>
public sealed class DocsBindingTests
{
    /// <summary>
    /// Names deliberately absent from the docs, each with the reason. Keep this list short: it is
    /// the pressure valve that stops an inconvenient failure from getting the whole test disabled,
    /// not a place to park undocumented surface.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["--vv"] = "debug-only dump of raw dotnet output; documented in the SKILL flags table, not the articles"
    };

    [Fact]
    public void EveryRegisteredCommand_IsDocumented()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepoRoot(), "README.md"));
        var articles = ReadArticles();

        foreach (var command in RegisteredCommandNames())
        {
            if (Allowed.ContainsKey(command))
            {
                continue;
            }

            readme.Should().Contain(command, "command '{0}' must appear in README.md", command);
            articles.Should().Contain(command, "command '{0}' must appear in a docfx article", command);
        }
    }

    [Fact]
    public void EveryLongFormOption_IsDocumented()
    {
        var articles = ReadArticles();

        foreach (var option in LongFormOptionNames())
        {
            if (Allowed.ContainsKey(option))
            {
                continue;
            }

            articles.Should().Contain(option, "option '{0}' must appear in a docfx article", option);
        }
    }

    /// <summary>All command names in the Spectre tree, walked depth-first (e.g. "log", "list package").</summary>
    private static IEnumerable<string> RegisteredCommandNames()
    {
        var names = new List<string>();
        Walk([], names);
        return names;

        void Walk(string[] path, List<string> into)
        {
            foreach (var name in ParseCommandsSection(RunHelp([.. path, "--help"])))
            {
                into.Add(name);
                Walk([.. path, name], into);
            }
        }
    }

    /// <summary>All long-form option names declared on the CLI's settings types.</summary>
    private static IEnumerable<string> LongFormOptionNames()
    {
        return typeof(GainCommandSettings).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(GainCommandSettings).Namespace)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(p => p.GetCustomAttributes<CommandOptionAttribute>())
            .SelectMany(a => a.LongNames)
            .Select(n => "--" + n)
            .Distinct(StringComparer.Ordinal);
    }

    private static string ReadArticles()
    {
        var articlesDir = Path.Combine(FindRepoRoot(), "docfx", "articles");
        return string.Join(
            "\n",
            Directory.EnumerateFiles(articlesDir, "*.md", SearchOption.AllDirectories).Select(File.ReadAllText));
    }

    private static List<string> ParseCommandsSection(string help)
    {
        var lines = help.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == "COMMANDS:");

        if (start < 0)
        {
            return [];
        }

        return
        [
            .. lines
                .Skip(start + 1)
                .TakeWhile(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim().Split(' ')[0]),
        ];
    }

    private static string RunHelp(params string[] args)
    {
        var console = new TestConsole().Width(100);
        var app = new CommandApp();
        app.Configure(config =>
        {
            CliConfigurator.Configure(config, "1.2.3-test");
            config.Settings.Console = console;
        });

        app.Run(args);
        return console.Output;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }
}
```

`CommandOptionAttribute.LongNames` exists in Spectre.Console.Cli 0.55.0 (verified against
`~/.nuget/packages/spectre.console.cli/0.55.0/lib/net10.0/Spectre.Console.Cli.xml`), so the
reflection above compiles as written.

- [ ] **Step 2: Run it**

Run: `dtk dotnet test DotnetTokenKiller.slnx --filter "FullyQualifiedName~DocsBindingTests"`
Expected: PASS after Task 10. Any additional failure names a command or option that is genuinely
undocumented — document it in the relevant docfx article rather than adding it to `Allowed`, unless
it is deliberately internal, in which case add it with a reason.

- [ ] **Step 3: Commit**

```bash
git add tests/DotnetTokenKiller.Cli.IntegrationTests/DocsBindingTests.cs docfx/ README.md
git commit -m "test: bind the registered command surface to the documentation"
```

---

### Task 12: Close out

**Files:**
- Modify: `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md`
- Modify: `docs/superpowers/specs/2026-08-07-integration-freshness-design.md`

- [ ] **Step 1: Full verification**

```bash
dtk dotnet build DotnetTokenKiller.slnx
dtk dotnet test DotnetTokenKiller.slnx
dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes
```

Expected: build clean (warnings are errors), all tests pass, nothing to format. Do not proceed on a
failure — fix it.

- [ ] **Step 2: End-to-end check against a scratch project**

```bash
rm -rf /tmp/dtk-e2e && mkdir -p /tmp/dtk-e2e
dotnet run --project src/DotnetTokenKiller.Cli -- integrate claude --dir /tmp/dtk-e2e
# Simulate a 0.6.0 install: strip the stamp, leaving an unstamped legacy artifact.
sed -i '$d' /tmp/dtk-e2e/.claude/hooks/dotnet-to-dtk.py
dotnet run --project src/DotnetTokenKiller.Cli -- integrate claude --dir /tmp/dtk-e2e
```

Expected: the second run reports `updated` for the hook and prints the note about an older dtk —
not `skipped` with a `--force` hint. Then:

```bash
dotnet run --project src/DotnetTokenKiller.Cli -- integrate claude --dir /tmp/dtk-e2e
```

Expected: `unchanged` for both artifacts and `Already integrated. Nothing to do.`

- [ ] **Step 3: Mark §8 resolved in the gap analysis**

Add a resolution note under §8 of `docs/superpowers/specs/2026-07-27-rtk-gap-analysis.md`, matching
the format of the existing ones:

```markdown
> **Resolved 2026-08-07.** `doctor` now checks every installed hook: present, registered in the
> host CLI's settings, and current against the generator — then feeds a payload through the
> installed script via the interpreter the registration actually names, asserting every canonical
> subcommand comes back rewritten. Generated artifacts carry a provenance stamp, so an untouched
> hook from an older dtk refreshes on the next `dtk integrate` without `--force`, which is what
> made §7's failure mode survivable on already-deployed machines.
> See [the design](2026-08-07-integration-freshness-design.md).
```

- [ ] **Step 4: Flip the spec's status line**

In `docs/superpowers/specs/2026-08-07-integration-freshness-design.md`, change the first line to:

```markdown
**Status:** Implemented — see [the plan](../plans/2026-08-07-integration-freshness.md)
```

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs: mark hook freshness and diagnostics implemented"
```

## Self-Review Notes

Spec sections and where they land: stamping → Task 1; refresh semantics and the legacy branch →
Task 2; `UnchangedFiles` → Tasks 2 and 4; artifact scope (three hooks + `SKILL.md`) → Task 3; the
shared descriptor → Task 6; discovery, status, and probe → Task 7; runner stdin → Task 5; doctor
wiring → Task 8; the real-interpreter test → Task 9; docs → Task 10; `DocsBindingTests` → Task 11.

One refinement was made against the spec while planning, and is reflected in Task 7: the
registration check matches on the hook script's **file name** rather than on dtk's exact command
string. The spec's rule would have reported a working Windows install as unregistered, because the
SKILL and instruction files tell those users to change `python3` to `python` in the registration.
Matching on the file name also spans both registration shapes without per-provider branching, and
it is what supplies the interpreter the probe then uses.
