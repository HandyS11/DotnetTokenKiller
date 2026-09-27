#!/bin/sh
# Gate K: proves that the real, released `kilo` CLI (Kilo Code, an OpenCode fork) rewrites `dotnet build`
# to `dtk dotnet build` through `dtk init kilo` + `dtk hook kilo`, against a mock OpenAI-compatible model
# (eng/gates/mock-openai.mjs) so no network model access or API key is needed.
#
# Installs the given @kilocode/cli release from npm, builds a throwaway git-root project with
# `dtk init kilo`, points it at the mock model via a hand-written `kilo.json` (Kilo's v1-compat provider
# config: `{"provider":{"mock":{"npm":"@ai-sdk/openai-compatible","options":{...}}},"model":"mock/mock"}`,
# plus `{"permission":{"bash":"allow"}}` so the mock's tool call needs no interactive approval), and runs
# `kilo run -m mock/mock "build the project"` non-interactively. Passes when the fake `dotnet`/`dtk` on
# PATH recorded `dtk dotnet build` and never a bare `dotnet build`. A control run — same project, dtk's
# plugin removed via `dtk init kilo --dir <project> --uninstall` — must show the bare `dotnet build`,
# proving the gate can actually fail.
#
# Needs network (to install the npm package, and to fetch @ai-sdk/openai-compatible if the installed
# `kilo` doesn't bundle it) and Node (both to run the mock model and because `kilo` itself is a Node CLI
# wrapping a native binary fetched by its own postinstall script).
#
# npm 11's install-script allowlist blocks that postinstall from running automatically ("npm warn
# install-scripts ... not yet covered by allowScripts"), so this script runs it by hand after `npm
# install`; without that the `kilo` binary has no native binary to spawn and exits immediately.
#
# Not run in CI.
#
# Usage: sh eng/gates/kilo-gate.sh <kilo-version> [dtk-binary]
#   <kilo-version>  e.g. 7.8.1 (the @kilocode/cli npm version, no leading "v")
#   [dtk-binary]    defaults to the branch's Debug build,
#                   src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk
set -eu

usage="usage: sh eng/gates/kilo-gate.sh <kilo-version> [dtk-binary]"
version=${1:?$usage}
repo=$(cd "$(dirname "$0")/../.." && pwd)
dtk_bin=${2:-"$repo/src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk"}
[ -x "$dtk_bin" ] || { echo "kilo-gate.sh: no executable dtk at $dtk_bin (build it first: dtk dotnet build DotnetTokenKiller.slnx)" >&2; exit 2; }
command -v node >/dev/null || { echo "kilo-gate.sh: node is required to run kilo and the mock model" >&2; exit 2; }
command -v npm >/dev/null || { echo "kilo-gate.sh: npm is required to install @kilocode/cli" >&2; exit 2; }

work=$(mktemp -d /tmp/dtk-kilo-gate.XXXXXX)
mock_pid=
cleanup() {
    [ -n "$mock_pid" ] && kill "$mock_pid" 2>/dev/null || true
    rm -rf "$work"
}
trap cleanup EXIT

mkdir -p "$work/kilo" "$work/home/.config" "$work/home/.local/share" "$work/home/.cache" "$work/bin" "$work/project"

echo "kilo-gate.sh: npm install @kilocode/cli@${version}"
( cd "$work/kilo" && npm install --prefix "$work/kilo" --loglevel=error "@kilocode/cli@${version}" ) >"$work/npm-install.out" 2>&1 \
    || { echo "kilo-gate.sh: npm install failed" >&2; cat "$work/npm-install.out" >&2; exit 2; }

# npm's install-script allowlist (npm 11+) skips @kilocode/cli's postinstall (which links the
# platform-specific native binary already fetched as an optionalDependency into bin/.kilo) unless it is
# explicitly approved; run it directly so the `kilo` wrapper has a binary to spawn.
kilo_pkg_dir="$work/kilo/node_modules/@kilocode/cli"
[ -f "$kilo_pkg_dir/postinstall.mjs" ] || { echo "kilo-gate.sh: no postinstall.mjs under $kilo_pkg_dir (unexpected @kilocode/cli layout)" >&2; exit 2; }
node "$kilo_pkg_dir/postinstall.mjs" >"$work/postinstall.out" 2>&1 \
    || { echo "kilo-gate.sh: postinstall.mjs failed" >&2; cat "$work/postinstall.out" >&2; exit 2; }

