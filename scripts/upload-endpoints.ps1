# Imports the workspace definitions - indexes, node styles, search settings, endpoints - from
# src/QuranKG.Definitions. The import is idempotent. The embeddings index on the verses' Arabic
# is the one EMBEDDING_PROVIDER names (harrier | ollama): see `just embeddings-ollama`.
#
# Ingest first: an endpoint naming a node type the graph does not have yet does not compile.
#
# Usage: scripts/upload-endpoints.ps1 [--target local|prod]
. (Join-Path $PSScriptRoot "lib.ps1")

Import-DotEnv
$target = Get-Target $args
Resolve-Target $target
Assert-Command dotnet        "Install the .NET 10 SDK."
Assert-Command curiosity-cli "dotnet tool install --global Curiosity.CLI"
Assert-ApiToken $target
Assert-WorkspaceReachable

$stage = Join-Path ([IO.Path]::GetTempPath()) ("qkg-defs-" + [Guid]::NewGuid().ToString("N"))
try {
    $provider = if ($env:EMBEDDING_PROVIDER) { $env:EMBEDDING_PROVIDER } else { "harrier" }
    Write-Step "Staging the definitions (embeddings: $provider)"
    Invoke-Workspace stage-definitions (Join-Path $stage "defs")

    Write-Step "Importing into $env:CURIOSITY_URL  [target: $target]"
    Invoke-Checked "import" { curiosity-cli import-workspace-definitions -s $env:CURIOSITY_URL -t $env:CURIOSITY_API_TOKEN --import-path (Join-Path $stage "defs") -z }
}
finally { if (Test-Path $stage) { Remove-Item -Recurse -Force $stage } }

Write-Step "Done"
Write-Info "Endpoint responses are cached for an hour, keyed on each endpoint's own code: after changing"
Write-Info "only code/endpoints/qkg/_lib/dto.cs, restart the workspace (just down; just up) to see it."
