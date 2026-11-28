# Hook race and `| tail` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Under Claude Code, refuse a command that both rtk's and dtk's hooks would rewrite and hand back the merged
command. In every harness, turn `dotnet <filtered> … | tail -N` / `| head -N` into `--max-lines N`.

**Architecture:** `DotnetCommandRewriter` learns the tail/head conversion, which all harnesses get, and an
explicit-`dtk` mode, which only Claude Code uses. A new `RtkRewriteRace` decides, from an injected `IRtkRewriter`
(`ProcessRtkRewriter` runs `rtk rewrite` without a shell), whether a Claude `PreToolUse` reply becomes a `deny` that
carries the merged command. `HookEntryPoint` wires the real rtk peer. `DoctorUseCase`'s `rtk coexistence` check probes
the installed hook with a mixed command.

**Tech Stack:** C# / .NET 10, xUnit v3 + FluentAssertions + NSubstitute, `System.Text.Json.Nodes`, the `claude` CLI
(2.1.332) for the gates.

**Spec:** `docs/superpowers/specs/2026-11-27-field-report-0.14-design.md`, items 1 and 3. Branch:
`fix/hook-race-and-tail`, which already holds the spec as commit c21916b.

## Global Constraints

- `IsSimpleCommand` and `IsAutoApprovable` stay fail-closed and keep judging the **original** command. No reply may
  newly carry `"allow"`.
- Never block a command because of dtk's own trouble. When rtk is missing, slow (over **2 s**), exits otherwise than
  0/1/2/3, prints nothing or throws, the reply is the same as today.
- Never loop: refuse only when the merged command is stable under both rewrites.
- Codex's hook command and timeout do not change. `IntegrationInstructions.AgentRules` does not change.
- Start `rtk` by the absolute path `ExecutableSearch.FindOnProcessPath("rtk")` returns, never a bare `rtk`. Never use a
  shell. Remove `RTK_REWRITE_HOST` from the child's environment.
- The refusal's reason is exactly `dtk and rtk would each rewrite this command, and Claude Code applies only one hook's
  rewrite. Run this instead, which carries both:\n` followed by the merged command.
- Repo rules: `TreatWarningsAsErrors`, the Sonar parity rules, file-scoped namespaces, `var`, `_camelCase`, the
  `Async` suffix, LF line endings, `CultureInfo.InvariantCulture` for printed numbers.
- Build with `dtk dotnet build DotnetTokenKiller.slnx`, test with `dtk dotnet test DotnetTokenKiller.slnx` (one class:
  `--filter "FullyQualifiedName~ClassName"`), and verify formatting with `dtk dotnet format DotnetTokenKiller.slnx
  --no-restore --verify-no-changes`.
- Gates run in the foreground, one command at a time, each under 10 minutes. A background job dies when the session
  ends.
- The hook rewrites `dotnet build` even inside a heredoc or commit message sent through Bash, so write prose files
  with Write/Edit, not with `cat <<EOF`.

## Review Focus

1. **Mixed command whose `dotnet` sits in quoted text.** For `git commit -m "run dotnet build"` or `gh pr create
   --body "… dotnet build …"`, dtk rewrites nothing, so no refusal may fire. Task 5 adds
   `MergedCommand_DtkLeavesTheCommandAlone_AsksRtkNothing`, and Task 6 adds the end-to-end
   `Claude_DotnetOnlyInQuotedText_IsNeitherRewrittenNorRefused`.
2. **rtk hangs.** A broken install, or an rtk waiting on stdin, must not stall the agent beyond the 2 s timeout, and
   the reply then is today's. Task 4: `Rewrite_RtkOutlivesTheTimeout_ReturnsNullWithinTheTimeout`.
3. **Exact bytes.** A command holding quotes, `$`, `%`, `|` inside quotes, or non-ASCII must reach rtk, and come back
   in the reason, byte for byte. Task 4: `Rewrite_PassesTheCommandAsOneArgumentVerbatim`. Task 6:
   `Claude_RefusalReason_CarriesTheMergedCommandVerbatim`.
4. **`| tail -N` followed by anything but a terminator** (`| tail -5 > out.txt`, `| tail -5 2>&1`, `| tail -5 | wc
   -l`) keeps its pipe. Task 2 lists these in `Rewrite_PipeIntoTailOrHeadNotJustALineCount_KeepsThePipe`.
5. **Explicit `dtk` outside Claude Code.** Copilot CLI, Antigravity and the others reply about an explicit
   `dtk dotnet test | tail -5` exactly as today, which is no reply at all. Task 3:
   `Copilot_ExplicitDtkPipedIntoTail_PrintsNothing`.

---

### Task 1: Gate R-0. Does Claude Code apply a hook's `deny` over rtk's rewrite, and does the agent re-send?

A go/no-go before any code, using a stub in place of dtk's refusal. If it fails, stop and report to the user (the
spec's Gate R).

**Files:** scratch only, under `$S=<this session's scratchpad>/gate-r0`. When this plan was written, the scratchpad
was `/tmp/claude-1000/-home-cloudcli-projects-DotnetTokenKiller/3b78f719-cb34-4293-a69e-ca2dff0a0bbb/scratchpad`.
Nothing goes in the repo.

