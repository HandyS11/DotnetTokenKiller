# dtk's rules for every Claude Code agent — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `dtk init claude` registers `SessionStart` and `SubagentStart` hooks whose `dtk hook claude` reply injects four lines of dtk rules into every Claude Code agent working in a .NET project, and the same lines lead every other harness's dtk instructions.

**Architecture:** One `dtk hook claude` command serves three events; `HookPayloads` branches on `hook_event_name` only for the two new ones and leaves every other payload on today's rewrite path. A bounded `DotnetProjectDetector` decides whether to answer. `ClaudeCodeIntegrator` writes, removes and (through `IHookApprovalInspector`) checks the three registrations; `HookHealthChecker` probes the context reply.

**Tech Stack:** .NET 10, C#, System.Text.Json `JsonNode` (AOT-safe), xunit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-11-22-agent-context-hooks-design.md`

## Global Constraints

- `dtk hook` always exits 0 and prints nothing on any failure (Cursor's `{}` excepted), and stays on `HookEntryPoint`'s fast path: no DI, config, tracking or tokenizer.
- Every payload not named `SessionStart` or `SubagentStart` — `PreToolUse`, no event name, another harness's event name — keeps today's behaviour byte for byte.
- The registered command stays exactly `dtk hook claude` for all three events.
- The rules stay at four lines (one lead line, three bullets).
- `TreatWarningsAsErrors`, the Sonar parity rules (S3776 ≤ 15, S1192), `IsAotCompatible`: no reflection-based JSON.
- Text containing `dotnet build`/`dotnet test` goes into files with Write/Edit and into commits with `git commit -F <file>`: the installed hook rewrites such text inside Bash commands, heredocs included.
- Tests run with `dtk dotnet test <project> --filter "FullyQualifiedName~<Class>"`; the CLI integration suite runs in the foreground (about 2 min).

## Review Focus

1. A `cwd` at the root of a huge non-.NET tree: the detector gives up after its entry budget and answers "not .NET" quickly, never hangs (Task 1 test `IsDotnetProject_BudgetSpentBeforeAMatch_IsFalse`).
2. A directory symlink loop below `cwd`: the walk does not follow reparse points (Task 1 test `IsDotnetProject_DoesNotFollowDirectorySymlinks`, Unix only).
3. Cursor's or Devin's import running `dtk hook claude` with its own event name (`sessionStart`, `preToolUse`): no context reply, and a payload with a command is still rewritten (Task 2 test `Claude_OtherEventName_KeepsTodaysRewrite`).
4. A `settings.json` that already holds the user's own `SessionStart`/`SubagentStart` hooks: dtk's entries are appended beside them, and uninstall removes only dtk's (Task 3 test `Integrate_KeepsTheUsersOwnContextHooks`).
5. A `cwd` that is missing, relative, a file, or unreadable: no reply, no exception (Task 1 theory `IsDotnetProject_UnusableDirectory_IsFalse`, Task 2 test `Claude_ContextEventWithoutAUsableCwd_RepliesNothing`).

---

### Task 1: The rules text and .NET project detection

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetProjectDetector.cs`
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs` (add the rules; `UsageBody` is untouched until Task 6)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetProjectDetectorTests.cs`

**Interfaces:**
- Produces: `internal static string IntegrationInstructions.AgentRuleBullets` (the three bullet lines, `\n`-separated, no trailing newline) and `internal static string IntegrationInstructions.AgentRules` (lead line + `\n` + `AgentRuleBullets`). Declared before `UsageBody` (static initialisation order).
- Produces: `internal static class DotnetProjectDetector` with `internal const int DefaultEntryBudget = 2000;`, `internal static bool IsDotnetProject(string? directory)` and `internal static bool IsDotnetProject(string? directory, int entryBudget)`.

The rules, verbatim (Task 6 reuses `AgentRuleBullets`):

```text
dtk (DotnetTokenKiller) filters this project's dotnet commands; a hook runs `dotnet build|test|…` through it.
- Run them plainly: dtk's output is already short and its last line is the verdict. Don't pipe it into `tail` or `grep`; `--max-lines N` caps it.
- To see more of a run, or what a cap cut, use `dtk log build` / `dtk log test` (`--full` for all) instead of running it again.
- `dtk dotnet test --names` lists the tests that passed; `DTK_RAW=1` gives dotnet's own output.
```

- [ ] **Step 1: Write the failing tests** (temp-directory fixture as in `GitWorkingTreeTests`)

```csharp
[Theory]
[InlineData("App.csproj")] [InlineData("App.fsproj")] [InlineData("App.vbproj")]
[InlineData("App.sln")] [InlineData("App.slnx")] [InlineData("App.slnf")]
[InlineData("global.json")] [InlineData("Directory.Build.props")]
public void IsDotnetProject_MarkerInTheDirectory_IsTrue(string marker)   // file in _root → IsDotnetProject(_root) true

[Fact] public void IsDotnetProject_MarkerInAnAncestor_IsTrue()            // _root/App.slnx, ask _root/src/App/Sub
[Fact] public void IsDotnetProject_ProjectTwoLevelsDown_IsTrue()          // _root/backend/Api/Api.csproj, ask _root
[Fact] public void IsDotnetProject_ProjectThreeLevelsDown_IsFalse()       // _root/a/b/c/X.csproj
[Theory]
[InlineData(".hidden")] [InlineData("bin")] [InlineData("obj")] [InlineData("node_modules")] [InlineData("artifacts")]
public void IsDotnetProject_ProjectOnlyInASkippedFolder_IsFalse(string folder)   // _root/<folder>/X.csproj
[Fact] public void IsDotnetProject_GlobalJsonBelow_IsFalse()              // below cwd only solution/project files count
[Fact] public void IsDotnetProject_NoMarker_IsFalse()                     // _root holds README.md, package.json, src/index.ts
[Fact] public void IsDotnetProject_BudgetSpentBeforeAMatch_IsFalse()      // 30 empty dirs, then z/X.csproj; budget 10 → false; DefaultEntryBudget → true
[Fact] public void IsDotnetProject_DoesNotFollowDirectorySymlinks()       // Unix: _root/a/loop → _root, no marker; returns false, terminates
[Theory]
[InlineData(null)] [InlineData("")] [InlineData("relative/path")]
public void IsDotnetProject_UnusableDirectory_IsFalse(string? directory)
[Fact] public void IsDotnetProject_MissingOrFileDirectory_IsFalse()       // _root/missing, and the path of a file
[Fact] public void AgentRules_AreFourLines() =>
    IntegrationInstructions.AgentRules.Split('\n').Should().HaveCount(4);
```

- [ ] **Step 2: Run them to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~DotnetProjectDetectorTests"`
Expected: build error (no `DotnetProjectDetector`, no `AgentRules`).

- [ ] **Step 3: Implement**

Ancestors (the directory itself and up to the root): any file whose extension is one of `.sln .slnx .slnf .csproj .fsproj .vbproj`, or named `global.json` / `Directory.Build.props` (ordinal-ignore-case on Windows and macOS, ordinal on Linux, as `WorkTreeChanges.Comparer`); ancestor enumeration does not count toward the budget. Below: breadth-first to depth 2 (`directory/x` and `directory/x/y`), solution/project extensions only; every enumerated entry counts one against the budget; skip names starting with `.`, and `bin`, `obj`, `node_modules`, `artifacts` (ordinal-ignore-case); `EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }`. A `directory` that is null, empty, not fully qualified or not an existing directory is false; `IOException`, `UnauthorizedAccessException` and `SecurityException` mean "no match here".

- [ ] **Step 4: Run them to see them pass** — same command, all green.

- [ ] **Step 5: Commit** — `feat: detect a .NET project for the agent context hooks`, together with the spec and this plan.

### Task 2: `dtk hook claude` answers SessionStart and SubagentStart

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/HookPayloads.cs` (`HookPayloadKind.ClaudeCode` arm)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`, `tests/DotnetTokenKiller.Cli.IntegrationTests/HookEntryPointTests.cs`

**Interfaces:**
- Consumes: `DotnetProjectDetector.IsDotnetProject(string?)`, `IntegrationInstructions.AgentRules` (Task 1).
- Produces: reply `{"hookSpecificOutput":{"hookEventName":"<SessionStart|SubagentStart>","additionalContext":"<AgentRules>"}}`; `internal static readonly FrozenSet<string> HookPayloads.AgentTypesWithoutShell` = `web-fetch`, `statusline-setup`.

- [ ] **Step 1: Write the failing tests** (a temp dir with `App.csproj` as `cwd`; payloads built with `JsonObject` so the path escapes correctly)

```csharp
[Theory] [InlineData("SessionStart")] [InlineData("SubagentStart")]
public void Claude_ContextEventInADotnetProject_InjectsTheRules(string eventName)
    // hookSpecificOutput.hookEventName == eventName; additionalContext == IntegrationInstructions.AgentRules
[Theory] [InlineData("SessionStart")] [InlineData("SubagentStart")]
public void Claude_ContextEventOutsideADotnetProject_RepliesNothing(string eventName)   // Reply(...) is null
[Theory] [InlineData("web-fetch")] [InlineData("statusline-setup")]
public void Claude_SubagentWithoutAShell_GetsNoRules(string agentType)                  // null
[Theory] [InlineData("general-purpose")] [InlineData("Explore")] [InlineData("Plan")] [InlineData("feature-dev:code-reviewer")]
public void Claude_AnyOtherSubagent_GetsTheRules(string agentType)
[Fact] public void Claude_ContextEventWithoutAUsableCwd_RepliesNothing()   // no cwd; cwd 42; cwd "relative"
[Theory] [InlineData("sessionStart")] [InlineData("preToolUse")] [InlineData("PreToolUse")]
public void Claude_OtherEventName_KeepsTodaysRewrite(string eventName)
    // with tool_input.command "dotnet build" → updatedInput.command "dtk dotnet build"; without tool_input → null
[Fact] public void Claude_ContextEventWithACommand_IsNotRewritten()        // SessionStart + tool_input → context reply only
```

In `HookEntryPointTests` (real binary): `Hook_Claude_SubagentStartInADotnetProject_PrintsTheRules` and `Hook_Claude_SessionStartElsewhere_PrintsNothing`, exit code 0 both.

- [ ] **Step 2: Run to see them fail**

Run: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~HookPayloadsTests"`
Expected: the context tests fail (today's path returns null or rewrites); the `OtherEventName` ones already pass.

- [ ] **Step 3: Implement `private static string? ReplyToClaude(JsonNode? root)`**: read `hook_event_name` as a string; exactly `SessionStart` or `SubagentStart` → context reply (string `cwd`, `agent_type` not in `AgentTypesWithoutShell`, `IsDotnetProject(cwd)`), else `ReplyWithUpdatedInput(root, expectedTool: null)` as today. Wire it into the `ClaudeCode` arm only; Devin and Droid keep `ReplyWithUpdatedInput`.

- [ ] **Step 4: Run both test classes** — `HookPayloadsTests` green; then `dtk dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests --filter "FullyQualifiedName~HookEntryPointTests"` green.

- [ ] **Step 5: Commit** — `feat: dtk hook claude injects dtk's rules at session and subagent start`

### Task 3: `dtk init claude` registers, and `--uninstall` removes, the context hooks

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs`, `tests/DotnetTokenKiller.Application.Tests/Integration/UninstallIntegrationTests.cs`

**Interfaces:**
- Produces: `private static HookRegistrationSpec[] Registrations(HookInstallation hook)` returning, in order, `(PreToolUse, "Bash")`, `(SessionStart, "startup|clear|compact")`, `(SubagentStart, "*")`, all with `hook.Command`, the same `RegistrationPath`, no timeout. `internal const string SessionStartMatcher = "startup|clear|compact";` (Task 4 may add `resume`).

- [ ] **Step 1: Write the failing tests** (project and global scope)

```csharp
[Fact] public async Task Integrate_RegistersTheHookForThreeEvents()
    // settings.json: hooks.PreToolUse[0] matcher "Bash", hooks.SessionStart[0] matcher "startup|clear|compact",
    // hooks.SubagentStart[0] matcher "*"; each entry's hooks[0] is {"type":"command","command":"dtk hook claude"}
[Fact] public async Task Integrate_OnAnInstallWithOnlyPreToolUse_AddsTheContextHooks()   // pre-seeded file; result lists it Updated
[Fact] public async Task Integrate_Twice_LeavesTheFileUnchanged()                         // second run: Unchanged, bytes equal
[Fact] public async Task Integrate_ListsTheSettingsFileOnce()                             // Created/Updated hold the path once
[Fact] public async Task Integrate_KeepsTheUsersOwnContextHooks()                         // user's SessionStart "echo hi" kept, dtk's appended; uninstall leaves "echo hi"
[Fact] public async Task Uninstall_RemovesAllThreeRegistrations()                         // no string "dtk hook claude" left in the file
```

`UninstallIntegrationTests`' Claude round trip must still restore the whole temp tree in both scopes.

- [ ] **Step 2: Run to see them fail** — `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~ClaudeCodeIntegratorTests"`; the three-event and add tests fail.

- [ ] **Step 3: Implement** — install merges the three specs in order (the legacy-script retirement keeps using the `PreToolUse` merge's result); uninstall removes the three. If the context accumulators list the file once per merge, deduplicate where `IntegrationContext` records it.

- [ ] **Step 4: Run** `ClaudeCodeIntegratorTests` and `UninstallIntegrationTests` — green.

- [ ] **Step 5: Commit** — `feat: dtk init claude registers the SessionStart and SubagentStart hooks`

### Task 4: Gate — real Claude Code, go/no-go (foreground)

No product code unless the `resume` check fails. Scratch directory: this session's scratchpad.

- [ ] **Step 1:** Build the CLI (`dtk dotnet build src/DotnetTokenKiller.Cli`), make `<scratch>/bin/dtk` a symlink to `src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk`, and create `<scratch>/gate-dotnet` (an empty `Gate.csproj`, `git init`) and `<scratch>/gate-plain` (a `README.md`, `git init`). Run `<scratch>/bin/dtk init claude --dir <dir>` in both.
- [ ] **Step 2:** In `gate-dotnet`, with `PATH=<scratch>/bin:$PATH`: `claude -p --output-format json "<prompt>"`, the prompt asking it to quote any instruction in its context containing "instead of running it again", then to spawn a general-purpose and an Explore subagent with the same request and quote their answers. Pass: the main answer and both subagent answers quote the rules. Keep the `session_id`.
- [ ] **Step 3:** Same in `gate-plain`. Pass: no agent quotes them.
- [ ] **Step 4:** `claude -p --resume <session_id> "<same question, main agent only>"` in `gate-dotnet`. Pass: still quoted. Otherwise add `resume` to `SessionStartMatcher` (update Task 3's test), rebuild, and repeat Step 4.
- [ ] **Step 5:** If Step 2 fails for the subagents: stop, write down what each agent saw, and report to the user before any later task. Otherwise record the three results (and the `resume` decision) for the PR description in `<scratch>/gate-results.md`.

### Task 5: Doctor checks the context hooks and probes the reply

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/ClaudeCodeIntegrator.cs` (implement `IHookApprovalInspector`), `src/DotnetTokenKiller.Application/UseCases/HookHealthChecker.cs`
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/ClaudeCodeIntegratorTests.cs`, `tests/DotnetTokenKiller.Application.Tests/UseCases/HookHealthCheckerTests.cs`

**Interfaces:**
- Consumes: Task 3's registrations.
- Produces: `ClaudeCodeIntegrator.InspectApproval(HookInstallation, string)` → one `HookApprovalFinding("context hooks", satisfied, message)`. Satisfied when both `hooks.SessionStart[].hooks[].command` and `hooks.SubagentStart[].hooks[].command` include `dtk hook claude`: `SessionStart and SubagentStart registered: agents in .NET projects get dtk's rules`. Otherwise: `<missing events, joined by " and "> not registered in <path>: agents will not get dtk's rules. Run 'dtk init claude[ --global]'.` (the scope flag as `RemedyCommand` renders it).
- Produces: a `claude context probe (<scope>)` check from `HookHealthChecker` for `HookPayloadKind.ClaudeCode` registrations that are current or modified: a temp directory holding `probe.csproj`, payload `{"hook_event_name":"SubagentStart","agent_type":"general-purpose","cwd":<temp>}`, pass when stdout contains `additionalContext`; failure message `<dtk> does not inject dtk's rules. It is older than this dtk; run 'dotnet tool update -g DotnetTokenKiller'.`; the temp directory is deleted either way.

- [ ] **Step 1: Write the failing tests** — `InspectApproval_BothContextHooksRegistered_IsSatisfied`, `InspectApproval_OnlyPreToolUse_NamesBothMissingEventsAndTheRemedy` (project and `--global` remedy), `InspectApproval_UnreadableFile_IsNotSatisfied`; in `HookHealthCheckerTests` (its fake runner): `Claude_ContextProbe_PassesOnAContextReply`, `Claude_ContextProbe_FailsOnNoReply`, `NonClaudeHooks_GetNoContextProbe`.
- [ ] **Step 2: Run to see them fail** — the two test classes.
- [ ] **Step 3: Implement** as the Interfaces block says; reading the file goes through `IntegratorHelpers.LenientJson`, and any read or parse failure is an unsatisfied finding, never an exception.
- [ ] **Step 4: Run** — green.
- [ ] **Step 5: Commit** — `feat: dtk doctor checks the Claude Code context hooks`

### Task 6: Every harness's instructions lead with the same rules

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs` (`UsageBody`, the two hash lists)
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/ReleasedOwnedFileHashesTests.cs`, and whatever pins the rendered text (`SharedInstructionArtifactsTests`, `SubcommandBindingTests`, `GitHubCopilotIntegratorTests`, the docs binding tests)

**Interfaces:**
- Consumes: `IntegrationInstructions.AgentRuleBullets` (Task 1).

- [ ] **Step 1: Capture the released bodies.** In `ReleasedOwnedFileHashesTests`, add `V0110MarkdownRule` and `V0110CursorRule` constants holding, byte for byte, the Devin/Aider rule and the Cursor rule the current code renders. Confirm they equal what the released packages write: dtk 0.11.0 (installed globally) and 0.12.0 (`dotnet tool install DotnetTokenKiller --version 0.12.0 --tool-path <scratch>/dtk-0.12.0`), each running `dtk init cursor --dir <tmp>`, `dtk init devin --dir <tmp>` and `dtk init aider --dir <tmp>`. Their hashes must equal the current pins `babe8c7a…53d8` and `2b666a47…6180`.
- [ ] **Step 2: Write the failing test** `ReleasedHashes_IncludeTheV0110Bodies` (pattern of `ReleasedHashes_IncludeTheV0100Bodies`); run it; it fails.
- [ ] **Step 3: Append** the two hashes, each with the comment `// 0.11.0–0.12.0`; the test passes.
- [ ] **Step 4: Change `UsageBody`**: `AgentRuleBullets` becomes the first three bullets, replacing the `Don't pipe dtk output into tail or grep…` and `` `dtk log [build|test]` prints… `` bullets; the rest keeps its order. `UsageBody` turns from `const` to `static readonly`, declared after `AgentRuleBullets`.
- [ ] **Step 5: Run the Application suite**: `dtk dotnet test tests/DotnetTokenKiller.Application.Tests`. Expected failures: `CurrentCursorRule_MatchesThePinnedHash` and `CurrentDevinRule_MatchesThePinnedHash`, plus any test pinning the old bullets' text. Re-pin the two current hashes to the new bodies; update the other pins to the new text, never by weakening an assertion.
- [ ] **Step 6: Run it again** — green. **Commit** — `feat: every harness's dtk instructions lead with the agent rules`

### Task 7: The skill becomes the on-demand reference

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/SharedInstructionArtifacts.cs` (`SkillDescription`, `SkillMarkdown`), `src/DotnetTokenKiller.Application/Integration/IntegrationInstructions.cs` (split `UsageBody`'s reference bullets out if the skill reuses them)
- Test: `tests/DotnetTokenKiller.Application.Tests/SubcommandBindingTests.cs` (`ClaudeSkill_EmbedsTheSharedUsageBody` and the description pin), `tests/DotnetTokenKiller.Application.Tests/Integration/SharedInstructionArtifactsTests.cs`

**Interfaces:**
- Consumes: Task 6's `UsageBody` (the AGENTS.md section keeps embedding it whole).
- Produces: the skill's frontmatter `name: dotnet-token-killer` unchanged (Claude Code's lookup and `SkillLegacySignature` depend on it); description and body as below.

Description, verbatim (one line in the frontmatter, single-quoted as today):

```text
Reference for DotnetTokenKiller (dtk) options beyond plain use: keeping a run's raw output (--tee), a failing test's console output (--show-output), the tests that passed (--names), failing a run that matched no test (--fail-on-empty), capping output (--max-lines), a run's saved raw output (dtk log), filtering saved output (dtk pipe), every warning of a build, and token savings (dtk gain).
```

- [ ] **Step 1: Write the failing tests** — `Skill_Description_NamesTheOptionsBeyondPlainUse` (frontmatter description equals the text above); `Skill_Body_LeavesTheRulesToTheContext` (body contains none of `AgentRuleBullets`' lines and no `## Installation`); `Skill_Body_ExplainsTheChangedFilesNarrowing` (contains `in unchanged files not shown` and `dtk log build --full | dtk pipe build`); `Skill_Body_KeepsTheReference` (contains `--tee`, `--show-output`, `--fail-on-empty`, `--names`, `--max-lines`, `dtk pipe`, `DTK_RAW=1`, `dtk gain`). Replace `ClaudeSkill_EmbedsTheSharedUsageBody` with a pin of the new composition, and the description's subcommand pin with the test above.
- [ ] **Step 2: Run to see them fail** — `dtk dotnet test tests/DotnetTokenKiller.Application.Tests --filter "FullyQualifiedName~SubcommandBindingTests|FullyQualifiedName~SharedInstructionArtifactsTests"`.
- [ ] **Step 3: Rewrite the skill** as the spec's *The skill* says. The changed-files line reads, in substance: in a git work tree a build without errors lists only the warnings of changed files and counts the rest (`N warnings in unchanged files not shown`); `dtk log build --full | dtk pipe build` lists them all; a change to a project file, `.props`/`.targets`, `.editorconfig` or `global.json`, or no `origin/HEAD`, lists every warning. Update the `SkillDescription` doc comment: the trigger is now the options, on purpose.
- [ ] **Step 4: Run the Application suite** — green (re-pin any other test that pinned the old skill text).
- [ ] **Step 5: Commit** — `feat: the dtk skill becomes the reference for options beyond plain use`

### Task 8: Imported hooks, docs, timing, and the PR

**Files:**
- Modify: `docs/articles/ai-agent-setup.md` (Claude Code § Installation, How It Works, Manual Installation; § What the agent is told), `docs/articles/harness-verification.md`, `docs/articles/performance.md` (§ dtk hook), `CHANGELOG.md` (Unreleased › Added), `CLAUDE.md` (the `dtk hook` paragraph)

- [ ] **Step 1: Imported hooks.** With the web-fetch agent, read Cursor's hooks docs (cursor.com/docs/hooks) and Devin's (docs.devin.ai) on which Claude Code hook events they import and how they handle the replies. Record in `harness-verification.md`: whether `SessionStart`/`SubagentStart` are imported, under which event name, and why dtk stays harmless (a non-matching event name keeps today's behaviour; a Claude-shaped context reply). If a harness would block or error on it, stop and report to the user.
- [ ] **Step 2: Timing.** 55 runs each, medians, like the existing § dtk hook figures: `dtk hook claude` answering `SessionStart` with `cwd` = this repository (a match), and with `cwd` = a large non-.NET tree such as `/usr/share` (budget exhausted). Add the dated figures to `performance.md`.
- [ ] **Step 3: Docs.** The skill's new role (reference, its trigger) wherever the docs describe the skill; `ai-agent-setup.md`: init registers `PreToolUse`, `SessionStart` (`startup|clear|compact`, or what Task 4 settled) and `SubagentStart`; the exact four lines it injects, only in a .NET project (the detection rule in one sentence); every subagent type except `web-fetch` and `statusline-setup`, custom agents without a shell included; re-running `dtk init claude` adds the two hooks to an existing install, and `dtk doctor` warns until then; the manual-installation JSON gains the two entries. CHANGELOG: one Added entry (the hooks) and one Changed entry (the skill's trigger and body). CLAUDE.md: one sentence on the three events and `DotnetProjectDetector`.
- [ ] **Step 4: Full verification.** The four test projects (`Domain`, `Infrastructure`, `Application`, `Cli.IntegrationTests`, the last in the foreground), `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`, and a re-run of Task 4 Step 2 against the final build.
- [ ] **Step 5: Commit and open the PR** against `develop`: title `feat: give every Claude Code agent dtk's rules through session and subagent hooks`; body (written with Write, `gh pr create --body-file`) with the gate results, the timing, the imported-hook findings, and no other project's name or private usage counts; it ends with the Claude Code attribution line.
