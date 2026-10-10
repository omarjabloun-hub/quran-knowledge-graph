# First run: create .env from the template (never overwrites one), then check the prerequisites.
. (Join-Path $PSScriptRoot "lib.ps1")

Write-Step "Environment file"
$envFile = Join-Path $script:RootDir ".env"
if (Test-Path $envFile) { Write-Info ".env already exists - left untouched." }
else { Copy-Item (Join-Path $script:RootDir ".env.example") $envFile; Write-Info "created .env from .env.example." }

& (Join-Path $PSScriptRoot "check-prereqs.ps1")

Write-Step "Next"
Write-Info "just bootstrap      start the workspace, fetch the data, ingest it, deploy everything"
Write-Info "                    (about 15 minutes the first time; see docs/local-setup.md)"