- [ ] **Step 1: Build the sandbox.** `dotnet new console -o $S/app`, then `git init -q` inside it. Write
  `$S/stub-hook.py`, run with `python3 -I`. It reads the payload from stdin. When `tool_input.command` contains
  `dotnet build` and not `dtk dotnet build`, it sets `r1 = command.replace("dotnet build", "dtk dotnet build")` and
  runs `rtk rewrite r1` (with `RTK_REWRITE_HOST` removed). `merged` is rtk's stdout on exit 0/3, else `r1`. It then
  prints the deny JSON with the exact reason from Global Constraints. Otherwise it prints nothing. Write
  `$S/app/.claude/settings.json` with two `PreToolUse` groups, matcher `Bash`: `rtk hook claude`, and
  `python3 -I $S/stub-hook.py`.
- [ ] **Step 2: Run in bypass mode.** From `$S/app`: `claude -p "Run exactly this Bash command, nothing else, then
  stop: dotnet build; git status --short" --setting-sources project --permission-mode bypassPermissions
  --output-format stream-json --verbose > $S/r0-bypass.jsonl`.
  Expected: the jsonl shows a Bash `tool_use` with the original command, its `tool_result` carrying the reason, then a
  second Bash `tool_use` whose command is `merged` exactly, and a build result.
- [ ] **Step 3: Run in default mode with the documented allow rules.** Add `"permissions":{"allow":["Bash(dotnet
  build:*)","Bash(git status:*)","Bash(rtk git status:*)","Bash(dtk dotnet:*)"]}` to the settings. Run Step 2's
  command without `--permission-mode`, into `$S/r0-default.jsonl`.
  Expected: as in Step 2, and the re-sent command runs, with no permission denial in the jsonl.
- [ ] **Step 4: Record the verdict.** Write a few lines into the scratchpad (`$S/VERDICT.md`): pass or fail, the
  Claude Code version, and quotes of the two `tool_use` commands. On fail, stop the plan and tell the user. On pass,
  continue. No commit.

### Task 2: `| tail -N` and `| head -N` become `--max-lines N`, for every harness

**Files:**
- Modify: `src/DotnetTokenKiller.Application/Integration/Hooks/DotnetCommandRewriter.cs`: `Rewrite` (:136-168), near
  `SendsOutputToATextFilterOrFile` (:472).
- Modify: `src/DotnetTokenKiller.Domain/PassthroughSubcommands.cs` and
  `src/DotnetTokenKiller.Cli/ArgumentPreprocessor.cs:192-204`.
