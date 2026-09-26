#!/bin/sh
# Gate C: proves that the real, released `crush` binary rewrites `dotnet build` to `dtk dotnet build`
# through `dtk init crush` + `dtk hook crush`, against a mock OpenAI-compatible model
# (eng/gates/mock-openai.mjs) so no network model access or API key is needed.
#
# Downloads the given Crush release, builds a throwaway git-root project with `dtk init crush`, points
# it at the mock model via a hand-appended `provider add`/`model add`/`permissions allow` in .crushrc,
# and runs `crush run "build the project"` non-interactively. Passes when the fake `dotnet`/`dtk` on
# PATH recorded `dtk dotnet build` and never a bare `dotnet build`. A control run — same project, dtk's
# hook section removed via `dtk init crush --uninstall` — must show the bare `dotnet build`, proving the
# gate can actually fail.
#
# Needs network (to download the release) and Node (to run the mock model); not run in CI.
#
# Usage: sh eng/gates/crush-gate.sh <crush-version> [dtk-binary]
#   <crush-version>  e.g. 0.96.1 (no leading "v")
#   [dtk-binary]     defaults to the branch's Debug build,
#                    src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk
set -eu

usage="usage: sh eng/gates/crush-gate.sh <crush-version> [dtk-binary]"
version=${1:?$usage}
repo=$(cd "$(dirname "$0")/../.." && pwd)
dtk_bin=${2:-"$repo/src/DotnetTokenKiller.Cli/bin/Debug/net10.0/dtk"}
[ -x "$dtk_bin" ] || { echo "crush-gate.sh: no executable dtk at $dtk_bin (build it first: dtk dotnet build DotnetTokenKiller.slnx)" >&2; exit 2; }
command -v node >/dev/null || { echo "crush-gate.sh: node is required to run the mock model" >&2; exit 2; }

asset="crush_${version}_Linux_x86_64.tar.gz"
url="https://github.com/charmbracelet/crush/releases/download/v${version}/${asset}"

# Downloads are cached across runs (by version) under /tmp; everything else is a fresh scratch tree
# removed on exit, including the mock server this script starts.
cache_dir="/tmp/dtk-crush-gate-cache/${version}"
mkdir -p "$cache_dir"
if [ ! -f "$cache_dir/$asset" ]; then
    echo "crush-gate.sh: downloading $url"
    curl -sSLf -o "$cache_dir/$asset.part" "$url"
    mv "$cache_dir/$asset.part" "$cache_dir/$asset"
fi

work=$(mktemp -d /tmp/dtk-crush-gate.XXXXXX)
mock_pid=
cleanup() {
    [ -n "$mock_pid" ] && kill "$mock_pid" 2>/dev/null || true
    rm -rf "$work"
}
trap cleanup EXIT

mkdir -p "$work/crush" "$work/home/.config" "$work/home/.local/share" "$work/home/.cache" "$work/bin" "$work/project"
tar xzf "$cache_dir/$asset" -C "$work/crush"
crush_bin=$(find "$work/crush" -type f -name crush | head -n1)
[ -n "$crush_bin" ] && [ -x "$crush_bin" ] || { echo "crush-gate.sh: no 'crush' executable found in $asset" >&2; exit 2; }

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

( cd "$work/project" && git init -q && git config user.email gate@example.com && git config user.name "Gate C" )
"$dtk_bin" init crush --dir "$work/project" >/dev/null

rc_file="$work/project/.crushrc"
[ -f "$rc_file" ] || rc_file="$work/project/crushrc"
[ -f "$rc_file" ] || { echo "crush-gate.sh: dtk init crush wrote neither .crushrc nor crushrc" >&2; exit 2; }

cat >> "$rc_file" <<EOF
provider add mock --type openai-compat --base-url http://127.0.0.1:${port}/v1 --api-key x --discover-models true
model add mock/mock --name mock --context-window 128000
permissions allow bash
EOF

mock_log="$work/mock-requests.log"
: > "$mock_log"
node "$repo/eng/gates/mock-openai.mjs" "$port" "$mock_log" > "$work/mock.out" 2>&1 &
mock_pid=$!
for _ in $(seq 1 50); do
    curl -sSf "http://127.0.0.1:${port}/v1/models" >/dev/null 2>&1 && break
    sleep 0.1
done

run_crush() {
    ( cd "$work/project" && env HOME="$work/home" \
        XDG_CONFIG_HOME="$work/home/.config" \
        XDG_DATA_HOME="$work/home/.local/share" \
        XDG_CACHE_HOME="$work/home/.cache" \
        CRUSH_DISABLE_PROVIDER_AUTO_UPDATE=1 \
        CRUSH_DISABLE_DEFAULT_PROVIDERS=1 \
        PATH="$work/bin:$PATH" \
        timeout 120 "$crush_bin" run -m mock/mock "build the project" ) > "$work/crush-run.out" 2>&1
}

echo "crush-gate.sh: Crush v${version}, main run (dtk's hook installed)"
: > "$gate_log"
main_exit=0
run_crush || main_exit=$?
echo "--- crush exit: $main_exit ---"
cat "$work/crush-run.out"
echo "--- \$GATE_LOG ---"
cat "$gate_log"

main_result=FAIL
if grep -Fxq 'dtk dotnet build' "$gate_log" && ! grep -Fxq 'dotnet build' "$gate_log"; then
    main_result=PASS
fi
echo "crush-gate.sh: Crush v${version} main run: $main_result"

echo
echo "crush-gate.sh: Crush v${version}, control run (dtk's hook removed)"
"$dtk_bin" init crush --dir "$work/project" --uninstall >/dev/null
: > "$gate_log"
control_exit=0
run_crush || control_exit=$?
echo "--- crush exit: $control_exit ---"
cat "$work/crush-run.out"
echo "--- \$GATE_LOG ---"
cat "$gate_log"

control_result=FAIL
if grep -Fxq 'dotnet build' "$gate_log"; then
    control_result=PASS
fi
echo "crush-gate.sh: Crush v${version} control run (expect bare 'dotnet build'): $control_result"

echo
echo "crush-gate.sh: SUMMARY Crush v${version}: main=$main_result control=$control_result"

if [ "$main_result" = PASS ] && [ "$control_result" = PASS ]; then
    exit 0
fi
exit 1
