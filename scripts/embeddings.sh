#!/usr/bin/env bash
# Switches the model that embeds the Arabic of every verse: harrier (built in, CPU) or ollama
# (OLLAMA_EMBEDDING_MODEL on the Mac's GPU). Deletes the other provider's index, records the
# choice in .env (EMBEDDING_PROVIDER) and uploads the definitions, which creates the new index;
# the workspace then embeds the 6,236 verses again (watch it with `just status`).
#
# Usage: scripts/embeddings.sh harrier|ollama [--target local|prod]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
. "$SCRIPT_DIR/lib.sh"

load_env
parse_target "$@"
resolve_target

PROVIDER="${1:-}"
case "$PROVIDER" in
  harrier|ollama) ;;
  *) fail "Usage: scripts/embeddings.sh harrier|ollama [--target local|prod]" ;;
esac

if [ "$PROVIDER" = "ollama" ]; then
  MODEL="${OLLAMA_EMBEDDING_MODEL:-qwen3-embedding:8b}"
  step "Checking Ollama on this machine"
  curl -sS -o /dev/null http://localhost:11434/api/tags \
    || fail "Ollama is not answering on http://localhost:11434 — start the Ollama app (or 'ollama serve')."
  if command -v ollama >/dev/null 2>&1 && ! ollama list | awk '{print $1}' | grep -qx "$MODEL"; then
    fail "Model $MODEL is not pulled yet: ollama pull $MODEL"
  fi
  info "Ollama answers and has $MODEL."
fi

step "Switching the verse embeddings to $PROVIDER  [target: $TARGET]"
workspace use-embeddings "$PROVIDER" --target "$TARGET"

if [ "$TARGET" = "local" ]; then
  EMBEDDING_PROVIDER="$PROVIDER"
  export EMBEDDING_PROVIDER
fi

bash "$SCRIPT_DIR/upload-endpoints.sh" --target "$TARGET"

step "Embedding"
info "The workspace is re-embedding the verses. Follow it with: just status"