- Test: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/DotnetCommandRewriterTests.cs`,
  `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/HookPayloadsTests.cs`, and the
  `PassthroughSubcommands` tests in `tests/DotnetTokenKiller.Domain.Tests`.

**Interfaces:**
- Produces: `public static bool PassthroughSubcommands.IsUnfilteredRun(IReadOnlyList<string> dotnetArgs)`, which is
  `IsInteractiveFilteredRun || IsMachineReadableRun || IsToolHelpRun`, exactly the three detectors
  `ArgumentPreprocessor.IsPassthrough` combines today. Its argument list starts at the subcommand. `IsPassthrough`
  calls it instead of its own `||`, so its result does not change, and its existing tests prove that. The rewriter
  checks `IsUnfilteredRun(…) || PromptsForConfirmation(…)`.
- Produces: `DotnetCommandRewriter.Rewrite(string command)` keeps its signature and its contract: the same reference
  back when nothing changes.

- [ ] **Step 1: Write the failing tests.** Add to `DotnetCommandRewriterTests`
  `[Theory] Rewrite_PipeIntoALineCountTailOrHead_BecomesMaxLines(string command, string expected)` with InlineData:
  - `dotnet test | tail -20` → `dtk dotnet test --max-lines 20`
  - `dotnet test --filter X 2>&1 | tail -n 30` → `dtk dotnet test --max-lines 30 --filter X 2>&1`
  - `dotnet build | head -5 && git add .` → `dtk dotnet build --max-lines 5 && git add .`
  - `dotnet build | tail -n5; echo done` → `dtk dotnet build --max-lines 5; echo done`
  - `dotnet test |& tail --lines=40` → `dtk dotnet test --max-lines 40`
  - `dotnet test | tail --lines 40 || true` → `dtk dotnet test --max-lines 40 || true`
  - `cd src && dotnet build | tail -3` → `cd src && dtk dotnet build --max-lines 3`
  - `dotnet test --filter "A|B" | tail -5` → `dtk dotnet test --max-lines 5 --filter "A|B"`
  - `dotnet jb cleanupcode App.slnx --no-build | tail -5` → `dtk dotnet jb cleanupcode --max-lines 5 App.slnx --no-build`
  - `(dotnet test | tail -5)` → `(dtk dotnet test --max-lines 5)`

  Add `[Theory] Rewrite_PipeIntoTailOrHeadNotJustALineCount_KeepsThePipe(string command, string expected)`. Each
  expected value is today's rewrite, `dtk dotnet …` with the pipe kept: `| tail -f`, `| tail -F`, `| tail -n +5`,
  `| head -n -5`, `| tail -c 100`, `| tail -5 log.txt`, `| tail -q -5`, `| tail -0`, `| tail -5 | wc -l`,
  `| tail -5 > out.txt`, `| tail -5 2>&1`, `dotnet test --max-lines 9 | tail -5`, `dotnet test --vv | tail -5`,
  `dotnet build --interactive | tail -5`, `dotnet build -getProperty:Version | tail -1`, `dotnet list package
  --format json | head -20`, `dotnet ef migrations --help | head -5`, `dotnet test $(cat args) | tail -5`.

  Add to `HookPayloadsTests`:
  - `Copilot_PipeIntoTail_StillAsks`: the reply's command is `dtk dotnet test --max-lines 5`, and its decision is
    `ask`.
  - `Cursor_PipeIntoTail_GetsTheNeutralReply`: `{}`.
- [ ] **Step 2: Run them to verify they fail.** `dtk dotnet test DotnetTokenKiller.slnx --filter
  "FullyQualifiedName~DotnetCommandRewriterTests|FullyQualifiedName~HookPayloadsTests"`.
  Expected: the new BecomesMaxLines cases fail with the pipe still present. The KeepsThePipe cases and both gating
  tests already pass; they pin today's behaviour.
- [ ] **Step 3: Implement.** In `Rewrite`, after a subcommand matched and `SendsOutputToATextFilterOrFile` returned
  false, read the first unquoted operator after the arguments with `ShellScanner`, as `SendsOutputToATextFilterOrFile`
  does. Convert only when all of these hold:
  - it is `|` or `|&` into the word `tail` or `head` (`ProgramName`);
  - its only operand is a count in one of the forms `-N`, `-n N`, `-nN`, `--lines=N`, `--lines N`, with N matching
    `^[1-9][0-9]*$`;
  - the next unquoted thing after it is the end, `;`, `&&`, `||`, `&` not followed by `>`, `\n` or `)`;
  - the arguments (the words between the subcommand's end and the pipe, split on blanks) contain no `--max-lines`,
    `--max-lines=…` or `--vv`;
  - `PassthroughSubcommands.IsUnfilteredRun(subcommand words + argument words)` and `PromptsForConfirmation(…)` are
    both false.

  Then emit ` --max-lines N` right after `command[subcommandStart..end]`, and drop everything from the blanks before
  the `|` through the count's last character. A substitution anywhere in the arguments means no conversion.
- [ ] **Step 4: Add `IsUnfilteredRun` and use it in `ArgumentPreprocessor.IsPassthrough`.** Add one Domain test,
  `IsUnfilteredRun_IsAnyOfTheThreeDetectors`. Add one rewriter case to the KeepsThePipe list, `dotnet ef database
  drop | tail -5`, for `PromptsForConfirmation`.
- [ ] **Step 5: Run the tests to verify they pass.** Run the Step 2 command, plus
  `--filter "FullyQualifiedName~ArgumentPreprocessor|FullyQualifiedName~PassthroughSubcommands"`.
  Expected: all pass.
- [ ] **Step 6: Docs.** In `CLAUDE.md`, the hook paragraph at :93-98 currently says "`| tail`, `| head` and `| tee`
  are still rewritten". Change it to say the tail/head line-count forms become `--max-lines N` with the pipe dropped,
  that the command then exits with dotnet's code, and what keeps the pipe. Make the same change in
  `docs/articles/usage.md:581` (the rewrite-rule paragraph) and at `--max-lines` (:507-509): the first N lines plus
  the status line, where `tail` gave the last N. In `CHANGELOG.md` `[Unreleased]` `### Changed`, add one entry: it
  states the exit-code change (`… | tail -5 && git add` now stops on a failure) and the head-versus-tail difference.
- [ ] **Step 7: Commit.** `git commit -m "feat: turn | tail -N and | head -N on a filtered dotnet command into
  --max-lines N"`, with the attribution line from the session's instructions.

### Task 3: Explicit `dtk dotnet …` / `dtk <sub> …` piped into `tail`, for Claude Code only

