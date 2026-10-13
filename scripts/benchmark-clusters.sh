#!/usr/bin/env bash
# The clusters benchmark (tools/cluster-benchmark): scores the passages against the ruku', the
# themes and the similar surahs against QurSim. Creates its Python environment (numpy only) the
# first time, and the verse table with the embedding benchmark's prep.py if it is missing.
# Arguments pass through to evaluate.py, e.g.:
#   scripts/benchmark-clusters.sh --split test              the held-out half of the surahs
#   scripts/benchmark-clusters.sh --target 180 --tau 0.12   try other settings
#   scripts/benchmark-clusters.sh --from build.json         score a workspace build (just clusters … --save)
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

BENCH="$ROOT_DIR/tools/cluster-benchmark"
VENV="$BENCH/.venv"
OUT="$ROOT_DIR/tools/embedding-benchmark/out"

find_python
[ -d "$ROOT_DIR/data/raw" ] || fail "No data/raw/ — run 'just fetch' first."

if [ ! -x "$VENV/bin/python" ]; then
  step "Creating the clusters benchmark's Python environment (.venv)"
  "$PYTHON" -m venv "$VENV"
  "$VENV/bin/pip" install --quiet --upgrade pip
  "$VENV/bin/pip" install --quiet -r "$BENCH/requirements.txt"
fi

if [ ! -f "$OUT/verses.jsonl" ]; then
  step "Preparing the verse table (tools/embedding-benchmark/prep.py)"
  "$VENV/bin/python" -I "$ROOT_DIR/tools/embedding-benchmark/prep.py" "$ROOT_DIR/data/raw" "$OUT"
fi

step "Scoring the clusters"
"$VENV/bin/python" -I "$BENCH/evaluate.py" "$OUT" "$@"
