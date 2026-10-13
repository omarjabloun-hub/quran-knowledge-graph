#!/usr/bin/env bash
# Imports the workspace definitions — indexes, node styles, search settings, endpoints — from
# src/QuranKG.Definitions. The import is idempotent. The embeddings index on the verses' Arabic
# is the one EMBEDDING_PROVIDER names (harrier | ollama | arabic): see `just embeddings-arabic`.
#
# Ingest first: an endpoint naming a node type the graph does not have yet does not compile.
#
# Usage: scripts/upload-endpoints.sh [--target local|prod]
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

load_env
parse_target "$@"
resolve_target
require_command dotnet        "Install the .NET 10 SDK."
require_command curiosity-cli "dotnet tool install --global Curiosity.CLI"
require_api_token
require_workspace_reachable

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

step "Staging the definitions (embeddings: ${EMBEDDING_PROVIDER:-harrier})"
workspace stage-definitions "$STAGE/defs"

step "Importing into $CURIOSITY_URL  [target: $TARGET]"
curiosity-cli import-workspace-definitions -s "$CURIOSITY_URL" -t "$CURIOSITY_API_TOKEN" --import-path "$STAGE/defs" -z

step "Done"
info "Endpoint responses are cached for an hour, keyed on each endpoint's own code: after changing"
info "only code/endpoints/qkg/_lib/dto.cs, restart the workspace (just down && just up) to see it."
