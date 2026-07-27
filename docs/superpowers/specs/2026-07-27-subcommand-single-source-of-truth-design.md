# Subcommand List — Single Source of Truth

**Date:** 2026-07-27
**Status:** Implemented — see [the plan](../plans/2026-07-27-subcommand-single-source-of-truth.md)
**Implements:** §7 of [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md)

Three things below were superseded during implementation. The plan records the reasoning; in
short: the binding tests moved into the layer each one guards rather than all living in
`Cli.IntegrationTests`; the repo-hook parity test fails rather than skips when it cannot find the
repo root, because xunit 2.9.3 has no runtime skip; and the two generated-hook tests are pinned to
literal expected bytes rather than deriving their expectation from `DotnetSubcommands`, which had
made them incapable of failing under the very mutation meant to prove them.

## Problem

The set of dotnet subcommands dtk handles exists in four places:

| Location | Layer | Order | Bound to canonical? |
|---|---|---|---|
| `ArgumentPreprocessor.KnownSubcommandsOrdered` | Cli | display | canonical (routing + completion) |
| `FilterKeys` | Domain | — | no (5 hand-written consts) |
| `_DTK_SUBCOMMANDS` in `HookScriptTemplates.cs:54` | Application | alphabetical | no |
| `.claude/hooks/dotnet-to-dtk.py` | repo | alphabetical | no test enforces parity |

Adding a subcommand means editing all four. Forgetting the third means the hook never rewrites
`dotnet <new>`, so the new filter ships with 0% adoption and nothing errors. Forgetting the fourth
means this repo's own agent sessions stop benefiting. Both failures are silent, and the test suite
catches neither today.

The layering knot: the canonical list lives in `Cli`, while the hook template that needs it lives in
`Application`. `Application` cannot reference `Cli`, which is what forced the copy.

This refactor changes no user-visible behaviour. Its value is that every later item in the gap
analysis — new filters, pipe mode, `dtk log` — adds subcommands.

## Design

### 1. Canonical source in Domain

New `src/DotnetTokenKiller.Domain/DotnetSubcommands.cs`:

```csharp
public static class DotnetSubcommands
{
    public const string Build = "build";
    public const string Test = "test";
    public const string Restore = "restore";
    public const string Clean = "clean";
    public const string Format = "format";

    /// Canonical display order — drives CLI routing, completion, and help.
    public static readonly IReadOnlyList<string> Ordered = [Build, Test, Restore, Clean, Format];

    /// Case-insensitive membership, for argument preprocessing.
    public static readonly IReadOnlySet<string> All = new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);

    /// Deterministic alphabetical order — for generated artifacts, so their bytes are stable.
    public static readonly IReadOnlyList<string> Sorted = [.. Ordered.Order(StringComparer.Ordinal)];
}
```

Domain is the lowest layer; `Application`, `Infrastructure`, and `Cli` all reach it. The set of
filterable dotnet commands is a domain concept, so this is where it belongs — it is not a
concession to the layering.

### 2. `FilterKeys` bound by construction

`FilterKeys` consts are used in `[FromKeyedServices(...)]` attributes, so they must remain
compile-time constants and cannot be replaced by a list. Instead they alias the canonical consts:

```csharp
public const string Build = DotnetSubcommands.Build;
```

This eliminates the second copy outright rather than testing it: the values can no longer diverge.

### 3. Consumers rebound

- `ArgumentPreprocessor.KnownSubcommandsOrdered` and `KnownSubcommands` forward to
  `DotnetSubcommands.Ordered` / `.All`. The per-subcommand consts (`BuildSubcommand` etc., used by
  `CliConfigurator`) alias the Domain consts. Existing call sites and tests compile unchanged.
- `HookScriptTemplates` references `DotnetSubcommands` directly. The knot dissolves without moving
  anything into `Cli`.

### 4. Hook script generated from the canonical list

`HookScriptTemplates.SharedCore` becomes an interpolated string. Two things are generated:

- the tuple: `_DTK_SUBCOMMANDS = ("build", "clean", "format", "restore", "test")` from
  `DotnetSubcommands.Sorted`;
- the `build|test|restore|clean|format` prose in the Claude, Gemini, and Copilot CLI docstring
  headers, from the same list.

Alphabetical order is deliberate: it matches what the three hook files already contain, so the
generator's first output must be byte-identical to today's committed hook. An empty diff against
`.claude/hooks/dotnet-to-dtk.py` is the first proof the generator is correct.

Implementation note: the templates are 4-quote raw string literals containing Python
triple-quoted docstrings. Interpolated raw strings need enough `$` sigils that `{` in the Python
source is not read as a hole — the existing `re.compile(...)` body and JSON dict literals contain
braces. Prefer building the generated fragment as a separate `string.Join` expression and
concatenating, over interpolating inside the large literal, so the Python body stays unescaped and
readable.

### 5. Binding tests

All in `tests/DotnetTokenKiller.Cli.IntegrationTests` — the only project with `InternalsVisibleTo`
from both `Cli` and `Application`, so it can see both sides of every binding. A new
`SubcommandBindingTests.cs`:

| Test | Binds | Mechanism |
|---|---|---|
| `FilterKeys` public const values == `DotnetSubcommands.Ordered` | Domain keys | reflection over `FilterKeys` public consts |
| every subcommand resolves a keyed `IOutputFilter` | DI registrations | build the real container, `GetRequiredKeyedService` per key |
| `dtk dotnet --help` lists exactly the canonical set | Spectre registrations | render help via `TestConsole`, assert the command column |
| generated `ClaudeHook` tuple == canonical set | hook template | parse `_DTK_SUBCOMMANDS` out of the generated script |
| `.claude/hooks/dotnet-to-dtk.py` byte-identical to `ClaudeHook` | repo's own hook | walk up from `AppContext.BaseDirectory` to the repo root |

The repo-root test skips (rather than fails) when no `.git` directory is found above the test
assembly, so a packaged/NuGet-consumed run does not fail on a file that is not there. In this
repo's CI the directory always exists, so the test does run.

Spectre registrations stay explicit — each command has bespoke descriptions and examples, and the
help surface is already snapshot-locked. Table-driving `AddCommand<T>` through reflection would
trade a clear failure for a harder-to-read one. The binding is enforced by test, not by
construction.

## Testing

- Full suite green: `dtk dotnet test DotnetTokenKiller.slnx`.
- **Mutation check by hand:** temporarily remove one entry from `DotnetSubcommands.Ordered` and
  confirm each of the five new tests fails. A binding test that passes vacuously is worse than no
  test, because it reads as coverage.
- Existing `CliConfiguratorTests` snapshots must not change — any snapshot diff means the refactor
  altered the user-facing help surface, which it must not.
- `dtk dotnet format --verify-no-changes` and `jb inspectcode` clean, per `TreatWarningsAsErrors`.

## Out of scope

- **Multi-word subcommands** (`list package`, `ef migrations`). The hook's `_PATTERN` joins the list
  into a regex alternation, where a token containing a space, and ordering between a prefix and its
  extension, both need care. §1 of the gap analysis will hit this; solving it now would be
  speculative.
- Changing which subcommands dtk supports. The set stays exactly `build`, `test`, `restore`,
  `clean`, `format`.
- Generating `.claude/hooks/dotnet-to-dtk.py` at build time. The parity test catches drift with no
  build machinery; regeneration can follow if the test ever proves annoying rather than useful.