**Files:**
- Modify: `DotnetCommandRewriter.cs` (the early `Contains("dotnet")` return at :140, `IsCommandPosition` :442) and
  `HookPayloads.cs` (`ReplyToClaude` :214-234, `TryRewrite` :485-500).
- Test: `DotnetCommandRewriterTests.cs`, `HookPayloadsTests.cs`.

**Interfaces:**
- Consumes: Task 2's conversion.
- Produces: `internal static string Rewrite(string command, bool convertExplicitDtk)`. `Rewrite(command)` means
  `Rewrite(command, false)`, and the same-reference contract holds. Claude Code's `PreToolUse` path (`ReplyToClaude` →
  `ReplyWithUpdatedInput`) passes `true`; every other harness passes `false`.

- [ ] **Step 1: Write the failing tests.**
  - `[Theory] Rewrite_ExplicitDtkPipedIntoALineCount_BecomesMaxLines` with `convertExplicitDtk: true`:
    - `dtk dotnet test | tail -20` → `dtk dotnet test --max-lines 20`
    - `dtk test --filter X | tail -5 && git add -A` → `dtk test --max-lines 5 --filter X && git add -A`
    - `/usr/local/bin/dtk dotnet build | head -3` → `/usr/local/bin/dtk dotnet build --max-lines 3`
  - `[Theory] Rewrite_ExplicitDtkNotConverted_IsReturnedUnchanged` (`BeSameAs`), with `true`:
    - `DTK_RAW=1 dtk dotnet test | tail -5`
    - `dtk log test | tail -5`
    - `dtk pipe test < x.log | tail -5`
    - `echo dtk dotnet test | tail -5`
    - `dtk dotnet test | tail -f`
    - `dtk dotnet test`
  - `Rewrite_ExplicitDtkWithoutTheFlag_IsReturnedUnchanged`: `dtk dotnet test | tail -20` with `false`, `BeSameAs`.
  - In `HookPayloadsTests`:
    - `Claude_ExplicitDtkPipedIntoTail_IsConverted`: the reply's `updatedInput.command` is `dtk dotnet test
      --max-lines 20`.
    - `Copilot_ExplicitDtkPipedIntoTail_PrintsNothing`: the reply is null.
- [ ] **Step 2: Run them to verify they fail.** Same command as Task 2, Step 2. Expected: the converted cases fail;
  the unchanged ones pass.
- [ ] **Step 3: Implement.** With `convertExplicitDtk`, also accept a token at a command position (the same
  `Boundaries`/`RunsHereWithTheSamePath` rules as `dotnet`) whose `ProgramName` is `dtk` or `dtk.exe`, followed by
  either `dotnet <sub>` or `<sub>`, where `<sub>` matches through `TryMatchSubcommand`. Apply only Task 2's
  conversion to it; never add a prefix. A `DTK_RAW=` assignment before it means no conversion. The early return
  checks for `dtk` as well as `dotnet` when the flag is set.
- [ ] **Step 4: Run the tests to verify they pass.** Expected: all pass, and the whole `HookPayloadsTests` class
  still passes.
- [ ] **Step 5: Commit.** `git commit -m "feat: convert an explicit dtk command piped into tail or head, under Claude
  Code"`.

### Task 4: `IRtkRewriter` and `ProcessRtkRewriter`

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/IRtkRewriter.cs` (the interface and the `RtkRewrite`
  record).
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/ProcessRtkRewriter.cs`.
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/ProcessRtkRewriterTests.cs`.
- Create, if Application.Tests has none: `tests/DotnetTokenKiller.Application.Tests/Helpers/UnixFactAttribute.cs`,
  mirroring `tests/DotnetTokenKiller.Infrastructure.Tests/Helpers/WindowsFactAttribute.cs`.

**Interfaces:**
- Produces:
  ```csharp
  internal interface IRtkRewriter
  {
      /// <returns>rtk's answer, or null when rtk could not be asked (not on PATH, timeout, failed to start).</returns>
      RtkRewrite? Rewrite(string command);
  }

  /// <param name="ExitCode">0 allow + rewrite, 1 none, 2 deny rule, 3 ask + rewrite (rtk's protocol).</param>
  /// <param name="Output">stdout with one trailing "\n" or "\r\n" removed.</param>
  internal sealed record RtkRewrite(int ExitCode, string Output);

  internal sealed class ProcessRtkRewriter(Func<string?> locateRtk, TimeSpan timeout) : IRtkRewriter
  {
      internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
  }
  ```

- [ ] **Step 1: Write the failing tests.** Mark them `[UnixFact]`. Each writes a fake `rtk` POSIX script, mode 755, into
  a temp directory, the way `tests/DotnetTokenKiller.Cli.IntegrationTests/Helpers/NodeDriver.cs:30` does.
  - `Rewrite_RtkRewrites_ReturnsItsExitCodeAndOutput`: the script runs `printf 'rtk %s\n' "$2"; exit 3`. Rewriting
    `git status` gives `(3, "rtk git status")`.
  - `Rewrite_RtkHasNoRewrite_ReturnsExitOneAndNoOutput`: `(1, "")`.
  - `Rewrite_PassesTheCommandAsOneArgumentVerbatim`: the script prints `$#` and `$2`. For `dotnet build "My App.sln";
    git log --format="%h $x" | grep 'é'`, the output is `2` and the input unchanged.
  - `Rewrite_RemovesRtkRewriteHostFromTheEnvironment`: the test process sets `RTK_REWRITE_HOST=1`, and restores it
    afterwards. The script prints `${RTK_REWRITE_HOST-unset}`, and the output is `unset`.
  - `Rewrite_RtkOutlivesTheTimeout_ReturnsNullWithinTheTimeout`: the script runs `sleep 10`. With a 300 ms timeout,
    the result is null and the call returns in under 3 s.
  - `Rewrite_NoRtkOnPath_ReturnsNullAndStartsNothing`: `locateRtk` returns null.
  - `Rewrite_LocatesRtkOnce`: two calls invoke `locateRtk` once.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~ProcessRtkRewriterTests"`. Expected:
  compile error, because the type does not exist yet.
