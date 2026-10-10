#!/usr/bin/env bash
# The embedding benchmark (tools/embedding-benchmark): creates its Python environment the first
# time, then embeds the verses with every candidate model not done yet and scores them all on
# QurSim. Arguments pass through to run.py, e.g.:
#   scripts/benchmark.sh --only qwen3       just the Qwen3 models
#   scripts/benchmark.sh --skip-hf          only the Ollama models (pull them first)
#   scripts/benchmark.sh --evaluate-only fused=qwen3-embedding-8b__arabic_plain__ollama:1,shared_lemmas:1,shared_roots:0.5
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

BENCH="$ROOT_DIR/tools/embedding-benchmark"
VENV="$BENCH/.venv"

require_command python3 "Install Python 3.11+ (brew install python@3.12)."
[ -d "$ROOT_DIR/data/raw" ] || fail "No data/raw/ — run 'just fetch' first."

if [ ! -x "$VENV/bin/python" ]; then
  step "Creating the benchmark's Python environment (.venv)"
  python3 -m venv "$VENV"
  "$VENV/bin/pip" install --quiet --upgrade pip
  "$VENV/bin/pip" install --quiet -r "$BENCH/requirements.txt"
fi

step "Running the benchmark"
cd "$BENCH"
TOKENIZERS_PARALLELISM=false "$VENV/bin/python" -I run.py "$@"
