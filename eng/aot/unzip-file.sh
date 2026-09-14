#!/bin/sh
# Extracts one entry of a zip (a .nupkg) to a file with whatever the machine has: unzip, Windows' own
# bsdtar (Git Bash's tar is GNU tar, which reads no zip), or python3/python.
#
# Usage: sh eng/aot/unzip-file.sh <zip> <entry> <destination>
set -eu

usage="usage: sh eng/aot/unzip-file.sh <zip> <entry> <destination>"
zip=${1:?$usage}
entry=${2:?$usage}
dest=${3:?$usage}

if command -v unzip >/dev/null 2>&1; then
    unzip -p "$zip" "$entry" > "$dest"
    exit 0
fi

if [ -n "${SYSTEMROOT:-}" ] && command -v cygpath >/dev/null 2>&1; then
    bsdtar="$(cygpath -u "$SYSTEMROOT")/System32/tar.exe"
    if [ -x "$bsdtar" ]; then
        tmp=$(mktemp -d)
        "$bsdtar" -xf "$(cygpath -w "$zip")" -C "$(cygpath -w "$tmp")" "$entry"
        mv "$tmp/$entry" "$dest"
        rm -rf "$tmp"
        exit 0
    fi
fi

# Many Linux images ship only python3, and Windows installs often only python.
if command -v python3 >/dev/null 2>&1; then
    python=python3
elif command -v python >/dev/null 2>&1; then
    python=python
else
    echo "unzip-file.sh: need unzip, Windows' tar.exe, python3 or python to extract $entry from $zip" >&2
    exit 1
fi

"$python" - "$zip" "$entry" "$dest" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive, open(sys.argv[3], 'wb') as out:
    out.write(archive.read(sys.argv[2]))
PY
