#!/usr/bin/env bash
# Builds the front end (C# -> JavaScript with Transpose) and uploads it. Touches nothing else.
#
# Usage: scripts/upload-front-end.sh [--target local|prod]
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

load_env
parse_target "$@"
resolve_target
require_command dotnet        "Install the .NET 10 SDK."
require_command tps           "dotnet tool install --global Transpose.Compiler"
require_command curiosity-cli "dotnet tool install --global Curiosity.CLI"
require_upload_token
require_workspace_reachable

step "Building the front end"
dotnet build "$FRONTEND_CSPROJ" -c Release

step "Uploading to $CURIOSITY_URL  [target: $TARGET]"
curiosity-cli upload-front-end -s "$CURIOSITY_URL" -t "$INTERFACE_UPLOAD_TOKEN" -p "$FRONTEND_OUTPUT"

step "Done"
info "Reload the workspace in the browser (a hard reload if the old bundle is cached)."
