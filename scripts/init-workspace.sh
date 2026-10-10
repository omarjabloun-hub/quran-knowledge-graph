#!/usr/bin/env bash
# Starts the local workspace container and waits until it answers.
# The image, port, admin user and storage folder come from .env via docker-compose.yml.
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

load_env
TARGET="local"
resolve_target

require_command docker "Install Docker Desktop and make sure it is running."
require_command curl   "Install curl."

TIMEOUT="${WORKSPACE_STARTUP_TIMEOUT_SECONDS:-900}"

step "Starting the workspace (curiosityai/curiosity:${CURIOSITY_VERSION:-72111})"
cd "$ROOT_DIR"
docker compose up -d workspace

info "waiting for $CURIOSITY_URL (up to ${TIMEOUT}s — the first run pulls a ~3 GB image)"
elapsed=0
until curl -sS -o /dev/null "$CURIOSITY_URL" 2>/dev/null; do
  if [ "$elapsed" -ge "$TIMEOUT" ]; then
    docker compose logs --tail 60 workspace || true
    fail "Workspace did not answer at $CURIOSITY_URL within ${TIMEOUT}s."
  fi
  sleep 3
  elapsed=$((elapsed + 3))
done

if command -v curiosity-cli >/dev/null 2>&1; then
  curiosity-cli wait-for -s "$CURIOSITY_URL" --max-timeout "$TIMEOUT" >/dev/null 2>&1 || true
fi

step "Workspace is running at $CURIOSITY_URL"
info "Sign in as ${WORKSPACE_ADMIN_USER:-admin} (password in .env). The first sign-in opens a setup"
info "assistant: 'Finish later' skips it."
