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
printf '%s' '{"hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet build"}}' > "$work/codex.json"

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

# Codex CLI: the turn's shell without a login (sh/bash -c on Unix, powershell -NoProfile -Command on Windows), falling
# back to `$SHELL -lc` on Unix or `%COMSPEC% /C` on Windows when the turn has none; the bare command, because Codex runs
# the original command when a hook fails.
check "codex, bash" "$work/codex.json" rewrite "$with_dtk" "$bash_cmd" -c 'dtk hook codex'
for ps in "$pwsh_cmd" "$powershell_cmd"; do
    [ -n "$ps" ] || continue
    check "codex, $(basename "$ps")" "$work/codex.json" rewrite "$with_dtk" "$ps" -NoProfile -Command 'dtk hook codex'
done

if [ "$failures" -ne 0 ]; then
    echo "check-hook-shells.sh: $failures check(s) failed" >&2
    exit 1
fi
