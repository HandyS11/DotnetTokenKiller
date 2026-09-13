#!/bin/sh
# Fails if any ELF file in a tool package needs a glibc symbol version above the floor. The glibc Native
# AOT packages must start wherever .NET 10 does: glibc 2.27 by default.
#
# Usage: eng/aot/check-glibc-floor.sh <nupkg> [max-version]
#
# Needs unzip, readelf (binutils) and GNU sort.
set -eu

usage="usage: eng/aot/check-glibc-floor.sh <nupkg> [max-version]"
nupkg=${1:?$usage}
max=${2:-2.27}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
unzip -q "$nupkg" -d "$work/package"
find "$work/package" -type f | sort > "$work/files"

elf_count=0
failed=0
while IFS= read -r file; do
    if [ "$(head -c 4 "$file" | od -An -tx1 | tr -d ' \n')" != "7f454c46" ]; then
        continue
    fi
    elf_count=$((elf_count + 1))
    name=${file#"$work/package/"}
    if ! readelf --version-info -W "$file" > "$work/version-info"; then
        echo "::error::readelf failed on $name"
        exit 1
    fi
    highest=$(grep -o 'GLIBC_[0-9][0-9.]*' "$work/version-info" | sed 's/^GLIBC_//' | sort -uV | tail -n 1)
    if [ -z "$highest" ]; then
        echo "$name: needs no versioned glibc symbol"
        continue
    fi
    if [ "$(printf '%s\n%s\n' "$highest" "$max" | sort -V | tail -n 1)" != "$max" ]; then
        echo "::error::$name needs GLIBC_$highest, above the GLIBC_$max floor"
        failed=1
    else
        echo "$name: highest glibc symbol version GLIBC_$highest (floor GLIBC_$max)"
    fi
done < "$work/files"

if [ "$elf_count" -eq 0 ]; then
    echo "::error::$nupkg contains no ELF file"
    exit 1
fi
exit "$failed"