kilo_bin="$work/kilo/node_modules/.bin/kilo"
[ -x "$kilo_bin" ] || { echo "kilo-gate.sh: no executable 'kilo' at $kilo_bin after install" >&2; exit 2; }

gate_log="$work/gate.log"
: > "$gate_log"

# A fake `dotnet` that just records the command it was called with, and a `dtk` wrapper that forwards
# `dtk hook ...` to the real dtk (so the hook's rewrite is genuine) and records everything else, exactly
# like the fake `dotnet` — so a broken or missing rewrite falls through to a recorded bare `dotnet build`.
cat > "$work/bin/dotnet" <<EOF
#!/bin/sh
echo "dotnet \$*" >> "$gate_log"
exit 0
EOF
cat > "$work/bin/dtk" <<EOF
#!/bin/sh
if [ "\${1:-}" = "hook" ]; then
    exec "$dtk_bin" "\$@"
fi
echo "dtk \$*" >> "$gate_log"
exit 0
EOF
chmod +x "$work/bin/dotnet" "$work/bin/dtk"

# A free localhost port for the mock model, chosen by asking the OS for one (node is already required).
port=$(node -e 'const s=require("node:net").createServer();s.listen(0,"127.0.0.1",()=>{console.log(s.address().port);s.close();});')

( cd "$work/project" && git init -q && git config user.email gate@example.com && git config user.name "Gate K" )
"$dtk_bin" init kilo --dir "$work/project" >/dev/null

# Kilo's v1-compat provider config (packages/core/src/v1/config/provider.ts in the Kilo source): a custom
# OpenAI-compatible provider pointed at the mock, selected as the default model, with bash auto-approved
# in this scratch project only (packages/core/src/v1/config/permission.ts).
cat > "$work/project/kilo.json" <<EOF
{
  "provider": {
    "mock": {
      "npm": "@ai-sdk/openai-compatible",
      "options": { "baseURL": "http://127.0.0.1:${port}/v1", "apiKey": "x" },
      "models": { "mock": {} }
    }
  },
  "model": "mock/mock",
  "permission": { "bash": "allow" }
}
EOF

mock_log="$work/mock-requests.log"
: > "$mock_log"
node "$repo/eng/gates/mock-openai.mjs" "$port" "$mock_log" > "$work/mock.out" 2>&1 &
mock_pid=$!
for _ in $(seq 1 50); do
    curl -sSf "http://127.0.0.1:${port}/v1/models" >/dev/null 2>&1 && break
    sleep 0.1
done

run_kilo() {
    ( cd "$work/project" && env HOME="$work/home" \
        XDG_CONFIG_HOME="$work/home/.config" \
        XDG_DATA_HOME="$work/home/.local/share" \
        XDG_CACHE_HOME="$work/home/.cache" \
        KILO_DISABLE_AUTOUPDATE=1 \
        KILO_DISABLE_LSP_DOWNLOAD=1 \
        PATH="$work/bin:$PATH" \
        timeout 180 "$kilo_bin" run -m mock/mock "build the project" ) > "$work/kilo-run.out" 2>&1
}

echo "kilo-gate.sh: kilo v${version}, main run (dtk's plugin installed)"
: > "$gate_log"
main_exit=0
run_kilo || main_exit=$?
echo "--- kilo exit: $main_exit ---"
cat "$work/kilo-run.out"
echo "--- \$GATE_LOG ---"
cat "$gate_log"

main_result=FAIL
if grep -Fxq 'dtk dotnet build' "$gate_log" && ! grep -Fxq 'dotnet build' "$gate_log"; then
    main_result=PASS
fi
echo "kilo-gate.sh: kilo v${version} main run: $main_result"

echo
echo "kilo-gate.sh: kilo v${version}, control run (dtk's plugin removed)"
"$dtk_bin" init kilo --dir "$work/project" --uninstall >/dev/null
: > "$gate_log"
control_exit=0
run_kilo || control_exit=$?
echo "--- kilo exit: $control_exit ---"
cat "$work/kilo-run.out"
echo "--- \$GATE_LOG ---"
cat "$gate_log"

control_result=FAIL
if grep -Fxq 'dotnet build' "$gate_log"; then
    control_result=PASS
fi
echo "kilo-gate.sh: kilo v${version} control run (expect bare 'dotnet build'): $control_result"

echo
echo "kilo-gate.sh: SUMMARY kilo v${version}: main=$main_result control=$control_result"

if [ "$main_result" = PASS ] && [ "$control_result" = PASS ]; then
    exit 0
fi
exit 1
