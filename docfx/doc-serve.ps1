# doc-serve.ps1: Build and serve DotnetTokenKiller (DTK) documentation locally.

Push-Location $PSScriptRoot/..
$DOCFX_JSON = "docfx/docfx.json"

if (-not (Get-Command docfx -ErrorAction SilentlyContinue)) {
    Write-Host "Error: docfx is not installed." -ForegroundColor Red
    Write-Host "Please install it via 'dotnet tool install -g docfx'"
    Pop-Location
    exit 1
}

Write-Host ">>> Purging existing generated files..." -ForegroundColor Yellow
$PathsToPurge = @(
    "$PSScriptRoot/_site",
    "$PSScriptRoot/api"
)

foreach ($Path in $PathsToPurge) {
    if (Test-Path $Path) {
        Remove-Item -Recurse -Force $Path
    }
}

Write-Host ">>> Building and serving documentation from $DOCFX_JSON..." -ForegroundColor Cyan
try {
    docfx $DOCFX_JSON --serve
}
finally {
    Pop-Location
}
