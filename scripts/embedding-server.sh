#!/usr/bin/env bash
# The embeddings server (tools/embedding-server): EMBEDDING_SERVER_MODEL — AraModernBert-STS — on
# this Mac's GPU, behind the OpenAI embeddings API the workspace's external index calls. It runs
# in the background; its log and PID live in tools/embedding-server/ (gitignored).
#
# Usage: scripts/embedding-server.sh start|stop|status
#   start   creates the Python environment the first time, starts the server, waits for /health
#   stop    stops it
#   status  says whether it answers
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
. "$SCRIPT_DIR/lib.sh"

load_env

SERVER="$ROOT_DIR/tools/embedding-server"
VENV="$SERVER/.venv"
PID_FILE="$SERVER/server.pid"
LOG_FILE="$SERVER/server.log"
PORT="${EMBEDDING_SERVER_PORT:-8765}"
MODEL="${EMBEDDING_SERVER_MODEL:-NAMAA-Space/AraModernBert-Base-STS}"
HEALTH="http://127.0.0.1:$PORT/health"
TIMEOUT="${EMBEDDING_SERVER_STARTUP_TIMEOUT_SECONDS:-600}"

answers() { curl -fsS -o /dev/null "$HEALTH" 2>/dev/null; }
running_pid() {
  [ -f "$PID_FILE" ] || return 1
  local pid; pid="$(cat "$PID_FILE")"
  kill -0 "$pid" 2>/dev/null && echo "$pid"
}

case "${1:-}" in
  start)
    if answers; then
      info "The embeddings server already answers on port $PORT: $(curl -fsS "$HEALTH")"
      exit 0
    fi
    find_python
    if [ ! -x "$VENV/bin/python" ]; then
      step "Creating the embeddings server's Python environment (.venv)"
      "$PYTHON" -m venv "$VENV"
      "$VENV/bin/pip" install --quiet --upgrade pip
      "$VENV/bin/pip" install --quiet -r "$SERVER/requirements.txt"
    fi
    step "Starting the embeddings server: $MODEL on port $PORT"
    EMBEDDING_SERVER_MODEL="$MODEL" EMBEDDING_SERVER_PORT="$PORT" TOKENIZERS_PARALLELISM=false \
      nohup "$VENV/bin/python" -I "$SERVER/server.py" >"$LOG_FILE" 2>&1 &
    echo $! >"$PID_FILE"
    info "waiting for $HEALTH (up to ${TIMEOUT}s — the first start downloads the model)"
    elapsed=0
    until answers; do
      if ! running_pid >/dev/null; then
        tail -20 "$LOG_FILE" >&2 || true
        fail "The embeddings server exited — its log: $LOG_FILE"
      fi
      [ "$elapsed" -ge "$TIMEOUT" ] && fail "The embeddings server did not answer within ${TIMEOUT}s — its log: $LOG_FILE"
      sleep 2
      elapsed=$((elapsed + 2))
    done
    info "$(curl -fsS "$HEALTH")"
    ;;
  stop)
    if pid="$(running_pid)"; then
      kill "$pid"
      rm -f "$PID_FILE"
      info "Stopped the embeddings server (pid $pid)."
    else
      rm -f "$PID_FILE"
      info "The embeddings server is not running."
    fi
    ;;
  status)
    if answers; then info "Answers on port $PORT: $(curl -fsS "$HEALTH")"
    else             info "Not answering on port $PORT. Start it: just embedding-server start"; exit 1
    fi
    ;;
  *)
    fail "Usage: scripts/embedding-server.sh start|stop|status"
    ;;
esac
