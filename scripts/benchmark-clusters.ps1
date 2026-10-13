# The clusters benchmark (tools/cluster-benchmark): scores the passages against the ruku', the
# themes and the similar surahs against QurSim. Creates its Python environment (numpy only) the
# first time, and the verse table with the embedding benchmark's prep.py if it is missing.
# Arguments pass through to evaluate.py (see scripts/benchmark-clusters.sh for examples).
. (Join-Path $PSScriptRoot "lib.ps1")

$bench = Join-Path $script:RootDir "tools/cluster-benchmark"
$venv  = Join-Path $bench ".venv"
$out   = Join-Path $script:RootDir "tools/embedding-benchmark/out"
$py    = if ($IsWindows) { Join-Path $venv "Scripts/python.exe" } else { Join-Path $venv "bin/python" }

$python = Find-Python
if (-not (Test-Path (Join-Path $script:RootDir "data/raw"))) { Stop-WithError "No data/raw/ - run 'just fetch' first." }

if (-not (Test-Path $py)) {
    Write-Step "Creating the clusters benchmark's Python environment (.venv)"
    Invoke-Checked "venv" { & $python -m venv $venv }
    Invoke-Checked "pip" { & $py -m pip install --quiet --upgrade pip }
    Invoke-Checked "pip" { & $py -m pip install --quiet -r (Join-Path $bench "requirements.txt") }
}

if (-not (Test-Path (Join-Path $out "verses.jsonl"))) {
    Write-Step "Preparing the verse table (tools/embedding-benchmark/prep.py)"
    Invoke-Checked "prep" { & $py -I (Join-Path $script:RootDir "tools/embedding-benchmark/prep.py") (Join-Path $script:RootDir "data/raw") $out }
}

# Inside a script block $args is the block's own (empty) list, so the script's is kept first.
$passThrough = $args
Write-Step "Scoring the clusters"
Invoke-Checked "evaluate" { & $py -I (Join-Path $bench "evaluate.py") $out @passThrough }
