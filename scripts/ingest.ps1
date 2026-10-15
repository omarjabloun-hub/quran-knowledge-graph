# Writes the graph: schema, then every node and edge (QuranKG.Connector). Idempotent - re-run
# it after changing the connector; nothing is deleted. `--dry-run` maps the corpus and prints the
# counters without contacting the workspace. `--only tafsir` runs only those steps (comma-separated:
# structure, language, verses, words, topics, qursim, tafsir, hadith) and leaves the rest of the graph as it is.
#
# Usage: scripts/ingest.ps1 [--target local|prod] [--dry-run] [--only <steps>]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args
Resolve-Target $target
Assert-Command dotnet "Install the .NET 10 SDK."

$dry  = $args -contains "--dry-run"
$only = @()
$i    = [Array]::IndexOf($args, "--only")
if ($i -ge 0 -and $i + 1 -lt $args.Count) { $only = @("--only", $args[$i + 1]) }
if (-not (Test-Path (Join-Path $script:RootDir "data/raw"))) { Stop-WithError "No data/raw/ - run 'just fetch' first." }

if (-not $dry) {
    Assert-ApiToken $target
    Assert-WorkspaceReachable
    Write-Step "Ingesting into $env:CURIOSITY_URL  [target: $target]"
}
else { Write-Step "Dry run: mapping the corpus, nothing is written" }

Push-Location $script:RootDir
try {
    if ($dry) { Invoke-Checked "ingest" { dotnet run --project $script:ConnectorCsproj -c Release -- --dry-run @only } }
    else      { Invoke-Checked "ingest" { dotnet run --project $script:ConnectorCsproj -c Release -- @only } }
}
finally { Pop-Location }

# The connector only adds and updates: the hadith-verse links an earlier run found and this one did
# not are deleted in the workspace (qkg/hadith/prune-quotes), when this run wrote the hadith.
if (-not $dry -and ($only.Count -eq 0 -or ("," + $only[1] + ",") -like "*,hadith,*")) {
    Write-Step "Pruning the hadith-verse links this run did not find"
    Invoke-Workspace call qkg/hadith/prune-quotes "{}" --target $target
}
