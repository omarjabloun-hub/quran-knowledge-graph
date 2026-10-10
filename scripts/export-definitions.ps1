# Exports the workspace's live definitions into .export/ (gitignored) - the round trip after a
# change made in the admin UI: diff .export/ against src/QuranKG.Definitions/, copy what you
# mean to keep, commit.
#
# Usage: scripts/export-definitions.ps1 [--target local|prod]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args
Resolve-Target $target
Assert-Command curiosity-cli "dotnet tool install --global Curiosity.CLI"
Assert-ApiToken $target
Assert-WorkspaceReachable

$out = Join-Path $script:RootDir ".export"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

Write-Step "Exporting from $env:CURIOSITY_URL into .export/"
Invoke-Checked "export" { curiosity-cli export-workspace-definitions -s $env:CURIOSITY_URL -t $env:CURIOSITY_API_TOKEN --export-path $out -u }

Write-Step "Compare"
Write-Info "git diff --no-index .export/code src/QuranKG.Definitions/code"
Write-Info "git diff --no-index .export/config src/QuranKG.Definitions/config"
