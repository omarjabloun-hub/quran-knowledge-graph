# Starts the local workspace container and waits until it answers.
# The image, port, admin user and storage folder come from .env via docker-compose.yml.
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
Resolve-Target "local"
Assert-Command docker "Install Docker Desktop and make sure it is running."

$timeout = if ($env:WORKSPACE_STARTUP_TIMEOUT_SECONDS) { [int]$env:WORKSPACE_STARTUP_TIMEOUT_SECONDS } else { 900 }
$version = if ($env:CURIOSITY_VERSION) { $env:CURIOSITY_VERSION } else { "72111" }

Write-Step "Starting the workspace (curiosityai/curiosity:$version)"
Push-Location $script:RootDir
try { Invoke-Checked "docker compose up" { docker compose up -d workspace } } finally { Pop-Location }

Write-Info "waiting for $env:CURIOSITY_URL (up to ${timeout}s - the first run pulls a ~2 GB image)"
$elapsed = 0
while (-not (Test-WorkspaceReachable)) {
    if ($elapsed -ge $timeout) {
        Push-Location $script:RootDir; docker compose logs --tail 60 workspace; Pop-Location
        Stop-WithError "Workspace did not answer at $env:CURIOSITY_URL within ${timeout}s."
    }
    Start-Sleep -Seconds 3
    $elapsed += 3
}

if (Get-Command curiosity-cli -ErrorAction SilentlyContinue) {
    curiosity-cli wait-for -s $env:CURIOSITY_URL --max-timeout $timeout | Out-Null
}

$user = if ($env:WORKSPACE_ADMIN_USER) { $env:WORKSPACE_ADMIN_USER } else { "admin" }
Write-Step "Workspace is running at $env:CURIOSITY_URL"
Write-Info "Sign in as $user (password in .env). The first sign-in opens a setup assistant: 'Finish later' skips it."
