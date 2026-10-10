# Quran Knowledge Graph — every task runs through `just`. `just` on its own lists them.
#
# Each recipe calls a script in scripts/ that exists as .sh and .ps1; the two lines below pick
# the right one, so the same command works on macOS, Linux and Windows. Recipes that take
# `target` act on the local workspace by default: `just deploy prod` uses the PROD_ values.

set dotenv-load := true

run := if os_family() == "windows" { "powershell -NoProfile -ExecutionPolicy Bypass -File scripts/" } else { "bash scripts/" }
ext := if os_family() == "windows" { ".ps1" } else { ".sh" }

tools     := "dotnet run --project src/QuranKG.Tools -c Release --"
workspace := "dotnet run --project src/QuranKG.Workspace -c Release --"

# List every recipe.
default:
    @just --list --unsorted

# ── Start here ────────────────────────────────────────────────────────────────────────────────

# Create .env from the template (never overwrites) and check the prerequisites.
setup:
    @{{run}}setup{{ext}}

# Everything on the local workspace: start it, tokens, fetch, ingest, definitions, front end.
bootstrap:
    @{{run}}bootstrap{{ext}}

# Check that every required tool is installed.
check:
    @{{run}}check-prereqs{{ext}}

# Build every project (the front end needs `tps`).
build:
    dotnet build QuranKG.slnx -c Release

# Run the corpus tests (cleaning rules, joins, counts).
test:
    dotnet test --project src/QuranKG.Dataset.Tests/QuranKG.Dataset.Tests.csproj

# ── The local workspace (Docker) ─────────────────────────────────────────────────────────────

# Start the local workspace and wait until it answers.
up:
    @{{run}}init-workspace{{ext}}

# Stop it (its data stays in .workspace/).
down:
    docker compose down

# Follow its log.
logs:
    docker compose logs -f workspace

# Sign in as the admin in .env and write fresh Library + Interface Upload tokens into .env.
tokens target="local":
    {{workspace}} tokens --target {{target}}

# Node counts, compile status and how far the embeddings have got.
status target="local":
    {{workspace}} status --target {{target}}

# ── Data ──────────────────────────────────────────────────────────────────────────────────────

# Download the sources listed in data/manifest.json into data/raw/ (checksums verified).
fetch:
    {{tools}} fetch

# Load, join and validate the corpus without a workspace; print the counts.
check-data:
    {{tools}} check

# ── Graph ─────────────────────────────────────────────────────────────────────────────────────

# Write schema, nodes and edges (idempotent). `just ingest local --dry-run` writes nothing.
ingest target="local" *flags="":
    @{{run}}ingest{{ext}} --target {{target}} {{flags}}

# Regenerate the front end's N/E constants from the connector's schema.
emit-schema:
    dotnet run --project src/QuranKG.Connector -c Release -- --emit-schema src/QuranKG.FrontEnd/src/Schema/N.cs

# Regenerate the front end's copy of the endpoint DTOs from code/endpoints/qkg/_lib/dto.cs.
sync-dto:
    {{tools}} sync-dto

# ── Deploy ────────────────────────────────────────────────────────────────────────────────────

# Indexes, styles, search settings and endpoints (src/QuranKG.Definitions).
upload-endpoints target="local":
    @{{run}}upload-endpoints{{ext}} --target {{target}}

# Build and upload the front end.
upload-frontend target="local":
    @{{run}}upload-front-end{{ext}} --target {{target}}

# Definitions + front end, then warm the dashboard.
deploy target="local":
    @{{run}}deploy{{ext}} --target {{target}}

# Export the live definitions into .export/ to diff against src/QuranKG.Definitions/.
export-definitions target="local":
    @{{run}}export-definitions{{ext}} --target {{target}}

# Run an endpoint and print its JSON: just call qkg/dashboard/corpus  (or with a body: '{"Uid":"…"}')
call path body="{}" target="local":
    {{workspace}} call {{path}} '{{body}}' --target {{target}}

# ── Models (docs/models.md) ───────────────────────────────────────────────────────────────────

# Embed the verses' Arabic with OLLAMA_EMBEDDING_MODEL on this Mac's GPU instead of Harrier.
embeddings-ollama target="local":
    @{{run}}embeddings{{ext}} ollama --target {{target}}

# Back to the workspace's built-in Harrier model.
embeddings-harrier target="local":
    @{{run}}embeddings{{ext}} harrier --target {{target}}

# Pull the recommended local models into Ollama (embedding + chat; ~25 GB).
models-pull:
    ollama pull ${OLLAMA_EMBEDDING_MODEL:-qwen3-embedding:8b}
    ollama pull ${OLLAMA_CHAT_MODEL:-qwen3:32b}

# Compare embedding models on QurSim (Python; downloads models): see tools/embedding-benchmark.
benchmark *args="":
    @{{run}}benchmark{{ext}} {{args}}
