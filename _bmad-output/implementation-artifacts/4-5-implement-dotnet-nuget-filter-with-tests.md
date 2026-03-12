# Story 4.5: Implement dotnet nuget Filter with Tests

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet nuget` commands,
I want NuGet output stripped of progress bars and verbose HTTP indicators,
so that I save 75–85% of tokens while seeing the operation result clearly.

## Acceptance Criteria

1. **Push success compact**: When `DotnetNugetFilter.Apply(rawOutput)` is called with a `nuget push` fixture containing "Your package was pushed", the output is `✓ nuget push succeeded\n`.
2. **Token savings**: Token savings is ≥75% on the push fixture.
3. **Locals clear compact**: When `Apply(rawOutput)` is called with output containing "resources have been cleared", the output is `✓ nuget locals cleared\n`.
4. **Fallback noise stripping**: When `Apply(rawOutput)` is called with a nuget subcommand output that does not match push or clear, HTTP noise lines (PUT/GET/Created URLs) and progress indicators are stripped and remaining non-empty lines are returned.
5. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
6. **All regex patterns use `[GeneratedRegex]`**: No `new Regex(...)` at runtime.
7. **`DotnetNugetCommand` wired**: `DotnetNugetCommand` injects `FilteredRunUseCase filteredRun` and `DotnetNugetFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
8. **DI registration**: `DotnetNugetFilter` is registered as a singleton in `AddApplication()`.
9. **Snapshot test**: A Verify.Xunit snapshot test exists for the push fixture in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
10. **Fixture file**: `dotnet_nuget_push_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
11. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions).
12. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create fixture file as embedded resource (AC: #10)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_nuget_push_raw.txt` — see "Fixture File Content" section below
  - [x] The `EmbeddedResource` glob `Fixtures/**/*.txt` already exists in the `.csproj` — no `.csproj` changes needed

