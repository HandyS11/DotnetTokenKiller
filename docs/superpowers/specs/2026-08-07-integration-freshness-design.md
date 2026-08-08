**Status:** Implemented — see [the plan](../plans/2026-08-07-integration-freshness.md)
**Implements:** §8 of [2026-07-27-rtk-gap-analysis.md](2026-07-27-rtk-gap-analysis.md), plus the
deployed-artifact half of §7's failure mode

## Problem

Three defects share one root: dtk generates files into other tools' configuration directories and
then loses track of them.

**1. A new subcommand filter never reaches existing users.** `list package` filtering shipped in
#126. The hook that rewrites `dotnet list package` → `dtk dotnet list package` is generated from
`DotnetSubcommands`, so a freshly integrated project gets it. But
`IntegratorHelpers.ShouldSkipWrite` (`IntegratorHelpers.cs:47`) leaves any existing file untouched
without `--force`, and nothing stamps the installed copy with what generated it. A user who ran
`dtk integrate claude` at 0.6.0 upgrades to 0.7.0 and keeps a hook whose `_DTK_SUBCOMMANDS` tuple
has no `list package`. The filter is installed, correct, and unreachable. Nothing reports this.

This is §7's failure mode — "the new filter sees 0% adoption with no error anywhere" — displaced
from dtk's internal copies of the subcommand list, which §7 fixed, onto the copies already
deployed on users' machines, which it did not.

The same applies to `.claude/skills/dotnet-token-killer/SKILL.md`, whose frontmatter `description:`
is derived from the subcommand list (`ClaudeCodeIntegrator.cs:52`) precisely because it is the
string that decides whether the skill fires for a given intent. A stale skill silently fails to
surface for package-listing intent.

**2. `doctor` does not check the most fragile thing.** It verifies the SDK, the config file, the
tracking database, and the tee directory (`DoctorUseCase.cs:29-34`) — four things that rarely
break. It does not check whether the hook is installed, whether it is registered in the provider's
`settings.json`, whether the interpreter it shells out to resolves, or whether its content is
current. All three hooks are Python invoked by an absolute-ish command string; every one of those
is a live failure mode, and all of them are silent.

**3. Docs drift.** Four user-facing surfaces shipped since v0.6.0 are under-documented:
`gain --coverage` appears nowhere in `docfx/articles/token-analytics.md`; `pipe` and `log` appear
in neither `docfx/articles/getting-started.md` nor `docfx/index.md`; README's "Features at a
Glance" — the text NuGet renders — lists none of the three. Nothing fails when a command is added
without documenting it, which is how all four happened at once.

## Solution

### Provenance stamping

Every artifact dtk generates in full gains one trailing comment line carrying a SHA-256 over
everything above it:

```python
# ... end of hook script
# dtk-generated sha256:9f3ac1d2e4b7a05c...
```

Scope is the files dtk owns end to end: the three hook scripts (Claude Code, Gemini CLI, GitHub
Copilot CLI) and Claude's `SKILL.md`. Instruction files (`CLAUDE.md`, `AGENTS.md`, `.aider.conf.yml`
and friends) are *not* stamped: they are user-owned prose carrying a dtk-managed `<!-- dtk -->`
section, and `WriteSectionBasedFileAsync` already merges that section correctly under `--force`.

Two properties of the stamp are deliberate.

**Last line, not first.** A leading stamp would have to sit below the Python shebang and below the
SKILL.md YAML frontmatter — two different positional rules, each with its own parsing. A trailing
line makes the only per-artifact difference the comment syntax: `# …` for hooks, `<!-- … -->` for
markdown.

**No version number.** Freshness is decided by comparing the installed body against the current
template, never against a version string. The hash answers a different question — *is this body
authentic dtk output, or did someone edit it?* — and a version would add nothing to that. It would,
however, change `.claude/hooks/dotnet-to-dtk.py` in this repository on every release, breaking the
test that locks that file byte-identical to the generated template for reasons unrelated to the
hook's content.

Hashing normalises line endings to LF before digesting, so a CRLF checkout on Windows does not read
as tampering.

### Refresh semantics

A new `IntegratorHelpers.WriteGeneratedFileAsync(path, body, style, context, cancellationToken)`
serves stamped artifacts. `WriteFileAsync` keeps its current contract for everything else.

| Existing file state | no `--force` | `--force` |
|---|---|---|
| missing | write → Created | write → Created |
| body matches current template | no write → **Unchanged** | no write → Unchanged |
| stamp valid, body differs (older dtk output) | **write → Updated** | write → Updated |
| unstamped, matches provider signature (legacy) | **write → Updated** + note | write → Updated |
| stamp invalid, or unstamped and unrecognised | no write → Skipped + note | write → Updated |

