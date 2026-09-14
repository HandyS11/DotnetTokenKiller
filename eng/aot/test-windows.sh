#!/bin/sh
# Installs the Windows x64 package from a local feed and checks what the shim route promises: the command
# on PATH is the native binary and not a .cmd, tracking works with no e_sqlite3.dll beside it, the managed
# entry point still runs, and uninstall then reinstall round-trips. Prints 21-run medians for the shim and,
# with --compare-any, for the `any` package installed beside it. Git Bash on Windows. The parity tests, the
# integration suite and the three-shell smokes run from the workflow, which reads DTK_INSTALLED and
# DTK_STORE from $GITHUB_ENV when this script sets them.
#
# Usage: sh eng/aot/test-windows.sh <version> <feed-dir> <tools-dir> [--compare-any]
set -eu

usage="usage: sh eng/aot/test-windows.sh <version> <feed-dir> <tools-dir> [--compare-any]"
version=${1:?$usage}
feed=${2:?$usage}
tools=${3:?$usage}
compare_any=${4:-}
repo=$(cd "$(dirname "$0")/../.." && pwd)
nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}
fixture="$repo/tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt"

cd "$repo"
mkdir -p "$tools" "$nuget"
feed=$(cd "$feed" && pwd)
tools=$(cd "$tools" && pwd)
version_lower=$(echo "$version" | tr '[:upper:]' '[:lower:]')

fail() {
    echo "test-windows.sh: $1" >&2
    exit 1
}

# The parity tests compare against this JIT build, so it must carry the packs' version.
dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$version"
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$version" -o "$feed"

# A package already in the NuGet cache at this ID and version is installed from the cache, not the feed.
rm -rf "$nuget/dotnettokenkiller/$version_lower" "$nuget/dotnettokenkiller.win-x64/$version_lower" \
    "$nuget/dotnettokenkiller.any/$version_lower"

# A relative source resolves against the config file's directory; an absolute Git Bash path is
# POSIX-style, which NuGet cannot read. nuget.org serves everything except dtk's own packages.
write_config() {
    cat > "$1" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$2" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="DotnetTokenKiller*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
}
config="$(dirname "$feed")/$(basename "$feed").nuget.config"
write_config "$config" "$(basename "$feed")"

install() {
    dotnet tool install --tool-path "$tools" --configfile "$config" DotnetTokenKiller --version "$version"
}

store="$tools/.store/dotnettokenkiller/$version_lower/dotnettokenkiller.win-x64/$version_lower/tools/net10.0/win-x64"
shim="$tools/dtk.exe"
check_installed() {
    [ -d "$store" ] || { ls -R "$tools/.store" >&2; fail "expected $store: the install did not pick the win-x64 package"; }
    [ ! -f "$tools/dtk.cmd" ] || fail "the SDK wrote a dtk.cmd shim: the package's runner is not dotnet"
    [ -f "$shim" ] || fail "expected the shim $shim"
    cmp -s "$shim" "$reference" || fail "the installed dtk.exe is not the packaged native binary"
}

reference=$(mktemp -d)/dtk.exe
sh eng/aot/unzip-file.sh "$feed/DotnetTokenKiller.win-x64.$version.nupkg" \
    tools/net10.0/win-x64/shims/win-x64/dtk.exe "$reference"

install
check_installed
"$shim" --version

# Tracking with nothing beside the shim: the exe must find winsqlite3 in System32. dtk is a Windows
# program, so its environment gets Windows paths.
state="$tools/state"
mkdir -p "$state/logs"
DTK_CONFIG_PATH=$(cygpath -w "$state/config.json")
DTK_DB_PATH=$(cygpath -w "$state/tracking.db")
DTK_TEE_DIR=$(cygpath -w "$state/logs")
export DTK_CONFIG_PATH DTK_DB_PATH DTK_TEE_DIR
status=0
"$shim" pipe build --exit-code 1 < "$fixture" > "$state/pipe.txt" 2>&1 || status=$?
[ "$status" -eq 1 ] || { cat "$state/pipe.txt"; fail "pipe build exited $status, expected 1"; }
grep -q "dotnet build: 3 errors" "$state/pipe.txt" || { cat "$state/pipe.txt"; fail "pipe build did not print the filtered summary"; }
"$shim" gain --json > "$state/gain.json" || { cat "$state/gain.json"; fail "gain failed"; }
grep -q '"TotalCommands":1' "$state/gain.json" || { cat "$state/gain.json"; fail "gain did not report the tracked run: SQLite is not working"; }

# The managed entry point every `dotnet tool run`, manifest and dnx invocation uses.
dotnet "$store/dtk.dll" --version

# The SDK's RemoveShim and CreateShim paths.
dotnet tool uninstall --tool-path "$tools" DotnetTokenKiller
[ ! -f "$shim" ] || fail "uninstall left $shim behind"
install
check_installed

median_ms() {
    sort -n | awk '{ a[NR] = $1 } END { printf "%.1f", (NR % 2) ? a[(NR + 1) / 2] : (a[NR / 2] + a[NR / 2 + 1]) / 2 }'
}
time_runs() {
    i=0
    while [ "$i" -lt 21 ]; do
        start=$(date +%s%N)
        "$@" > /dev/null 2>&1 < "${STDIN_FILE:-/dev/null}" || true
        end=$(date +%s%N)
        echo $(( (end - start) / 1000000 ))
        i=$((i + 1))
    done | median_ms
}
echo "win-x64 shim: --version median $(time_runs "$shim" --version) ms"
echo "win-x64 shim: pipe build median $(STDIN_FILE=$fixture time_runs "$shim" pipe build --exit-code 1) ms"

if [ "$compare_any" = "--compare-any" ]; then
    any_feed="$feed-any"
    any_tools="$tools-any"
    mkdir -p "$any_feed" "$any_tools"
    dotnet pack src/DotnetTokenKiller.Cli -c Release -r any -p:PublishAot=false -p:Version="$version" -o "$any_feed"
    dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:ToolPackageRuntimeIdentifiers=any \
        -p:Version="$version" -o "$any_feed"
    any_config="$(dirname "$any_feed")/$(basename "$any_feed").nuget.config"
    write_config "$any_config" "$(basename "$any_feed")"
    rm -rf "$nuget/dotnettokenkiller/$version_lower"
    dotnet tool install --tool-path "$any_tools" --configfile "$any_config" DotnetTokenKiller --version "$version"
    echo "any package: --version median $(time_runs "$any_tools/dtk.exe" --version) ms"
    echo "any package: pipe build median $(STDIN_FILE=$fixture time_runs "$any_tools/dtk.exe" pipe build --exit-code 1) ms"
fi

if [ -n "${GITHUB_ENV:-}" ]; then
    echo "DTK_INSTALLED=$(cygpath -w "$shim")" >> "$GITHUB_ENV"
    echo "DTK_STORE=$(cygpath -w "$store")" >> "$GITHUB_ENV"
fi
