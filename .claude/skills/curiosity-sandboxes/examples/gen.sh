#!/bin/bash
# Reference template — tools/sandbox/gen.sh: regenerates the application's sandbox AI tool.
# Re-run after N.cs or the endpoint set changes, and commit the generated
# src/<App>.Definitions/code/chat-ai-tools/<UID>.cs with it.
#
# Layout it expects (copy the two other files of this folder there):
#   tools/gen_sandbox.py                 the generator
#   tools/sandbox/sandbox-template.cs    the template it fills
#   tools/sandbox/gen.sh                 this file
cd "$(dirname "$0")/../.."

# The sandbox's /docs/use-case.md is the brief WITHOUT its ground truth and hero scenarios: they
# name the planted findings, and the assistant must never read the answer key it is scored
# against. Adjust the two patterns to the headings of YOUR docs/use-case.md, and check the copy.
USECASE=$(mktemp)
trap 'rm -f "$USECASE"' EXIT
python3 - docs/use-case.md "$USECASE" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read()
text = re.sub(r"\n### [0-9.]* *Ground truth\n.*?(?=\n---\n|\n## )", "\n", text, flags=re.S)
text = re.sub(r"\n## [^\n]*[Hh]ero scenario.*?(?=\n## |\Z)", "\n", text, flags=re.S)
assert "expected_findings" not in text, "the use-case copy still describes the answer key"
open(sys.argv[2], "w", encoding="utf-8").write(text)
PY

# --uid       a readable UID that round-trips (UID128.Parse(s).ToString() == s), e.g. SBXapp111…
# --func      what the model calls it: lower-case letters, digits, _ and -
# --prefix    this application's endpoint folder; `app <endpoint>` is relative to it
# --exclude-types / --exclude-endpoints   the ground-truth types and the evaluation endpoint
python3 tools/gen_sandbox.py \
  --template tools/sandbox/sandbox-template.cs \
  --ncs src/App.FrontEnd/src/Schema/N.cs \
  --defs src/App.Definitions \
  --uid "<sandbox uid>" --cls AppSandbox --func app-sandbox \
  --display "App Sandbox" \
  --desc "The application's corpus as a filesystem - cases, changes, manuals and their reference data - with bash, jq, awk and python over it, plus search, consult, a read-only graph query engine and the application's own endpoints. Use it for open-ended questions that need counting, joining or comparing many records; read /docs/README.md first." \
  --prefix app/ \
  --exclude-types CaseFinding \
  --exclude-endpoints evaluation/scorecard \
  --usecase "$USECASE" \
  --readme-intro "<One sentence: what this application is for, in the user's words.>"