The third row is the fix: an untouched artifact from an older dtk is refreshed without asking,
because overwriting bytes dtk itself wrote destroys nothing.

**The legacy row exists because the fix would otherwise not fire in v0.7.0.** No artifact installed
by 0.6.0 or earlier carries a stamp, so without it every existing user would land in the last row
and auto-refresh would only begin paying off at 0.8.0 — a fix that does not fix the release it
ships in. An unstamped file matching a provider signature (`_DTK_SUBCOMMANDS` for hooks,
`name: dotnet-token-killer` for SKILL.md) is therefore treated as legacy dtk output and refreshed,
with the paths listed in the result notes so the action is visible rather than silent.

The trade-off is explicit: a user who hand-edited an unstamped hook loses that edit once. It is
accepted because these files are dtk-generated, are conventionally under version control, the
overwrite is reported, and the branch becomes unreachable as soon as one stamped generation is
installed.

### `IntegrationResult.UnchangedFiles`

"Already current" and "left alone because you edited it" are different outcomes with different
remedies, and today both land in `SkippedFiles`. `IntegrationResult` gains an `UnchangedFiles`
list; existing constructors are preserved and a new overload accepts it.

This is not bookkeeping for its own sake. `IntegrateCommand.PrintSummary` appends "use `--force` to
integrate into existing files" to skipped paths (`IntegrateCommand.cs:86`). Applied to a file that
is simply up to date, that hint is false — it invites a `--force` run that would change nothing.

### `doctor` hook checks

**A shared descriptor.** Each hook's script path, settings path, event key, hook command, template
body and stamp style are private constants inside each integrator, assembled inline into a
`HookSpec`. They are lifted into `DescribeHooks(directory, scope)` on a new internal
`IHookIntegrator`, implemented by the three hook-installing integrators and consumed by both the
write path and doctor.

This is the §7 discipline applied again: a diagnostic that keeps its own copy of where the hook
lives will eventually check a path integrate no longer writes, and will do so while reporting
success.

**Discovery.** Doctor enumerates hook-capable integrators × {project = current directory, global =
home}, treating *script file present* as "integrated here". When nothing is found in either scope
it emits a single passing informational check — dtk works without hooks, so their absence is not a
failure.

**Two checks per installation**, which keeps the output readable at the one or two installations a
real machine has:

- **status** — registered in the provider's `settings.json`, and stamp state. Stale fails with the
  remedy `dtk integrate <provider>` (no `--force`, now that refresh is automatic); user-modified
  fails with the `--force` form.
- **probe** — runs the *installed* script through its real interpreter and asserts the rewrite
  comes back.

**The probe.** The interpreter is the first token of the `command` registered in `settings.json`,
falling back to `python3`. That matters on Windows, where the SKILL.md itself tells users to change
`python3` to `python` in `settings.json`: probing with a hardcoded `python3` would fail on a working
installation, and probing with the registered command is also the only way to prove *that specific
command* resolves.

The payload's command chains every canonical subcommand:

```
dotnet build; dotnet clean; dotnet format; dotnet list package; dotnet restore; dotnet test
```

One subprocess covers the whole list, and a 0.6.0-era hook fails on `list package` specifically.
Copilot CLI answers `permissionDecision: "ask"` for compound commands by design; the assertion
therefore reads the rewritten command out of the payload, not the decision. The probe is skipped
when the status check already found no registration, so one root cause produces one failure.

**Runner capability.** `ICommandRunner` cannot write to a child's stdin (`ICommandRunner.cs:10`).
It gains `RunCapturedWithInputAsync(command, args, stdin, cancellationToken)`, implemented in
`ProcessCommandRunner` with `RedirectStandardInput`, and run under a ~10 second linked-token timeout
so a wedged interpreter cannot hang `doctor`. Test doubles implementing the interface gain the
member.

### Docs

| File | Change |
|---|---|
| `README.md` | `pipe`, `log`, `gain --coverage` in "Features at a Glance"; Diagnostics section refreshed with the new doctor output |
| `docfx/articles/token-analytics.md` | `gain --coverage`: the `Source` column, and the passthrough / degraded / filter-faulted outcomes |
| `docfx/articles/getting-started.md`, `docfx/index.md` | `pipe` and `log` |
| `docfx/articles/ai-agent-setup.md` | "Upgrading dtk" — re-run `dtk integrate <provider>` (self-refreshing) or `dtk doctor` after an upgrade |

**`DocsBindingTests`**, in `DotnetTokenKiller.Cli.IntegrationTests` — the project that already has
`InternalsVisibleTo` for `CliConfigurator` and already locates the repository root for the committed
hook's binding test. Two rules:

1. Every command name registered in `CliConfigurator`, branches and leaves alike (`dotnet build`,
   `list package`, `config set`, `pipe`, `log`, …), appears in `README.md` and in at least one
   `docfx/articles/*.md`.
