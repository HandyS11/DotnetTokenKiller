#!/bin/sh
# Packs dtk's Native AOT tool package for a Linux RID inside Microsoft's cross-build image for that RID,
# against the image's sysroot: glibc 2.27 (an Ubuntu 18.04 rootfs, the floor .NET itself supports) for
# linux-x64 and linux-arm64, musl 1.2.3 (an Alpine 3.17 rootfs) for the musl RIDs.
#
# Usage: eng/aot/pack-linux.sh <rid> <version> <feed-dir> <pack-log> [extra MSBuild arguments...]
#
# Needs Docker and the .NET SDK on the host. The images (about 7 GB each) carry no SDK: the host's SDK,
# the checkout, the NuGet cache, the feed and the log directory are mounted at their host paths, and the
# pack runs as the invoking user, so no root-owned files are left behind and git still reads the checkout
# (the informational version carries the commit SHA, which the parity tests compare).
set -eu

usage="usage: eng/aot/pack-linux.sh <rid> <version> <feed-dir> <pack-log> [extra MSBuild arguments...]"
rid=${1:?$usage}
version=${2:?$usage}
feed=${3:?$usage}
log=${4:?$usage}
shift 4

case "$rid" in
    linux-x64) tag=cross-amd64; arch=x64 ;;
    linux-arm64) tag=cross-arm64; arch=arm64 ;;
    linux-musl-x64) tag=cross-amd64-musl; arch=x64 ;;
    linux-musl-arm64) tag=cross-arm64-musl; arch=arm64 ;;
    *) echo "pack-linux.sh: unsupported RID '$rid'" >&2; exit 2 ;;
esac
image="mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-$tag"

repo=$(cd "$(dirname "$0")/../.." && pwd)
dotnet_root=${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}
nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}
mkdir -p "$feed" "$nuget" "$(dirname "$log")"
feed=$(cd "$feed" && pwd)
log_dir=$(cd "$(dirname "$log")" && pwd)
log="$log_dir/$(basename "$log")"

echo "Packing $rid $version in $image"
docker run --rm \
    --user "$(id -u):$(id -g)" \
    -v "$repo:$repo" -v "$dotnet_root:$dotnet_root:ro" -v "$nuget:$nuget" -v "$feed:$feed" -v "$log_dir:$log_dir" \
    -w "$repo" \
    -e HOME=/tmp -e DOTNET_ROOT="$dotnet_root" -e NUGET_PACKAGES="$nuget" \
    -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    "$image" \
    "$dotnet_root/dotnet" pack src/DotnetTokenKiller.Cli -c Release -r "$rid" \
    -p:IncludeSymbols=false -p:Version="$version" -p:SysRoot="/crossrootfs/$arch" -p:LinkerFlavor=lld \
    -o "$feed" "-flp:LogFile=$log;Verbosity=minimal" "$@"
