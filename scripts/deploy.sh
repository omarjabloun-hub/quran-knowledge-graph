#!/usr/bin/env bash
# Definitions + front end, then a first call to the dashboard so its code is compiled and its
# answer cached before anyone opens it. Does not ingest: run `just ingest` for data changes.
#
# Usage: scripts/deploy.sh [--target local|prod]
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
. "$SCRIPT_DIR/lib.sh"

load_env
parse_target "$@"

bash "$SCRIPT_DIR/upload-endpoints.sh" --target "$TARGET"
bash "$SCRIPT_DIR/upload-front-end.sh" --target "$TARGET"

step "Warming the dashboard"
workspace warm --target "$TARGET"
