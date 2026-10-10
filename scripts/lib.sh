#!/usr/bin/env bash
# Shared helpers. Sourced by the other scripts in this folder, never run directly.
# Written for bash 3.2 so it works with the bash macOS ships.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
info() { printf '    %s\n' "$1"; }
warn() { printf '\033[33m    ! %s\033[0m\n' "$1" >&2; }
fail() { printf '\033[31mERROR: %s\033[0m\n' "$1" >&2; exit 1; }

load_env() {
  if [ -f "$ROOT_DIR/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    . "$ROOT_DIR/.env"
    set +a
  else
    warn "No .env at $ROOT_DIR/.env — falling back to defaults. Run 'just setup' to create one."
  fi
  # The dotnet tools land here; a fresh shell may not have it on PATH yet.
  PATH="$PATH:$HOME/.dotnet/tools"
  export PATH
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1. ${2:-}"
}

# Reads --target <local|prod> out of the arguments into TARGET; the rest is the caller's.
parse_target() {
  TARGET="local"
  local expect=0
  for arg in "$@"; do
    if [ "$expect" = "1" ]; then TARGET="$arg"; expect=0; continue; fi
    case "$arg" in
      --target)   expect=1 ;;
      --target=*) TARGET="${arg#*=}" ;;
    esac
  done
}

# Points CURIOSITY_URL and the two tokens at the chosen workspace. The bare variables in .env
# are the local workspace, the PROD_ ones production; nothing falls back from one to the other,
# so forgetting a PROD_ value is an error rather than a deploy to localhost.
resolve_target() {
  case "${TARGET:-local}" in
    local)
      CURIOSITY_URL="${CURIOSITY_URL:-http://localhost:8080/}"
      ;;
    prod)
      CURIOSITY_URL="${PROD_CURIOSITY_URL:-}"
      CURIOSITY_API_TOKEN="${PROD_CURIOSITY_API_TOKEN:-}"
      INTERFACE_UPLOAD_TOKEN="${PROD_INTERFACE_UPLOAD_TOKEN:-}"
      [ -n "$CURIOSITY_URL" ] || fail "--target prod, but PROD_CURIOSITY_URL is not set in .env"
      ;;
    *)
      fail "Unknown --target '${TARGET}' (expected: local | prod)"
      ;;
  esac
  export CURIOSITY_URL CURIOSITY_API_TOKEN INTERFACE_UPLOAD_TOKEN
}

require_api_token() {
  case "${CURIOSITY_API_TOKEN:-}" in
    ""|replace-with-*) fail "No Library token for target '${TARGET}'. Run 'just tokens' (or set $( [ "$TARGET" = prod ] && echo PROD_CURIOSITY_API_TOKEN || echo CURIOSITY_API_TOKEN ) in .env)." ;;
  esac
}

require_upload_token() {
  case "${INTERFACE_UPLOAD_TOKEN:-}" in
    ""|replace-with-*) fail "No Interface Upload token for target '${TARGET}'. Run 'just tokens' (or set $( [ "$TARGET" = prod ] && echo PROD_INTERFACE_UPLOAD_TOKEN || echo INTERFACE_UPLOAD_TOKEN ) in .env)." ;;
  esac
}

# Connectivity only: any HTTP answer proves the workspace is up (a fresh one answers 401).
require_workspace_reachable() {
  require_command curl "Install curl and ensure it is on PATH."
  curl -sS -o /dev/null "$CURIOSITY_URL" \
    || fail "Workspace not reachable at $CURIOSITY_URL. For the local one, run 'just up' first."
}

# The .NET operations on a running workspace (src/QuranKG.Workspace): tokens, status, call, …
workspace() {
  dotnet run --project "$ROOT_DIR/src/QuranKG.Workspace" -c Release -- "$@"
}

CONNECTOR_CSPROJ="$ROOT_DIR/src/QuranKG.Connector/QuranKG.Connector.csproj"
TOOLS_CSPROJ="$ROOT_DIR/src/QuranKG.Tools/QuranKG.Tools.csproj"
FRONTEND_CSPROJ="$ROOT_DIR/src/QuranKG.FrontEnd/QuranKG.FrontEnd.csproj"
FRONTEND_OUTPUT="$ROOT_DIR/src/QuranKG.FrontEnd/bin/Release/netstandard2.0/tps"