- [ ] **Step 3: Implement `ProcessRtkRewriter.Rewrite`.** It is synchronous: the hook path is synchronous. Use
  `ProcessStartInfo(path)` with `ArgumentList` `rewrite`, then the command; `UseShellExecute = false`; redirect stdin
  (close it at once), stdout and stderr (UTF-8). Call `Environment.Remove("RTK_REWRITE_HOST")`. Read both streams
  asynchronously, then `WaitForExit(timeout)`. On a timeout, `Kill(entireProcessTree: true)` and return null. Return
  null on `Win32Exception`, `InvalidOperationException` or `IOException`.
- [ ] **Step 4: Run the tests to verify they pass.** Expected: all pass on Linux.
- [ ] **Step 5: Commit.** `git commit -m "feat: run rtk rewrite without a shell, bounded, for the Claude hook"`.

### Task 5: `RtkRewriteRace`, the decision

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/RtkRewriteRace.cs`.
- Create: `tests/DotnetTokenKiller.Application.Tests/Integration/Hooks/RtkRewriteRaceTests.cs`, with a test-local
  `FakeRtkRewriter(Func<string, RtkRewrite?>)` that records its calls.

**Interfaces:**
- Consumes: `IRtkRewriter`, `RtkRewrite` (Task 4).
- Produces:
  ```csharp
  internal static class RtkRewriteRace
  {
      internal const string ReasonLead = "dtk and rtk would each rewrite this command, and Claude Code applies only one hook's rewrite. Run this instead, which carries both:\n";

      /// <returns>The merged command when a refusal is warranted, else null.</returns>
      internal static string? MergedCommand(string command, string dtkRewrite, Func<string, string> rewriteWithDtk, IRtkRewriter rtk);

      /// <returns>{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":ReasonLead + merged}}</returns>
      internal static string DenyReply(string merged);
  }
  ```
  The decision, in order:
  1. `dtkRewrite == command`: null, and no rtk call.
  2. `a = rtk.Rewrite(command)`. Unless `a` is exit 0 or 3 with non-empty output different from `command`: null.
  3. `b = rtk.Rewrite(dtkRewrite)`. On exit 0 or 3 with non-empty output, `merged = b.Output`; on exit 1, `merged =
     dtkRewrite`; on anything else: null.
  4. If `rewriteWithDtk(merged) != merged`: null.
  5. `c = rtk.Rewrite(merged)`. Stable means `c` is exit 1 or 2, or exit 0 or 3 with output equal to `merged`. Not
     stable, or `c` is null: null.
  6. Return `merged`.

- [ ] **Step 1: Write the failing tests.** In each, the fake rtk prefixes `git` with `rtk ` and leaves the rest alone,
  unless the test says otherwise.
  - `MergedCommand_BothRewrite_ReturnsRtksRewriteOfDtks`: for `dotnet build; git status`, the result is `dtk dotnet
    build; rtk git status`, after 3 calls.
  - `MergedCommand_DtkLeavesTheCommandAlone_AsksRtkNothing`: zero calls.
  - `[Theory] MergedCommand_RtkWouldNotRewrite_ReturnsNull`, where the first answer is `(1, "")`, `(2, "")`,
    `(0, command)`, `(3, "")`, `(7, "x")` or `null`.
  - `MergedCommand_RtkRewritesOnlyTheDotnetSegment_ReturnsDtksRewrite`: rtk without the exclusion rewrites `dotnet
    build` to `rtk dotnet build`, and leaves `dtk dotnet build` alone (exit 1). The result is `dtk dotnet build`.
  - `MergedCommand_RtkFailsOnTheSecondCall_ReturnsNull`
  - `MergedCommand_MergeNotStableUnderRtk_ReturnsNull`: the third call returns a different output.
  - `MergedCommand_MergeNotStableUnderDtk_ReturnsNull`: `rewriteWithDtk` changes the merged command.
  - `DenyReply_IsClaudesDenyWithTheReasonAndTheMergedCommand`: parse the JSON and assert all four fields. The reason
    equals `ReasonLead + "dtk dotnet build; rtk git status"`.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~RtkRewriteRaceTests"`. Expected:
  compile error.
