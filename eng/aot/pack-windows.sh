#!/bin/sh
# Packs the Windows x64 tool package: a framework-dependent RID package whose packaged shim is the Native AOT
# dtk.exe published just before (UseNativeBinaryAsPackagedShim in the CLI csproj). Runs on Windows under
# Git Bash, as CI does; needs the .NET 10 SDK and the MSVC toolset the AOT compile needs.
#
# Usage: sh eng/aot/pack-windows.sh <version> <feed-dir> <publish-dir> <publish-log>
#
# Also writes artifacts/aot/win-x64-symbols/dtk.pdb: the framework-dependent pack below rebuilds
# bin/Release/net10.0/win-x64 and may clean the native/ subdirectory the AOT publish left there, so the
# symbols are copied out immediately after the AOT publish succeeds, before that pack runs. publish.yml
# requires native symbols for every RID the pointer lists; Task 16 uploads symbols-win-x64 from there.
set -eu

usage="usage: sh eng/aot/pack-windows.sh <version> <feed-dir> <publish-dir> <publish-log>"
version=${1:?$usage}
feed=${2:?$usage}
publish=${3:?$usage}
log=${4:?$usage}
repo=$(cd "$(dirname "$0")/../.." && pwd)

cd "$repo"
mkdir -p "$feed" "$publish" "$(dirname "$log")"
feed=$(cd "$feed" && pwd)
publish=$(cd "$publish" && pwd)
log="$(cd "$(dirname "$log")" && pwd)/$(basename "$log")"

# The parity tests compare against this JIT build, so it must carry the packs' version.
dtk dotnet build DotnetTokenKiller.slnx -c Release -p:Version="$version"

# The native binary. AotWarningLogTests reads this log: its IL warnings must be exactly Spectre's three.
dotnet publish src/DotnetTokenKiller.Cli -c Release -r win-x64 -p:Version="$version" -o "$publish" \
    "-flp:LogFile=$log;Verbosity=minimal"
exe="$publish/dtk.exe"
if [ ! -f "$exe" ]; then
    echo "pack-windows.sh: expected $exe" >&2
    exit 1
fi
if [ -f "$publish/e_sqlite3.dll" ]; then
    echo "pack-windows.sh: the AOT publish carries e_sqlite3.dll, so DtkUseWinSqlite3 did not apply" >&2
    exit 1
fi

# Copy the native symbols out now: the framework-dependent pack below reuses bin/Release/net10.0/win-x64
# and may clean this native/ subdirectory before publish.yml's later upload step would see it.
native_pdb="$repo/src/DotnetTokenKiller.Cli/bin/Release/net10.0/win-x64/native/dtk.pdb"
symbols_dir="$repo/artifacts/aot/win-x64-symbols"
mkdir -p "$symbols_dir"
if [ ! -f "$native_pdb" ]; then
    echo "pack-windows.sh: expected native symbols at $native_pdb" >&2
    exit 1
fi
cp "$native_pdb" "$symbols_dir/dtk.pdb"

# Framework-dependent (runner dotnet, entry point dtk.dll), with the SDK's apphost shim replaced by the exe.
dotnet pack src/DotnetTokenKiller.Cli -c Release -r win-x64 -p:PublishAot=false -p:UseAppHost=false \
    -p:IncludeSymbols=false -p:PackAsToolShimRuntimeIdentifiers=win-x64 -p:DtkPackagedShim="$exe" \
    -p:Version="$version" -o "$feed"
nupkg="$feed/DotnetTokenKiller.win-x64.$version.nupkg"
if [ ! -f "$nupkg" ]; then
    echo "pack-windows.sh: expected $nupkg" >&2
    exit 1
fi

# What was packed is what the SDK copies to ~/.dotnet/tools/dtk.exe: check it is the exe, byte for byte.
extract=$(mktemp -d)
sh "$repo/eng/aot/unzip-file.sh" "$nupkg" tools/net10.0/win-x64/shims/win-x64/dtk.exe "$extract/dtk.exe"
if ! cmp -s "$exe" "$extract/dtk.exe"; then
    echo "pack-windows.sh: the packaged shim is not the published dtk.exe" >&2
    exit 1
fi
rm -rf "$extract"
echo "Packed $nupkg ($(wc -c < "$nupkg") bytes) with the native shim ($(wc -c < "$exe") bytes)."
