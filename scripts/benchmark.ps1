# The embedding benchmark (tools/embedding-benchmark): creates its Python environment the first
# time, then embeds the verses with every candidate model not done yet and scores them all on
# QurSim. Arguments pass through to run.py (see scripts/benchmark.sh for examples).
. (Join-Path $PSScriptRoot "lib.ps1")

$bench = Join-Path $script:RootDir "tools/embedding-benchmark"
$venv  = Join-Path $bench ".venv"
$py    = if ($IsWindows) { Join-Path $venv "Scripts/python.exe" } else { Join-Path $venv "bin/python" }

$python = Find-Python
if (-not (Test-Path (Join-Path $script:RootDir "data/raw"))) { Stop-WithError "No data/raw/ - run 'just fetch' first." }

if (-not (Test-Path $py)) {
    Write-Step "Creating the benchmark's Python environment (.venv)"
    Invoke-Checked "venv" { & $python -m venv $venv }
    Invoke-Checked "pip" { & $py -m pip install --quiet --upgrade pip }
    Invoke-Checked "pip" { & $py -m pip install --quiet -r (Join-Path $bench "requirements.txt") }
}

# Inside a script block $args is the block's own (empty) list, so the script's is kept first.
$passThrough = $args
Write-Step "Running the benchmark"
$env:TOKENIZERS_PARALLELISM = "false"
Push-Location $bench
try { Invoke-Checked "benchmark" { & $py -I run.py @passThrough } } finally { Pop-Location }