- [ ] **Step 3: Implement both members** as specified above. Build the JSON as a `JsonObject` and call
  `ToJsonString()`, as `ReplyWithUpdatedInput` (`HookPayloads.cs:294-314`) does.
- [ ] **Step 4: Run them to verify they pass.**
- [ ] **Step 5: Commit.** `git commit -m "feat: decide when rtk's and dtk's rewrites of a command would race"`.

### Task 6: The Claude reply refuses; `HookEntryPoint` wires the real rtk peer

**Files:**
- Create: `src/DotnetTokenKiller.Application/Integration/Hooks/RtkPeer.cs`.
- Modify: `HookPayloads.cs` (the `Reply` overloads :144-202, `ReplyToClaude` :214-234) and
  `src/DotnetTokenKiller.Cli/HookEntryPoint.cs:30-49`.
- Test: `HookPayloadsTests.cs`, and `tests/DotnetTokenKiller.Cli.IntegrationTests/HookIntegrationTests.cs`, extending
  the `RunDtkSeparatingStreamsAsync` helper with environment variables if it lacks them.

**Interfaces:**
- Consumes: `RtkRewriteRace` (Task 5); `ProcessRtkRewriter` (Task 4); `Rewrite(string, bool)` (Task 3).
- Produces:
  - `internal sealed record RtkPeer(Func<string, bool> IsHookRegistered, IRtkRewriter Rewriter)`. Its argument is the
    project directory.
  - `internal static string? HookPayloads.Reply(HookPayloadKind kind, ReadOnlySpan<byte> payload, string?
    claudeProjectDirectory, RtkPeer? rtk)`. The existing three-argument overload forwards `rtk: null`.
  - `HookEntryPoint.Run(…, string? claudeProjectDirectory = null, RtkPeer? rtk = null)`. The production
    `Run(IReadOnlyList<string>)` passes `new RtkPeer(dir => new RtkHookCoexistence(new HomePaths().ClaudeDir,
    string.Empty).IsRtkHookPresent(dir), new ProcessRtkRewriter(() =>
    ExecutableSearch.FindOnProcessPath(RtkConfigLocator.RtkCommand), ProcessRtkRewriter.DefaultTimeout))`. Both are
    cheap to construct and do nothing until called.

- [ ] **Step 1: Write the failing tests.** Add to `HookPayloadsTests` a helper `Reply(kind, json, RtkPeer)`, plus a
  `RtkPeer` built on `RtkRewriteRaceTests`' fake. Move the fake to a shared test helper file.
  - `Claude_CommandBothRewrite_IsRefusedWithTheMergedCommand`. Payload: `{"hook_event_name":"PreToolUse",
    "tool_name":"Bash","tool_input":{"command":"dotnet build; git status"},"cwd":"/p"}`. The reply equals
    `RtkRewriteRace.DenyReply("dtk dotnet build; rtk git status")`.
  - `Claude_RefusalReason_CarriesTheMergedCommandVerbatim`, for a command with `"`, `$`, `%`, `é` and a newline.
  - `Claude_RtkHookNotRegistered_RewritesAsToday`: `IsHookRegistered` returns false, and rtk gets no call.
  - `Claude_RtkPeerSeesTheProjectDirectory`: `IsHookRegistered` receives `claudeProjectDirectory` when it is set,
    else the payload's `cwd`.
  - `[Theory] Claude_NotABashPreToolUse_RewritesAsToday`: no `hook_event_name`, which is doctor's old probe;
    `tool_name` `Task`; and a payload with `cursor_version`.
  - `Claude_DotnetOnlyInQuotedText_IsNeitherRewrittenNorRefused`: `git commit -m "run dotnet build"` gives null.
  - `Claude_PureDotnetCommand_AsksRtkOnce`: for `dotnet build`, the fake answers `(1, "")` once. The reply is today's
    rewrite.
  - Add to `HookIntegrationTests`, `[UnixFact]`-style, `Hook_Claude_CommandBothRewrite_IsRefusedAsync`. It runs the
    real `dtk hook claude` with these in the isolated HOME and on PATH:
    - `HOME/.claude/settings.json` registers `rtk hook claude`;
    - a fake `rtk` script on `PATH` answers `rewrite` by prefixing `git` and exiting 3, and exits 1 for anything
      else.

    Expected: stdout is the deny JSON.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~HookPayloadsTests"`, plus
  `"FullyQualifiedName~HookIntegrationTests"` with `--no-build` after a build. See the memory note: run the CLI
  integration classes one at a time.
