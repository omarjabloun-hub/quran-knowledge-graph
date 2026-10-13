# The embeddings server (tools/embedding-server): EMBEDDING_SERVER_MODEL - AraModernBert-STS - on
# this machine's GPU, behind the OpenAI embeddings API the workspace's external index calls. It runs
# in the background; its log and PID live in tools/embedding-server/ (gitignored).
#
# Usage: scripts/embedding-server.ps1 start|stop|status
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv

$server  = Join-Path $script:RootDir "tools/embedding-server"
$venv    = Join-Path $server ".venv"
$py      = if ($IsWindows) { Join-Path $venv "Scripts/python.exe" } else { Join-Path $venv "bin/python" }
$pidFile = Join-Path $server "server.pid"
$logFile = Join-Path $server "server.log"
$port    = if ($env:EMBEDDING_SERVER_PORT) { $env:EMBEDDING_SERVER_PORT } else { "8765" }
$model   = if ($env:EMBEDDING_SERVER_MODEL) { $env:EMBEDDING_SERVER_MODEL } else { "NAMAA-Space/AraModernBert-Base-STS" }
$health  = "http://127.0.0.1:$port/health"
$timeout = if ($env:EMBEDDING_SERVER_STARTUP_TIMEOUT_SECONDS) { [int]$env:EMBEDDING_SERVER_STARTUP_TIMEOUT_SECONDS } else { 600 }

function Get-Health { try { (Invoke-WebRequest -Uri $health -UseBasicParsing -TimeoutSec 5).Content } catch { $null } }
function Get-ServerProcess {
    if (-not (Test-Path $pidFile)) { return $null }
    Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue
}

switch ($args[0]) {
    "start" {
        $answer = Get-Health
        if ($answer) { Write-Info "The embeddings server already answers on port ${port}: $answer"; exit 0 }
        $python = Find-Python
        if (-not (Test-Path $py)) {
            Write-Step "Creating the embeddings server's Python environment (.venv)"
            Invoke-Checked "venv" { & $python -m venv $venv }
            Invoke-Checked "pip" { & $py -m pip install --quiet --upgrade pip }
            Invoke-Checked "pip" { & $py -m pip install --quiet -r (Join-Path $server "requirements.txt") }
        }
        Write-Step "Starting the embeddings server: $model on port $port"
        $env:EMBEDDING_SERVER_MODEL = $model
        $env:EMBEDDING_SERVER_PORT  = $port
        $env:TOKENIZERS_PARALLELISM = "false"
        $process = Start-Process -FilePath $py -ArgumentList "-I", (Join-Path $server "server.py") -PassThru -NoNewWindow `
                                 -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err"
        Set-Content -Path $pidFile -Value $process.Id
        Write-Info "waiting for $health (up to ${timeout}s - the first start downloads the model)"
        $elapsed = 0
        while (-not ($answer = Get-Health)) {
            if (-not (Get-ServerProcess)) { Get-Content "$logFile.err" -Tail 20; Stop-WithError "The embeddings server exited - its log: $logFile.err" }
            if ($elapsed -ge $timeout) { Stop-WithError "The embeddings server did not answer within ${timeout}s - its log: $logFile.err" }
            Start-Sleep -Seconds 2
            $elapsed += 2
        }
        Write-Info $answer
    }
    "stop" {
        $process = Get-ServerProcess
        if ($process) { Stop-Process -Id $process.Id; Write-Info "Stopped the embeddings server (pid $($process.Id))." }
        else          { Write-Info "The embeddings server is not running." }
        Remove-Item $pidFile -ErrorAction SilentlyContinue
    }
    "status" {
        $answer = Get-Health
        if ($answer) { Write-Info "Answers on port ${port}: $answer" }
        else         { Write-Info "Not answering on port $port. Start it: just embedding-server start"; exit 1 }
    }
    default { Stop-WithError "Usage: scripts/embedding-server.ps1 start|stop|status" }
}