- [x] Task 2: Implement `DotnetNugetFilter` (AC: #1, #2, #3, #4, #5, #6)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetNugetFilter.cs`
  - [x] `public sealed partial class DotnetNugetFilter : IOutputFilter` (needs `partial` for `[GeneratedRegex]`)
  - [x] Implement `Apply(string rawOutput)` — see "Implementation" section below
  - [x] Use `[GeneratedRegex]` for `HttpNoisePattern` and `PushPreamblePattern`

- [x] Task 3: Register `DotnetNugetFilter` in DI (AC: #8)
  - [x] Add `services.AddSingleton<DotnetNugetFilter>();` after `DotnetFormatFilter` in `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 4: Wire `DotnetNugetCommand` to use `FilteredRunUseCase` (AC: #7)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetNugetFilter filter` via primary constructor
  - [x] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import

- [x] Task 5: Write filter tests (AC: #1, #2, #3, #4, #5, #9)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetNugetFilterTests.cs`
  - [x] Snapshot test for push fixture (Verify.Xunit — static `Verifier.Verify()`)
  - [x] Savings gate test: ≥75%
  - [x] Noise line theory tests: verify HTTP/push lines absent from push fixture output
  - [x] Inline locals-clear test: "resources have been cleared" → `✓ nuget locals cleared\n`
  - [x] Inline fallback test: non-push/non-clear output → noise stripped, meaningful lines returned
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null

- [x] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #9)
  - [x] Run `dotnet test --filter "FullyQualifiedName~DotnetNugetFilterTests"` → first run fails (no `.verified.txt`)
  - [x] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` — should contain `✓ nuget push succeeded`
  - [x] Rename `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 7: Build and verify (AC: #11, #12)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (156 baseline + 9 new = 165 app tests, 166 total)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1–3.3, 4.1, 4.2, 4.3, 4.4)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Complete — all packages pinned |
| `.editorconfig` | Complete — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` | `Strip(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` | `Estimate(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs` | `ShortenPath`, `Truncate`, `FormatTokens` — complete |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Complete (story 2.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs` | Complete (story 3.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs` | Complete (story 3.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs` | Complete (story 3.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs` | Complete (story 4.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRunFilter.cs` | Complete (story 4.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs` | Complete (story 4.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetFormatFilter.cs` | Complete (story 4.4) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetNugetFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs` | Complete (story 4.1) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRunFilterTests.cs` | Complete (story 4.2) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetEfFilterTests.cs` | Complete (story 4.3) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetFormatFilterTests.cs` | Complete (story 4.4) — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_nuget_push_raw.txt` here |

**Test count baseline**: 156 tests total (138 app + 17 domain + 1 integration). All must continue to pass.

**`DotnetNugetCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it with `FilteredRunUseCase.RunAsync`. Same rewiring pattern as stories 4.1 (clean), 4.2 (run), 4.3 (ef), and 4.4 (format).

### Architecture Constraints (CRITICAL)

- `DotnetNugetFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`) + `Helpers` namespace
- Filter has NO `rootPath` constructor parameter (unlike `DotnetBuildFilter`/`DotnetFormatFilter`) — nuget push/clear output contains no absolute paths to shorten
- Filter MUST be `stateless` — no mutable instance fields
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter MUST be `partial` because it uses `[GeneratedRegex]` → `sealed partial class`
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`
- `using System.Text.RegularExpressions;` required for `[GeneratedRegex]` / `Regex`
- `using System.Text;` required for `StringBuilder` (used in fallback output)

### DI Registration: `DotnetNugetFilter` Has No Constructor Parameters

`DotnetNugetFilter` is a plain `sealed partial class` with no constructor parameters:

```csharp
services.AddSingleton<DotnetNugetFilter>();
```

### Filter Design: Output Mode Detection

`dotnet nuget` subcommands produce very different output. The filter uses phrase detection to choose output mode:

| Detected Phrase | Mode | Output |
|---|---|---|
| Line contains `"Your package was pushed"` | Push success | `✓ nuget push succeeded\n` |
| Line contains `"resources have been cleared"` | Locals clear | `✓ nuget locals cleared\n` |
| Neither phrase found | Fallback | Noise stripped; remaining non-empty lines returned |

**Fallback noise patterns** (stripped from output):

- HTTP method lines: `PUT https://...`, `GET https://...`, `Created https://...`, `OK https://...`
- Progress/push preamble: `Pushing <filename> to '<url>'...`

The fallback returns whatever non-noise, non-empty lines remain — giving visibility into unrecognized nuget subcommands (e.g. `nuget list source`, `nuget verify`) without the verbose HTTP chatter.

**Key design decisions:**

- `"Your package was pushed"` check uses `StringComparison.OrdinalIgnoreCase` — NuGet CLI capitalisation is stable but this is defensive
- `"resources have been cleared"` uses `OrdinalIgnoreCase` for the same reason
- `[GeneratedRegex]` used for `HttpNoisePattern` and `PushPreamblePattern`
- Fallback uses `StringBuilder` to accumulate non-noise lines

### Implementation: `DotnetNugetFilter`

```csharp
namespace DotnetTokenKiller.Application.Filters;

using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Text;
using System.Text.RegularExpressions;

public sealed partial class DotnetNugetFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var isPush = false;
        var isClear = false;
        var sb = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Contains("Your package was pushed", StringComparison.OrdinalIgnoreCase))
            {
                isPush = true;
                continue;
            }

            if (line.Contains("resources have been cleared", StringComparison.OrdinalIgnoreCase))
            {
                isClear = true;
                continue;
            }

            if (HttpNoisePattern().IsMatch(line) || PushPreamblePattern().IsMatch(line))
                continue;

            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine(line);
        }

        if (isPush)
            return "✓ nuget push succeeded\n";

        if (isClear)
            return "✓ nuget locals cleared\n";

        return sb.ToString();
    }

    // Matches NuGet HTTP method lines: "  PUT https://...", "  GET https://...", "  Created https://...", "  OK https://..."
    [GeneratedRegex(@"^\s*(PUT|GET|Created|OK)\s+https?://", RegexOptions.IgnoreCase)]
    private static partial Regex HttpNoisePattern();

    // Matches push preamble: "Pushing Foo.1.0.0.nupkg to 'https://...'"
    [GeneratedRegex(@"^Pushing .+\.nupkg to '", RegexOptions.IgnoreCase)]
    private static partial Regex PushPreamblePattern();
}
```

**Key design decisions:**

- **No constructor parameter**: Unlike `DotnetBuildFilter`/`DotnetFormatFilter`, there are no absolute paths to shorten in push/clear output.
- **Early-exit pattern**: Push and clear flags are set during the single pass; fallback `StringBuilder` accumulates simultaneously — single scan, no re-pass.
- **`isPush` takes priority over `isClear`**: `if (isPush)` checked first; in practice both can't be true in one nuget run.
- **Fallback `sb.ToString()`**: Returns empty string if all lines are noise — clean, no special-casing needed.
- **`RegexOptions.IgnoreCase` on patterns**: NuGet CLI is consistent but defensive for cross-platform scenarios.
- **No `CultureInfo.InvariantCulture`** needed — no integer interpolations in this filter (CA1305 not triggered).

### Updated `DotnetNugetCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetNugetCommand(
    FilteredRunUseCase filteredRun,
    DotnetNugetFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("nuget").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import.

### Updated `DependencyInjection.cs` (Application project)

Add one line after `DotnetFormatFilter`. No constructor args → no factory lambda:

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<FilteredRunUseCase>();
    services.AddSingleton<DotnetBuildFilter>();
    services.AddSingleton<DotnetTestFilter>();
    services.AddSingleton<DotnetRestoreFilter>();
    services.AddSingleton<DotnetPublishFilter>();
    services.AddSingleton<DotnetPackFilter>();
    services.AddSingleton<DotnetCleanFilter>();
    services.AddSingleton<DotnetRunFilter>();
    services.AddSingleton<DotnetEfFilter>();
    services.AddSingleton<DotnetFormatFilter>();
    services.AddSingleton<DotnetNugetFilter>();  // NEW
    return services;
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_nuget_push_raw.txt`:

```sh
Pushing DotnetTokenKiller.1.0.0.nupkg to 'https://api.nuget.org/v3/index.json'...
  PUT https://api.nuget.org/v3/index.json
  Created https://api.nuget.org/v3/index.json 1523ms
Your package was pushed.
```

**Expected output** (for snapshot verification):

```sh
✓ nuget push succeeded
```

**Token savings calculation:**

- Fixture: ~155 chars
- Output: ~24 chars (`✓ nuget push succeeded\n`)
- Savings: (155 - 24) / 155 ≈ 85% ≥ 75% ✓

### Test Implementation

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetNugetFilterTests
{
    private readonly DotnetNugetFilter _sut = new();

    [Fact]
    public Task Apply_PushFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_nuget_push_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_PushFixture_SavingsAtLeast75Percent()
    {
        var fixture = LoadFixture("dotnet_nuget_push_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(75.0, because: "nuget push filter should achieve ≥75% savings");
    }

    [Theory]
    [InlineData("PUT https://")]
    [InlineData("Created https://")]
    [InlineData("Pushing DotnetTokenKiller")]
    public void Apply_PushFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_nuget_push_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LocalsClear_ReturnsCompactMessage()
    {
        const string input = """
            http-cache resources have been cleared.
            global-packages resources have been cleared.
            temp resources have been cleared.
            plugins-cache resources have been cleared.
            """;
        _sut.Apply(input).Should().Be("✓ nuget locals cleared\n");
    }

    [Fact]
    public void Apply_FallbackWithNoise_StripsHttpLines()
    {
        const string input = """
            NuGet sources:
              GET https://api.nuget.org/v3/index.json
              OK https://api.nuget.org/v3/index.json 120ms
            nuget.org [Enabled]
              https://api.nuget.org/v3/index.json
            """;
        var result = _sut.Apply(input);
        result.Should().NotContain("GET https://", StringComparison.Ordinal);
        result.Should().NotContain("OK https://", StringComparison.Ordinal);
        result.Should().Contain("nuget.org [Enabled]", StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_NullInput_ReturnsNonNull()
    {
        _sut.Apply(null!).Should().NotBeNull();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNonNull()
    {
        _sut.Apply(string.Empty).Should().NotBeNull();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetNugetFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

**Test count**: ~9 tests (1 snapshot + 1 savings + 3 noise theory + 1 locals clear + 1 fallback noise strip + 1 null + 1 empty). New total: 156 + 9 = ~165 tests.

**Important notes on tests:**

- `_sut = new()` — no constructor args (no `rootPath`)
- `Verify(result)` returns `Task` — test method must return `Task` (not `void`), must NOT be `async`
- `.Should().NotContain(noiseLine, StringComparison.Ordinal)` — CA1307 requires `StringComparison` overload
- `.Should().Contain("nuget.org [Enabled]", StringComparison.Ordinal)` — same
- Inline strings use C# raw string literals (`""" ... """`)

### Verify Snapshot Acceptance Workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetNugetFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetNugetFilterTests.Apply_PushFixture_MatchesSnapshot.received.txt` — should contain `✓ nuget push succeeded`
4. Accept by renaming: `mv DotnetNugetFilterTests.Apply_PushFixture_MatchesSnapshot.received.txt DotnetNugetFilterTests.Apply_PushFixture_MatchesSnapshot.verified.txt`
5. Re-run tests — all pass
6. Commit the `.verified.txt` file

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5–4.4)

- **CA1852** — `DotnetNugetFilter` MUST be `sealed`
- **CA1050/RCS1110/S3903** — type MUST be in named namespace (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **`sealed partial class`** — because of `[GeneratedRegex]`; do NOT forget `partial`
- **`using System.Text.RegularExpressions;`** — NOT in implicit usings; must be explicit
- **`using System.Text;`** — NOT in implicit usings; must be explicit (for `StringBuilder`)
- **CA1307** — use `StringComparison.Ordinal` in all `Contains`/`EndsWith`/`StartsWith` calls: `line.Contains("...", StringComparison.OrdinalIgnoreCase)` everywhere in production code; `.Should().Contain("...", StringComparison.Ordinal)` in tests
- **CA1309** — `StringComparison.Ordinal` (not `OrdinalIgnoreCase`) satisfies CA1309 for known-stable strings; use `OrdinalIgnoreCase` in filter for defensive casing
- **RCS1118** — if any string literal is reused more than once, extract to `const`
- **No `CultureInfo.InvariantCulture` needed** — no integer interpolations in this filter (no CA1305 triggers)
- **Import ordering**: After `dotnet format`, project usings (Helpers, Domain) come before system usings (Text, RegularExpressions) per `.editorconfig`
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetNugetFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`sb.ToString()` may return empty string**: This is correct for the "all noise" case — no special casing needed

### Git Context (Recent Commits)

```sh
3ab05d7 Feat: implement DotnetCleanFilter with tests and update DotnetCleanCommand
c568abc Feat: restore, publish & pack filters (#5)
992e65f Fix: enhance duration parsing in DotnetTestFilter to support multiple time units
f1dbde2 Feat: test filters (#2)
d534cbd Feat: core & foundation (#1)
```

Current branch: `feat/add-filters-and-passthrough`. Stories 4.1–4.4 are either done or in review on this branch.

### Previous Story Intelligence (Story 4.4 — Format Filter)

Key learnings applied here:

- **`sealed partial class` pattern**: Same as stories 4.1–4.4; needed for `[GeneratedRegex]`
- **No `rootPath` constructor parameter**: NuGet push/clear output doesn't contain absolute file paths needing shortening — simpler than `DotnetFormatFilter`/`DotnetBuildFilter`
- **Single-pass design**: Flag detection (`isPush`, `isClear`) and fallback accumulation happen in one loop — same efficiency pattern as `DotnetEfFilter`
- **Snapshot directory**: Configured globally in `VerifyInit.cs` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: Test method must return `Task`, must NOT be `async`
- **`dotnet format` run last**: Always run after all tests pass — catches import ordering issues
- **Test class naming**: `DotnetNugetFilterTests` in `DotnetTokenKiller.Application.Tests.Filters` namespace
- **`DotnetNugetCommand` current state**: `DotnetNugetCommand(ICommandRunner commandRunner)` → same rewiring as stories 4.1–4.4 before them
- **CA1307 in tests**: FluentAssertions `Contains`/`NotContain` with string must include `StringComparison.Ordinal` overload to satisfy analyzer

### What This Story Does NOT Implement (Scope Guard)

- Passthrough for unrecognized subcommands — Story 4.6
- `SqliteTracker` — Story 5.1
- `JsonConfigProvider` — Story 6.1
- `FileTeeService` — Story 6.2
- Any other CLI command rewiring besides `DotnetNugetCommand`

### Project Structure Notes

- All target directories already exist — no new directories required
- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetNugetFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetNugetFilter` singleton after `DotnetFormatFilter`
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_nuget_push_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetNugetFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetNugetFilterTests.Apply_PushFixture_MatchesSnapshot.verified.txt`

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.5]
- [Source: _bmad-output/implementation-artifacts/4-4-implement-dotnet-format-filter-with-tests.md] — previous story; same command rewiring pattern, same analyzer pitfalls
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetEfFilter.cs] — reference for `sealed partial class` + single-pass flag detection pattern
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs] — reference for simple `sealed class` without rootPath
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs] — current state (uses `RunPassthroughAsync`)
- [Source: src/DotnetTokenKiller.Application/DependencyInjection.cs] — current state (needs `DotnetNugetFilter` added after `DotnetFormatFilter`)
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init pattern
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design] — filter constraints (stateless, sealed, non-throwing)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- FluentAssertions `Should().NotContain(string, StringComparison)` overload does not exist in this project's FA version — removed `StringComparison` parameter from all FA string containment assertions (matches pattern in other filter tests).

### Completion Notes List

- Implemented `DotnetNugetFilter` as `sealed partial class` with single-pass flag detection (`isPush`, `isClear`) and `StringBuilder` fallback accumulation.
- `HttpNoisePattern` and `PushPreamblePattern` use `[GeneratedRegex]` source generation.
- `DotnetNugetCommand` rewired from `ICommandRunner.RunPassthroughAsync` to `FilteredRunUseCase.RunAsync`.
- `DotnetNugetFilter` registered as singleton in `AddApplication()` after `DotnetFormatFilter`.
- 9 new tests added; all 166 total tests pass. Build: 0 errors, 0 warnings. Format: clean.
- Snapshot accepted: `✓ nuget push succeeded` (85% token savings on push fixture, exceeds ≥75% threshold).

### File List

- `src/DotnetTokenKiller.Application/Filters/DotnetNugetFilter.cs` (new)
- `src/DotnetTokenKiller.Application/DependencyInjection.cs` (modified — added `DotnetNugetFilter` singleton)
- `src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs` (modified — rewired to `FilteredRunUseCase`)
- `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_nuget_push_raw.txt` (new)
- `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetNugetFilterTests.cs` (new)
- `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetNugetFilterTests.Apply_PushFixture_MatchesSnapshot.verified.txt` (new)

## Change Log

- 2026-03-12: Implemented `DotnetNugetFilter` with push/clear/fallback output modes, wired `DotnetNugetCommand` to `FilteredRunUseCase`, registered filter in DI, added 9 tests (166 total). Story moved to review.
