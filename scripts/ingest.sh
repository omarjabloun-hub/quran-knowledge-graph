#!/usr/bin/env bash
# Writes the graph: schema, then every node and edge (QuranKG.Connector). Idempotent — re-run
# it after changing the connector; nothing is deleted. `--dry-run` maps the corpus and prints the
# counters without contacting the workspace.
#
# Usage: scripts/ingest.sh [--target local|prod] [--dry-run]
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

load_env
parse_target "$@"
resolve_target
require_command dotnet "Install the .NET 10 SDK."

dry=""
for arg in "$@"; do [ "$arg" = "--dry-run" ] && dry="--dry-run"; done

[ -d "$ROOT_DIR/data/raw" ] || fail "No data/raw/ — run 'just fetch' first."

if [ -z "$dry" ]; then
  require_api_token
  require_workspace_reachable
  step "Ingesting into $CURIOSITY_URL  [target: $TARGET]"
else
  step "Dry run: mapping the corpus, nothing is written"
fi

cd "$ROOT_DIR"
dotnet run --project "$CONNECTOR_CSPROJ" -c Release -- $dry
