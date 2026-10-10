#!/usr/bin/env bash
# First run: create .env from the template (never overwrites one), then check the prerequisites.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
. "$SCRIPT_DIR/lib.sh"

step "Environment file"
if [ -f "$ROOT_DIR/.env" ]; then
  info ".env already exists — left untouched."
else
  cp "$ROOT_DIR/.env.example" "$ROOT_DIR/.env"
  info "created .env from .env.example."
fi

bash "$SCRIPT_DIR/check-prereqs.sh"

step "Next"
info "just bootstrap      start the workspace, fetch the data, ingest it, deploy everything"
info "                    (about 15 minutes the first time; see docs/local-setup.md)"
