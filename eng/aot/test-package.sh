#!/bin/sh
# Installs a dtk Native AOT tool package from a local feed and tests it against the JIT build: the AOT
# warning check on its pack log, the JIT-vs-AOT parity tests and, optionally, the whole CLI integration suite.
#
# Usage: eng/aot/test-package.sh [--image <image>] <rid> <version> <feed-dir> <pack-log> <tools-dir> [--integration-suite]
#
# <feed-dir> holds DotnetTokenKiller.<rid>.<version>.nupkg; the pointer package is packed into it here.
# <tools-dir> receives the installed tool. With --image, the script runs itself inside that image (the musl
# RIDs use mcr.microsoft.com/dotnet/sdk:10.0-alpine) as the invoking user, with the checkout and the NuGet
# cache mounted at their host paths; every path argument must then be inside the checkout.
set -eu

usage="usage: eng/aot/test-package.sh [--image <image>] <rid> <version> <feed-dir> <pack-log> <tools-dir> [--integration-suite]"
repo=$(cd "$(dirname "$0")/../.." && pwd)
nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}

image=
if [ "${1:-}" = "--image" ]; then
    image=${2:?$usage}
    shift 2
fi

rid=${1:?$usage}
version=${2:?$usage}
feed=${3:?$usage}
log=${4:?$usage}
tools=${5:?$usage}
integration_suite=${6:-}

mkdir -p "$tools" "$nuget"
feed=$(cd "$feed" && pwd)
log="$(cd "$(dirname "$log")" && pwd)/$(basename "$log")"
tools=$(cd "$tools" && pwd)

if [ -n "$image" ]; then
    for path in "$feed" "$log" "$tools"; do
        case "$path" in
            "$repo"/*) ;;
            *) echo "test-package.sh: with --image, $path must be inside $repo" >&2; exit 2 ;;
        esac
    done
    exec docker run --rm \
        --user "$(id -u):$(id -g)" \
        -v "$repo:$repo" -v "$nuget:$nuget" -w "$repo" \
        -e HOME=/tmp -e NUGET_PACKAGES="$nuget" -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        "$image" \
        sh "$repo/eng/aot/test-package.sh" "$rid" "$version" "$feed" "$log" "$tools" ${integration_suite:+"$integration_suite"}
fi

cd "$repo"
version_lower=$(echo "$version" | tr '[:upper:]' '[:lower:]')

# The parity tests compare against this JIT build, so it must carry the packs' version.
dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$version"
dotnet pack src/DotnetTokenKiller.Cli -c Release -p:IncludeSymbols=false -p:Version="$version" -o "$feed"

# A package already in the NuGet cache at this ID and version is installed from the cache, not the feed.
rm -rf "$nuget/dotnettokenkiller/$version_lower" "$nuget/dotnettokenkiller.$rid/$version_lower"

# An explicit template: older BSD mktemp (macOS before 10.11) and some minimal implementations require one.
config=$(mktemp "${TMPDIR:-/tmp}/dtk-nuget-config.XXXXXX")
trap 'rm -f "$config"' EXIT
cat > "$config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
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
rm -rf "$tools"
dotnet tool install --tool-path "$tools" --configfile "$config" DotnetTokenKiller --version "$version"

store="$tools/.store/dotnettokenkiller/$version_lower/dotnettokenkiller.$rid"
if [ ! -d "$store" ]; then
    echo "::error::Expected $store: the install did not pick the $rid package."
    ls -R "$tools/.store"
    exit 1
fi

# The hook as Claude Code, Gemini CLI, Copilot CLI, Codex CLI and Antigravity CLI run it. Falls back to
# sh when bash is absent, so it also runs inside the musl image.
sh "$repo/eng/hooks/check-hook-shells.sh" "$tools"

# DTK_AOT_REQUIRED=1 makes a missing binary or pack log fail these tests instead of skipping them.
DTK_AOT_REQUIRED=1 DTK_AOT_PACK_LOG="$log" DTK_AOT_BINARY="$tools/dtk" \
    dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build \
    --filter "FullyQualifiedName~DotnetTokenKiller.Cli.IntegrationTests.Aot" --verbosity normal

if [ "$integration_suite" = "--integration-suite" ]; then
    DTK_TEST_BINARY="$tools/dtk" \
        dotnet test tests/DotnetTokenKiller.Cli.IntegrationTests -c Release --no-build --verbosity normal
fi
