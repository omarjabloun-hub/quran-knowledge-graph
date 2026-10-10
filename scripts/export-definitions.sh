#!/usr/bin/env bash
# Exports the workspace's live definitions into .export/ (gitignored) — the round trip after a
# change made in the admin UI: diff .export/ against src/QuranKG.Definitions/, copy what you
# mean to keep, commit.
#
# Usage: scripts/export-definitions.sh [--target local|prod]
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

load_env
parse_target "$@"
resolve_target
require_command curiosity-cli "dotnet tool install --global Curiosity.CLI"
require_api_token
require_workspace_reachable

OUT="$ROOT_DIR/.export"
rm -rf "$OUT"

step "Exporting from $CURIOSITY_URL into .export/"
curiosity-cli export-workspace-definitions -s "$CURIOSITY_URL" -t "$CURIOSITY_API_TOKEN" --export-path "$OUT" -u

step "Compare"
info "diff -r .export/code src/QuranKG.Definitions/code"
info "diff -r .export/config src/QuranKG.Definitions/config"
