# Verifies the tools the repository needs, and says how to install what is missing.
. (Join-Path $PSScriptRoot "lib.ps1")
$tools = Join-Path $HOME ".dotnet/tools"
$env:PATH += [IO.Path]::PathSeparator + $tools

$script:missing = 0

function Test-Tool($name, $hint, $required) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) {
        $version = (& $name --version 2>$null | Select-Object -First 1)
        Write-Host ("  [ok] {0,-15} {1}" -f $name, $version) -ForegroundColor Green
    }
    elseif ($required) {
        Write-Host ("  [!!] {0,-15} MISSING - {1}" -f $name, $hint) -ForegroundColor Red
        $script:missing++
    }
    else {
        Write-Host ("  [..] {0,-15} optional - {1}" -f $name, $hint) -ForegroundColor Yellow
    }
}

Write-Step "Prerequisites"
Test-Tool dotnet        "install the .NET 10 SDK from https://dotnet.microsoft.com/download" $true
Test-Tool tps           "dotnet tool install --global Transpose.Compiler"                   $true
Test-Tool curiosity-cli "dotnet tool install --global Curiosity.CLI"                        $true
Test-Tool docker        "install Docker Desktop and start it"                               $true
Test-Tool just          "winget install Casey.Just  /  brew install just"                   $false
Test-Tool ollama        "https://ollama.com/download - only for local models (docs/models.md)" $false
Test-Tool python        "only for the embedding benchmark (tools/embedding-benchmark)"      $false

if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and -not ((dotnet --list-sdks) -match '^(1[0-9])\.')) {
    Write-Host "  [!!] .NET 10 SDK     MISSING" -ForegroundColor Red
    $script:missing++
}

Write-Step "Configuration"
if (Test-Path (Join-Path $script:RootDir ".env")) { Write-Host "  [ok] .env present" -ForegroundColor Green }
else { Write-Host "  [..] .env missing - run: just setup" -ForegroundColor Yellow }

if ($script:missing -gt 0) { Write-Host ""; Write-Host "$($script:missing) required tool(s) missing." -ForegroundColor Red; exit 1 }
Write-Host ""; Write-Host "All required tools present." -ForegroundColor Green
