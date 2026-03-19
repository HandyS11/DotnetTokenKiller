#!/bin/bash

# doc-serve.sh: Build and serve DotnetTokenKiller (DTK) documentation locally.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
pushd "$SCRIPT_DIR/.." > /dev/null

DOCFX_JSON="docfx/docfx.json"

if ! command -v docfx &> /dev/null
then
    echo "Error: docfx is not installed."
    echo "Please install it via 'dotnet tool install -g docfx'"
    popd > /dev/null
    exit 1
fi

echo ">>> Purging existing generated files..."
rm -rf "$SCRIPT_DIR/_site" "$SCRIPT_DIR/api"

echo ">>> Building and serving documentation from $DOCFX_JSON..."
docfx $DOCFX_JSON --serve

popd > /dev/null
