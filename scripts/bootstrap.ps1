# Everything, from an empty machine to the dashboard, on the LOCAL workspace:
#   start the workspace -> mint tokens -> fetch and check the data -> ingest -> definitions ->
#   front end -> warm -> passages and themes -> status. Each step is idempotent, so it is
#   safe to re-run after a failure.
. (Join-Path $PSScriptRoot "lib.ps1")

$envFile = Join-Path $script:RootDir ".env"
if (-not (Test-Path $envFile)) { Copy-Item (Join-Path $script:RootDir ".env.example") $envFile }

& (Join-Path $PSScriptRoot "check-prereqs.ps1")
& (Join-Path $PSScriptRoot "init-workspace.ps1")

Import-DotEnv
Write-Step "Minting tokens"
Invoke-Workspace tokens
Import-DotEnv

Write-Step "Fetching the data"
Invoke-Checked "fetch" { dotnet run --project $script:ToolsCsproj -c Release -- fetch }
Write-Step "Checking the corpus"
Invoke-Checked "check" { dotnet run --project $script:ToolsCsproj -c Release -- check }

& (Join-Path $PSScriptRoot "ingest.ps1")
& (Join-Path $PSScriptRoot "deploy.ps1")

Write-Step "Building the passages and themes"
Invoke-Workspace clusters

Write-Step "Status"
Invoke-Workspace status

$user = if ($env:WORKSPACE_ADMIN_USER) { $env:WORKSPACE_ADMIN_USER } else { "admin" }
Write-Step "Ready"
Write-Info "Open $env:CURIOSITY_URL and sign in as $user."
Write-Info "The verses' Arabic is being embedded in the background (Verse.TextSimple above); until it"
Write-Info "finishes, 'Similar verses' ranks by shared lemmas and roots only."
