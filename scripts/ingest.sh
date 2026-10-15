#!/usr/bin/env bash
# Writes the graph: schema, then every node and edge (QuranKG.Connector). Idempotent — re-run
# it after changing the connector; nothing is deleted. `--dry-run` maps the corpus and prints the
# counters without contacting the workspace. `--only tafsir` runs only those steps (comma-separated:
# structure, language, verses, words, topics, qursim, tafsir, hadith) and leaves the rest of the graph as it is.
#
# Usage: scripts/ingest.sh [--target local|prod] [--dry-run] [--only <steps>]
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

load_env
parse_target "$@"
resolve_target
require_command dotnet "Install the .NET 10 SDK."

dry=""
only=()
expect_only=0
for arg in "$@"; do
  if [ "$expect_only" = "1" ]; then only=(--only "$arg"); expect_only=0; continue; fi
  case "$arg" in
    --dry-run) dry="--dry-run" ;;
    --only)    expect_only=1 ;;
    --only=*)  only=(--only "${arg#*=}") ;;
  esac
done

[ -d "$ROOT_DIR/data/raw" ] || fail "No data/raw/ — run 'just fetch' first."

if [ -z "$dry" ]; then
  require_api_token
  require_workspace_reachable
  step "Ingesting into $CURIOSITY_URL  [target: $TARGET]"
else
  step "Dry run: mapping the corpus, nothing is written"
fi

cd "$ROOT_DIR"
dotnet run --project "$CONNECTOR_CSPROJ" -c Release -- $dry ${only[@]+"${only[@]}"}

# The connector only adds and updates: the hadith–verse links an earlier run found and this one did
# not are deleted in the workspace (qkg/hadith/prune-quotes), when this run wrote the hadith.
if [ -z "$dry" ] && { [ ${#only[@]} -eq 0 ] || [[ ",${only[1]}," == *",hadith,"* ]]; }; then
  step "Pruning the hadith–verse links this run did not find"
  workspace call qkg/hadith/prune-quotes '{}' --target "$TARGET"
fi
