#!/bin/sh
# Runs an installed dtk tool on Rocky Linux 8 (glibc 2.28), the oldest glibc distribution .NET 10 supports:
# it must start, filter a build log and record it in the statically linked SQLite tracking database.
#
# Usage: eng/aot/smoke-old-glibc.sh <tools-dir>
#
# <tools-dir> is a `dotnet tool install --tool-path` directory. Needs Docker; runs the image for the host's
# architecture, with the checkout and the tools directory mounted read-only.
set -eu

if [ "${1:-}" = "--in-container" ]; then
    dnf install -y -q libicu > /dev/null
    ldd --version | head -n 1
    state=$(mktemp -d)
    export DTK_CONFIG_PATH="$state/config.json" DTK_DB_PATH="$state/tracking.db" DTK_TEE_DIR="$state/logs" HOME="$state"

    /tools/dtk --version

    status=0
    /tools/dtk pipe build --exit-code 1 < /repo/tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_build_errors.txt > "$state/pipe.txt" || status=$?
    cat "$state/pipe.txt"
    if [ "$status" -ne 1 ] || ! grep -q '^dotnet build: 3 errors' "$state/pipe.txt"; then
        echo "::error::dtk pipe build exited $status without the build summary"
        exit 1
    fi

    /tools/dtk gain --json > "$state/gain.json"
    if ! grep -q '"TotalCommands":1,' "$state/gain.json"; then
        cat "$state/gain.json"
        echo "::error::the pipe run was not recorded in the tracking database"
        exit 1
    fi
    echo "dtk started, filtered and tracked on $(grep '^PRETTY_NAME=' /etc/os-release | cut -d= -f2- | tr -d '"')"
    exit 0
fi

tools=${1:?usage: eng/aot/smoke-old-glibc.sh <tools-dir>}
tools=$(cd "$tools" && pwd)
repo=$(cd "$(dirname "$0")/../.." && pwd)
docker run --rm -v "$repo:/repo:ro" -v "$tools:/tools:ro" rockylinux:8 sh /repo/eng/aot/smoke-old-glibc.sh --in-container
