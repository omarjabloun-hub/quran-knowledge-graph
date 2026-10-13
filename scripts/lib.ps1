# Shared helpers. Dot-sourced by the other scripts in this folder, never run directly.
# Arguments mirror the .sh scripts (`--target local|prod`) so the Justfile calls either family
# with the same flags.

$ErrorActionPreference = "Stop"

$script:RootDir = Split-Path -Parent $PSScriptRoot

function Write-Step($message) { Write-Host ""; Write-Host "==> $message" -ForegroundColor White }
function Write-Info($message) { Write-Host "    $message" }
function Write-Warn($message) { Write-Host "    ! $message" -ForegroundColor Yellow }
function Stop-WithError($message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }

function Import-DotEnv {
    $envFile = Join-Path $script:RootDir ".env"
    if (-not (Test-Path $envFile)) {
        Write-Warn "No .env at $envFile - falling back to defaults. Run 'just setup' to create one."
    }
    else {
        Get-Content $envFile | ForEach-Object {
            $line = $_.Trim()
            if ($line -and -not $line.StartsWith("#")) {
                $eq = $line.IndexOf("=")
                if ($eq -gt 0) {
                    $key = $line.Substring(0, $eq).Trim()
                    $val = $line.Substring($eq + 1).Trim().Trim('"').Trim("'")
                    [Environment]::SetEnvironmentVariable($key, $val, "Process")
                }
            }
        }
    }
    # The dotnet tools land here; a fresh shell may not have it on PATH yet.
    $tools = Join-Path $HOME ".dotnet/tools"
    if (-not ($env:PATH -split [IO.Path]::PathSeparator -contains $tools)) { $env:PATH += [IO.Path]::PathSeparator + $tools }
}

function Assert-Command($name, $hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { Stop-WithError "Missing required command: $name. $hint" }
}

# A working Python 3.11+ for the benchmarks' virtual environments: the versioned names first (macOS
# puts its own 3.9 first on PATH as python3), then python3 and python, then uv's own Python. A
# candidate whose pyexpat does not load is skipped: pip fails inside `python -m venv` without it.
function Find-Python {
    $check = "import sys, pyexpat; sys.exit(sys.version_info < (3, 11))"
    foreach ($candidate in "python3.12", "python3.13", "python3.11", "python3", "python") {
        if (-not (Get-Command $candidate -ErrorAction SilentlyContinue)) { continue }
        & $candidate -c $check 2>$null
        if ($LASTEXITCODE -eq 0) { return $candidate }
    }
    if (Get-Command uv -ErrorAction SilentlyContinue) {
        $found = (& uv python find 3.12 2>$null)
        if ($found) { & $found -c $check 2>$null; if ($LASTEXITCODE -eq 0) { return $found } }
    }
    Stop-WithError "No working Python 3.11+ found. Install one: uv python install 3.12 (or https://www.python.org/downloads/)"
}

# Reads --target <local|prod> out of the arguments and returns it.
function Get-Target($argList) {
    $target = "local"
    for ($i = 0; $i -lt $argList.Count; $i++) {
        if ($argList[$i] -eq "--target" -and $i + 1 -lt $argList.Count) { $target = $argList[$i + 1] }
        elseif ($argList[$i] -like "--target=*") { $target = $argList[$i].Split("=", 2)[1] }
    }
    return $target
}

# Points CURIOSITY_URL and the two tokens at the chosen workspace. The bare variables in .env
# are the local workspace, the PROD_ ones production; nothing falls back from one to the other.
function Resolve-Target($target) {
    switch ($target) {
        "local" { if (-not $env:CURIOSITY_URL) { $env:CURIOSITY_URL = "http://localhost:8080/" } }
        "prod" {
            $env:CURIOSITY_URL = $env:PROD_CURIOSITY_URL
            $env:CURIOSITY_API_TOKEN = $env:PROD_CURIOSITY_API_TOKEN
            $env:INTERFACE_UPLOAD_TOKEN = $env:PROD_INTERFACE_UPLOAD_TOKEN
            if (-not $env:CURIOSITY_URL) { Stop-WithError "--target prod, but PROD_CURIOSITY_URL is not set in .env" }
        }
        default { Stop-WithError "Unknown --target '$target' (expected: local | prod)" }
    }
}

function Assert-ApiToken($target) {
    if (-not $env:CURIOSITY_API_TOKEN -or $env:CURIOSITY_API_TOKEN.StartsWith("replace-with-")) {
        $name = if ($target -eq "prod") { "PROD_CURIOSITY_API_TOKEN" } else { "CURIOSITY_API_TOKEN" }
        Stop-WithError "No Library token for target '$target'. Run 'just tokens' (or set $name in .env)."
    }
}

function Assert-UploadToken($target) {
    if (-not $env:INTERFACE_UPLOAD_TOKEN -or $env:INTERFACE_UPLOAD_TOKEN.StartsWith("replace-with-")) {
        $name = if ($target -eq "prod") { "PROD_INTERFACE_UPLOAD_TOKEN" } else { "INTERFACE_UPLOAD_TOKEN" }
        Stop-WithError "No Interface Upload token for target '$target'. Run 'just tokens' (or set $name in .env)."
    }
}

# Connectivity only: any HTTP answer proves the workspace is up (a fresh one answers 401).
function Test-WorkspaceReachable {
    try { Invoke-WebRequest -Uri $env:CURIOSITY_URL -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop | Out-Null; return $true }
    catch { return $null -ne $_.Exception.Response }
}

function Assert-WorkspaceReachable {
    if (-not (Test-WorkspaceReachable)) { Stop-WithError "Workspace not reachable at $env:CURIOSITY_URL. For the local one, run 'just up' first." }
}

# The .NET operations on a running workspace (src/QuranKG.Workspace): tokens, status, call, ...
function Invoke-Workspace {
    dotnet run --project (Join-Path $script:RootDir "src/QuranKG.Workspace") -c Release -- @args
    if ($LASTEXITCODE -ne 0) { Stop-WithError "QuranKG.Workspace $($args -join ' ') failed." }
}

function Invoke-Checked {
    param([string]$what, [scriptblock]$block)
    & $block
    if ($LASTEXITCODE -ne 0) { Stop-WithError "$what failed (exit code $LASTEXITCODE)." }
}

$script:ConnectorCsproj = Join-Path $script:RootDir "src/QuranKG.Connector/QuranKG.Connector.csproj"
$script:ToolsCsproj     = Join-Path $script:RootDir "src/QuranKG.Tools/QuranKG.Tools.csproj"
$script:FrontEndCsproj  = Join-Path $script:RootDir "src/QuranKG.FrontEnd/QuranKG.FrontEnd.csproj"
$script:FrontEndOutput  = Join-Path $script:RootDir "src/QuranKG.FrontEnd/bin/Release/netstandard2.0/tps"