- [ ] **Step 3: Implement.** In `ReplyToClaude`, on the non-context path, when `rtk` is not null, the payload is a
  `JsonObject` whose `hook_event_name` is `PreToolUse` and `tool_name` is `Bash`, and there is no `cursor_version`:
  1. Read `tool_input.command`.
  2. `rewritten = DotnetCommandRewriter.Rewrite(command, true)`.
  3. If it changed, set `dir = claudeProjectDirectory ?? cwd ?? Environment.CurrentDirectory`. If
     `rtk.IsHookRegistered(dir)` and `RtkRewriteRace.MergedCommand(command, rewritten, c =>
     DotnetCommandRewriter.Rewrite(c, true), rtk.Rewriter)` returns a command, return `RtkRewriteRace.DenyReply(…)`.
  4. Otherwise, fall through to `ReplyWithUpdatedInput` as today.

  Then wire `HookEntryPoint` as in Interfaces, and only for `HookPayloadKind.ClaudeCode`.
- [ ] **Step 4: Run the tests to verify they pass.** Expected: they pass, as do `ClaudeContextHookTests` and
  `HookEntryPointTests`, unchanged.
- [ ] **Step 5: Commit.** `git commit -m "fix: refuse a command rtk's and dtk's Claude hooks would both rewrite, with
  the merged command"`.

### Task 7: Doctor probes a mixed command

**Files:**
- Modify: `src/DotnetTokenKiller.Application/UseCases/DoctorUseCase.cs`: the constructors (:38-66) and
  `CheckRtkCoexistenceAsync` (:119-154).
- Test: `tests/DotnetTokenKiller.Application.Tests/UseCases/DoctorUseCaseTests.cs`.

**Interfaces:**
- Consumes: `RtkRewriteRace.ReasonLead` (Task 5).
- Produces: the internal constructor gains `Func<string?> locateDtk` after `locateRtk`. The public one passes
  `static () => ExecutableSearch.FindOnProcessPath("dtk")`, or the constant `HookHealthChecker` already uses for that
  name.

- [ ] **Step 1: Write the failing tests.** Use the existing `RtkAnswersRewriteWith` pattern. Stub `rtk rewrite "git
  status"` separately from `rtk rewrite "dotnet build"`, and stub `RunCapturedWithInputAsync(dtkPath, ["hook",
  "claude"], …)`.
  - `RunAsync_DtkHookRefusesAMixedCommand_PassesSayingSo`. The check passes with exactly `rtk leaves dotnet commands
    to dtk; a command both rewrite comes back refused with the merged command`. The payload sent holds
    `"hook_event_name":"PreToolUse"`, `"tool_name":"Bash"`, the command `git status && dotnet build`, and `cwd` equal
    to the project directory.
  - `RunAsync_DtkHookOnlyRewritesAMixedCommand_WarnsThatTheRewritesRace`. A warning, exactly: `the dtk hook on PATH
    lets rtk's and dtk's rewrites race on a command both rewrite, such as 'git status && dotnet build', and one of
    them is lost. Upgrade dtk to 0.14.0 or later.`
  - `RunAsync_RtkRewritesNoGitStatus_SkipsTheMixedProbe`: `rtk rewrite "git status"` exits 1. The pass message is
    today's, `rtk leaves dotnet commands to dtk`, and dtk is never started.
  - `RunAsync_NoDtkOnPath_KeepsTodaysPass`
  - `RunAsync_DtkHookProbeTimesOutOrPrintsGarbage_KeepsTodaysPass`, a Theory over a null result and non-JSON stdout.
  - Existing tests that construct the use case pass a `locateDtk` returning null, so they are unaffected.
- [ ] **Step 2: Run them to verify they fail.** `--filter "FullyQualifiedName~DoctorUseCaseTests"`.
- [ ] **Step 3: Implement.** After today's exclusion check passes (exit 1, no output), the probe runs only when `rtk
  rewrite "git status"`, run through `RtkConfigLocator.TryRunAsync` with `ProbeTimeout`, exits 0 or 3 with output
  different from `git status`. It then runs `dtk hook claude` with the payload above, bounded by a linked CTS of
  `RtkConfigLocator.ProbeTimeout`. If the stdout's `permissionDecision` is `deny` and the reason starts with
  `ReasonLead`, the check passes with the new message. If the stdout holds `updatedInput` and no deny, it warns.
  Anything else keeps today's pass.
- [ ] **Step 4: Run them to verify they pass.**
- [ ] **Step 5: Commit.** `git commit -m "feat: doctor checks that a command rtk and dtk both rewrite comes back
  merged"`.

