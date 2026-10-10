# Definitions + front end, then a first call to the dashboard so its code is compiled and its
# answer cached before anyone opens it. Does not ingest: run `just ingest` for data changes.
#
# Usage: scripts/deploy.ps1 [--target local|prod]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args

& (Join-Path $PSScriptRoot "upload-endpoints.ps1") --target $target
& (Join-Path $PSScriptRoot "upload-front-end.ps1") --target $target

Write-Step "Warming the dashboard"
Invoke-Workspace warm --target $target
