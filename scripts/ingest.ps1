# Writes the graph: schema, then every node and edge (QuranKG.Connector). Idempotent - re-run
# it after changing the connector; nothing is deleted. `--dry-run` maps the corpus and prints the
# counters without contacting the workspace.
#
# Usage: scripts/ingest.ps1 [--target local|prod] [--dry-run]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args
Resolve-Target $target
Assert-Command dotnet "Install the .NET 10 SDK."

$dry = $args -contains "--dry-run"
if (-not (Test-Path (Join-Path $script:RootDir "data/raw"))) { Stop-WithError "No data/raw/ - run 'just fetch' first." }

if (-not $dry) {
    Assert-ApiToken $target
    Assert-WorkspaceReachable
    Write-Step "Ingesting into $env:CURIOSITY_URL  [target: $target]"
}
else { Write-Step "Dry run: mapping the corpus, nothing is written" }

Push-Location $script:RootDir
try {
    if ($dry) { Invoke-Checked "ingest" { dotnet run --project $script:ConnectorCsproj -c Release -- --dry-run } }
    else      { Invoke-Checked "ingest" { dotnet run --project $script:ConnectorCsproj -c Release } }
}
finally { Pop-Location }
