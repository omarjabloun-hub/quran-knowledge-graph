#!/usr/bin/env bash
# Verifies the tools the repository needs, and says how to install what is missing.
set -euo pipefail
# shellcheck disable=SC1091
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
PATH="$PATH:$HOME/.dotnet/tools"

missing=0

check() { # name  install-hint  required(yes|no)
  if command -v "$1" >/dev/null 2>&1; then
    printf '  \033[32m✓\033[0m %-15s %s\n' "$1" "$($1 --version 2>/dev/null | head -1)"
  elif [ "$3" = "yes" ]; then
    printf '  \033[31m✗\033[0m %-15s MISSING — %s\n' "$1" "$2"
    missing=$((missing + 1))
  else
    printf '  \033[33m·\033[0m %-15s optional — %s\n' "$1" "$2"
  fi
}

step "Prerequisites"
check dotnet        "brew install --cask dotnet-sdk   (.NET 10 SDK, https://dotnet.microsoft.com/download)" yes
check tps           "dotnet tool install --global Transpose.Compiler"                                   yes
check curiosity-cli "dotnet tool install --global Curiosity.CLI"                                        yes
check docker        "install Docker Desktop and start it"                                               yes
check curl          "install curl"                                                                      yes
check just          "brew install just"                                                                 no
check ollama        "brew install ollama — only for local models (docs/models.md)"                      no
check python3       "only for the embedding benchmark (tools/embedding-benchmark)"                      no

if command -v dotnet >/dev/null 2>&1 && ! dotnet --list-sdks | grep -qE '^(1[0-9])\.'; then
  printf '  \033[31m✗\033[0m .NET 10 SDK     MISSING — found only: %s\n' "$(dotnet --list-sdks | tr '\n' ' ')"
  missing=$((missing + 1))
fi

step "Configuration"
if [ -f "$ROOT_DIR/.env" ]; then
  printf '  \033[32m✓\033[0m .env present\n'
else
  printf '  \033[33m·\033[0m .env missing — run: just setup\n'
fi

if [ "$missing" -gt 0 ]; then
  printf '\n\033[31m%d required tool(s) missing.\033[0m\n' "$missing"
  exit 1
fi
printf '\n\033[32mAll required tools present.\033[0m\n'
