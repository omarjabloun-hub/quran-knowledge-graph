#!/usr/bin/env bash
# Everything, from an empty machine to the dashboard, on the LOCAL workspace:
#   start the workspace → mint tokens → fetch and check the data → ingest → definitions →
#   front end → warm → status. Each step is idempotent, so it is safe to re-run after a failure.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
. "$SCRIPT_DIR/lib.sh"

[ -f "$ROOT_DIR/.env" ] || cp "$ROOT_DIR/.env.example" "$ROOT_DIR/.env"

bash "$SCRIPT_DIR/check-prereqs.sh"
bash "$SCRIPT_DIR/init-workspace.sh"

load_env
step "Minting tokens"
workspace tokens
load_env

step "Fetching the data"
dotnet run --project "$TOOLS_CSPROJ" -c Release -- fetch
step "Checking the corpus"
dotnet run --project "$TOOLS_CSPROJ" -c Release -- check

bash "$SCRIPT_DIR/ingest.sh"
bash "$SCRIPT_DIR/deploy.sh"

step "Status"
workspace status

step "Ready"
info "Open ${CURIOSITY_URL:-http://localhost:8080/} and sign in as ${WORKSPACE_ADMIN_USER:-admin}."
info "The verses' Arabic is being embedded in the background (Verse.TextSimple above); until it"
info "finishes, 'Similar verses' ranks by shared lemmas and roots only."