2. Every long-form (`--name`) `[CommandOption]` on those commands' settings appears in at least one
   `docfx/articles/*.md`.

A short allowlist covers names that are deliberately undocumented, each entry carrying a comment
saying why. The allowlist is the pressure valve that keeps the rule honest: without one, the first
inconvenient failure gets the whole test disabled. Any command or option the new rules flag that is
*not* deliberately undocumented gets documented as part of this work — the flagged set is expected
to be small, since the four known gaps were found by hand, but it is discovered at implementation
time rather than assumed here.

## Architecture

### Components

| Component | Layer | Responsibility |
|---|---|---|
| `ArtifactStamp` / `ArtifactStamping` | Application/Integration | Apply, parse, and verify the provenance line; recognise legacy signatures |
| `StampStyle` | Application/Integration | Comment syntax per artifact kind (`#` / `<!-- -->`) |
| `WriteGeneratedFileAsync` | Application/Integration | The decision table above |
| `IHookIntegrator.DescribeHooks` | Application/Integration | One description of a hook installation, shared by installer and diagnostic |
| `HookHealthChecker` | Application | Discovery, status check, probe → `DiagnosticCheck` list |
| `DoctorUseCase` | Application | Unchanged role: aggregates checks; gains the hook checker as a dependency |
| `RunCapturedWithInputAsync` | Domain / Infrastructure | Run a child process with stdin |
| `IntegrationResult.UnchangedFiles` | Domain | Separates "already current" from "left alone" |

`DoctorUseCase` stays an aggregator; the hook logic lives in its own type so it can be tested
against temp directories and a fake runner without dragging SDK and config probing along.

### Data flow

*Integrate:* command → integrator → `DescribeHooks` → `WriteGeneratedFileAsync` per artifact →
stamp applied → `IntegrationContext` buckets → `IntegrationResult` → `PrintSummary`.

*Doctor:* command → `DoctorUseCase` → existing four checks, plus `HookHealthChecker` → for each
integrator × scope, `DescribeHooks` → file present? → status check (settings parse + stamp verify)
→ probe (`RunCapturedWithInputAsync`) → `DiagnosticCheck` list → rendered rows.

## Error handling

- **Unreadable or malformed `settings.json`** — the status check fails with the parse error rather
  than throwing out of `doctor`. `MergeJsonSettingsAsync` already throws `InvalidOperationException`
  on the write path; the read path must not inherit that, since a diagnostic that crashes on a
  broken config is useless exactly when it is needed.
- **Interpreter missing** — the probe fails with the interpreter name and the remedy, which is the
  concrete form of "is `python3` on PATH".
- **Probe timeout** — reported as a failure naming the timeout, not as a hang.
- **Malformed or truncated stamp** — treated as "not authentic": the file is skipped without
  `--force`. Failing closed here costs a `--force`; failing open costs a lost edit.
- **Unwritable target directory** — unchanged from today's behaviour.

## Testing

- *Unit, stamping* — apply/read round-trip; LF normalisation under a CRLF body; tamper detection;
  legacy signature recognition; malformed stamp treated as unauthentic.
- *Unit, write path* — every cell of the decision table, asserting the reported bucket
  (Created / Updated / Unchanged / Skipped) as well as the bytes on disk.
- *Unit, health checker* — healthy; missing script; script present but unregistered; stale;
  user-modified; malformed settings; no integration in either scope.
- *Integration, real interpreter* — one probe against the generated hook using the Python on PATH,
  skipped with a stated reason when absent. No test currently executes the hooks at all — they are
  asserted only as text — so this closes a real hole: a generated script that Python refuses to
  parse would today pass every test in the suite.
- *Regression* — the committed `.claude/hooks/dotnet-to-dtk.py` gains its stamp, and the existing
  byte-identical binding test compares against stamped output.
- *Docs* — `DocsBindingTests` as specified.

## Sequencing

1. Stamping + `WriteGeneratedFileAsync` + `UnchangedFiles`. Self-contained; delivers the upgrade
   fix on its own.
2. `IHookIntegrator.DescribeHooks`, `RunCapturedWithInputAsync`, `HookHealthChecker`, doctor wiring.
   Depends on 1 for the stamp verification it reports.
3. Docs and `DocsBindingTests`. Depends on 2 only because the README Diagnostics section quotes
   doctor's output.

## Out of scope

- Stamping the `<!-- dtk -->` sections inside user-owned instruction files.
- A `dtk integrate --check` / dry-run mode. `doctor` now answers that question.
- Automatic re-integration on dtk upgrade without the user running anything. dtk has no upgrade
  hook, and writing into a project's configuration as a side effect of an unrelated command is a
  worse default than reporting it.
