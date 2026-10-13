# Switches the model that embeds the Arabic of every verse: harrier (built in, CPU), ollama
# (OLLAMA_EMBEDDING_MODEL on this machine's GPU) or arabic (EMBEDDING_SERVER_MODEL - AraModernBert-STS,
# the best measured - served by tools/embedding-server). Deletes the other provider's index, records the
# choice in .env (EMBEDDING_PROVIDER) and uploads the definitions, which creates the new index;
# the workspace then embeds the 6,236 verses again (watch it with `just status`).
#
# Usage: scripts/embeddings.ps1 harrier|ollama|arabic [--target local|prod]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args
Resolve-Target $target

$provider = if ($args.Count -gt 0) { $args[0] } else { "" }
if ($provider -notin @("harrier", "ollama", "arabic")) { Stop-WithError "Usage: scripts/embeddings.ps1 harrier|ollama|arabic [--target local|prod]" }

if ($provider -eq "ollama") {
    $model = if ($env:OLLAMA_EMBEDDING_MODEL) { $env:OLLAMA_EMBEDDING_MODEL } else { "qwen3-embedding:8b" }
    Write-Step "Checking Ollama on this machine"
    try { Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -UseBasicParsing -TimeoutSec 10 | Out-Null }
    catch { Stop-WithError "Ollama is not answering on http://localhost:11434 - start the Ollama app (or 'ollama serve')." }
    if ((Get-Command ollama -ErrorAction SilentlyContinue) -and -not ((ollama list) -match ("^" + [regex]::Escape($model) + "\s"))) {
        Stop-WithError "Model $model is not pulled yet: ollama pull $model"
    }
    Write-Info "Ollama answers and has $model."
}

if ($provider -eq "arabic") {
    Write-Step "Checking the embeddings server on this machine"
    & (Join-Path $PSScriptRoot "embedding-server.ps1") status
    if ($LASTEXITCODE -ne 0) { Stop-WithError "Start it first: just embedding-server start - the workspace embeds every verse through it, and search sends it the query." }
    if ($target -ne "local") { Write-Warn "A production workspace must reach EMBEDDING_SERVER_URL itself: serve the model where it can." }
}

Write-Step "Switching the verse embeddings to $provider  [target: $target]"
Invoke-Workspace use-embeddings $provider --target $target

if ($target -eq "local") { $env:EMBEDDING_PROVIDER = $provider }

& (Join-Path $PSScriptRoot "upload-endpoints.ps1") --target $target

Write-Step "Embedding"
Write-Info "The workspace is re-embedding the verses. Follow it with: just status"