### Task 8: Docs, CHANGELOG, hook timings

**Files:** `CLAUDE.md`, `docs/articles/ai-agent-setup.md`, `README.md:304`, `docs/articles/harness-verification.md`,
`docs/articles/performance.md:66-82`, `CHANGELOG.md`.

- [ ] **Step 1: `CLAUDE.md`.** After the context-hooks paragraph (:111-118), add one paragraph on the refusal: where
  it lives (`RtkRewriteRace`, `ProcessRtkRewriter`, `RtkPeer`), when it fires, that it fails open and is never sent
  unless stable, that only Claude Code's `PreToolUse`/`Bash` payloads get it, and that doctor probes it.
- [ ] **Step 2: `ai-agent-setup.md` and `README.md:304`.** In the Claude Code section (:105-210), add an rtk subsection.
  It covers: the parallel hooks and how the refusal resolves them; writing `dtk dotnet …` and `rtk …` yourself to
  avoid the extra turn; and the permissions note. That note says that if you rely on rtk approving commands your
  allow rules cover, you should add `Bash(rtk git status:*)`-style and `Bash(dtk dotnet:*)` rules, never
  `Bash(rtk:*)` (it allows `rtk run <anything>`), and that auto and bypass modes are unaffected. Point README's rtk
  note at it.
- [ ] **Step 3: `harness-verification.md`.** Under Devin, add a numbered item: it runs Claude Code's hooks and so may
  get the refusal; how it handles `deny` is unverified. Add the same item for each harness that can run rtk's hook
  beside dtk's, saying the race with rtk is unverified there.
- [ ] **Step 4: Measure and record the hook timings.** Run `dotnet publish src/DotnetTokenKiller.Cli -c Release -r
  linux-x64 -o artifacts/aot`. Then take 55 runs each, medians, using the method of `performance.md`'s `## dtk hook`
  section (a `/bin/true` baseline included), of `artifacts/aot/dtk hook claude` on:
  - (a) a `dotnet build` payload, with no rtk registration in a scratch HOME;
  - (b) the same, with `rtk hook claude` registered and the real rtk on PATH;
  - (c) `dotnet build; git status`, under (b)'s setup, which is the refusal path.

  Add a dated paragraph with those figures to `## dtk hook`.
- [ ] **Step 5: `CHANGELOG.md` `[Unreleased]`.** Under `### Fixed`, an entry for the race: the symptom, the refusal,
  the extra turn and the permissions note. Under `### Added`, an entry for doctor's probe.
- [ ] **Step 6: Commit.** `git commit -m "docs: rtk and dtk under Claude Code, the refusal, and its hook cost"`.

### Task 9: Final gate and verification

- [ ] **Step 1: Gate R with the real hook.** Repeat Task 1's sandbox with `"$REPO/artifacts/aot/dtk hook claude"` in
  place of the stub, with Steps 2 and 3, under:
  - rtk 0.43, the installed one;
  - rtk 1.2.1. Download its Linux x86_64 release asset from `github.com/rtk-ai/rtk/releases/tag/v1.2.1` into
    `$S/rtk-1.2.1/`, in its own directory. Verify the checksum if the release publishes one. Put that directory first
    on `PATH` for the `claude` process.

  Expected: the same pass as Task 1, and `dtk gain` in a scratch state shows the build tracked. If either version
  fails, stop and report to the user.
- [ ] **Step 2: Replays.** Through `artifacts/aot/dtk hook claude`, with rtk registered, replay
  `dotnet jb cleanupcode App.slnx --no-build; git status --short`, `dotnet tool restore >/dev/null; dotnet jb
  cleanupcode App.slnx --no-build; git status --short`, `dotnet build --no-incremental; git status` and `dotnet test |
  tail -20`. Each mixed one is refused with a merged command; the last becomes `--max-lines 20`.
- [ ] **Step 3: Full suite and format.** Run `dtk dotnet build DotnetTokenKiller.slnx`, `dtk dotnet test
  DotnetTokenKiller.slnx`, and `dtk dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`. Run the CLI
  integration classes one at a time if the whole run is too long. Expected: all green, and `SavingsBaselineTests`
  unchanged, since no filter output changed.
- [ ] **Step 4: Push and open the PR** against `develop`, titled `fix: stop rtk's and dtk's Claude hooks from losing a
  rewrite; turn | tail -N into --max-lines`. The body summarises the spec's items 1 and 3, quotes Gate R's two
  `tool_use` commands, and ends with the PR attribution line.
