# Running it on a Mac (Apple silicon, 64 GB)

Everything runs locally: the Curiosity workspace in Docker, the models in Ollama on the Mac's GPU,
and the tooling (.NET) on the Mac itself. Plan on ~15 minutes for the first `just bootstrap`,
plus model downloads.

```
                ┌──────────── your Mac ────────────────────────────────────────────┐
                │                                                                  │
 browser ──────►│  Docker Desktop                        Ollama (native, Metal GPU) │
 :8080          │  ┌─────────────────────────────┐       ┌──────────────────────┐   │
                │  │ curiosityai/curiosity:72111 │ ────► │ qwen3-embedding:8b   │   │
                │  │ graph · search · endpoints  │ :11434│ qwen3:32b            │   │
                │  │ front end · assistant       │       └──────────────────────┘   │
                │  └─────────────▲───────────────┘   host.docker.internal:11434     │
                │                │ HTTP (tokens in .env)                            │
                │  just / scripts / .NET 10: fetch → ingest → definitions → front end │
                └──────────────────────────────────────────────────────────────────┘
```

**Why Ollama outside Docker.** Docker on macOS runs a Linux VM with no access to the GPU; a model
in a container would run on the CPU. Ollama runs natively on Metal, and the workspace reaches it
at `host.docker.internal:11434`.

## 1. Install the tools (once)

```bash
# Homebrew: https://brew.sh
brew install --cask docker          # Docker Desktop — start it once, accept the prompts
brew install --cask dotnet-sdk      # .NET 10 SDK
brew install just git
brew install ollama                 # or the app from https://ollama.com/download

dotnet tool install --global Transpose.Compiler   # `tps`, compiles the front end (C# → JS)
dotnet tool install --global Curiosity.CLI        # `curiosity-cli`, uploads to the workspace
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zshrc && source ~/.zshrc
```

Docker Desktop → Settings → Resources: give the VM **16 GB of memory** and **6+ CPUs**. The graph
holds ~100,000 nodes and ~430,000 edges; the workspace is comfortable at 8–10 GB. The models do
not count against this — they live in Ollama, outside the VM.

The image is multi-arch: on Apple silicon it runs natively (arm64), no Rosetta.

## 2. Bootstrap

```bash
git clone https://github.com/omarjabloun-hub/quran-knowledge-graph.git
cd quran-knowledge-graph
just setup          # creates .env from .env.example, checks the tools
just bootstrap      # everything below, in order
```

`just bootstrap` runs, each step idempotent (re-run it after a failure):

| Step | What happens | Time |
|---|---|---|
| `just up` | starts the workspace (the first run pulls the image: ~1.9 GB on Apple silicon) and waits for it | 2–5 min |
| `just tokens` | signs in as the admin in `.env`, mints a Library and an Interface Upload token into `.env` | seconds |
| `just fetch` | downloads the 8 sources in `data/manifest.json` into `data/raw/`, checks their SHA-256 | 1 min |
| `just check-data` | loads, joins and validates the corpus — 114 surahs, 6,236 verses, 77,429 words | 5 s |
| `just ingest` | writes the schema and ~97,500 nodes / ~428,400 edges | 1–2 min |
| `just deploy` | imports indexes, styles and endpoints; builds and uploads the front end | 1 min |
| `just clusters` | cuts every surah into passages and groups them into themes, inside the workspace | seconds |

Then open <http://localhost:8080>, sign in with `WORKSPACE_ADMIN_USER` / `WORKSPACE_ADMIN_PASSWORD`
(admin / admin unless you changed `.env` **before** the first `just up`). The first sign-in opens a
setup assistant — *Finish later* skips it. The home page is the pattern dashboard.

Where to look:

| | |
|---|---|
| **Read the Quran** (sidebar) | the Quran View: a surah as continuous text in its passages; click a verse for its translations, words, passage, theme and related verses. `#/read/2/255` opens on a verse |
| **Stories and themes** (sidebar) | the 119 themes — passages of several surahs that tell the same story or treat the same subject — widest first, with a filter — under a map of every surah in its passages, coloured by theme |
| a surah → **Passages** | its sections in order, each with the theme it shares; a strip shows the whole surah at a glance |
| a surah → **Similar surahs** | the surahs that lean on the same distinctive words, and the themes both tell |
| a theme or a passage | **Read**: the theme's passages one after another, or the passage, as text with the words that tie them marked; then its key words and roots |

None of these needs a model: they read the Arabic morphology (lemmas and roots), so they are there
as soon as `just bootstrap` ends. Re-run `just clusters` after a new ingest.

In the background the workspace embeds every verse (two embeddings indexes, ~6,200 verses each).
`just status` shows how far it has got. Until `Verse.TextSimple` is done, *Similar verses* ranks by
shared lemmas and roots only, and says so on the page.

## 3. Local models

```bash
ollama serve &                      # the app starts this on its own
just models-pull                    # qwen3-embedding:8b (4.7 GB) and qwen3:32b (~20 GB)
```

### Embed the verses with AraModernBert-STS instead of the built-in model

```bash
just embedding-server start         # the model on the Mac's GPU (Python venv + 600 MB, the first time)
just embeddings-arabic              # deletes the Harrier index, creates the external one
just status                         # watch Verse.TextSimple fill up again (under two minutes)
```

AraModernBert-STS is the best of 24 embeddings measured on Quranic Arabic
([`tools/embedding-benchmark`](../tools/embedding-benchmark/README.md)). This writes
`EMBEDDING_PROVIDER=arabic` into `.env`, so later `just deploy` keeps it, and `just up` starts the
server with the workspace — it must be running for the workspace to embed or search. Ollama's
`qwen3-embedding:8b` is the alternative (`just embeddings-ollama`, and `W_SEM = 0.5` in
`qkg/verse/similar`); back to the built-in: `just embeddings-harrier`. Only one embeddings index
can exist on a field — the endpoints use the first one they find — which is why the switch deletes
before it creates.

### A chat model for the assistant and for naming clusters

The Quran Research Assistant (the **AI Assistant** page) counts, reads and tests claims through
tool calls, so it needs a model that calls tools reliably. Two ways to give it one:

**Claude, through a local OpenAI-compatible wrapper** (what this machine uses: a container
exposing the Claude Code CLI as `/v1/chat/completions` on port 8000). In the workspace:
**Manage → AI → LLM Providers → OpenAI → Add another OpenAI**:

| Field | Value |
|---|---|
| Custom Open AI Host | `http://host.docker.internal:8000/v1/chat/completions` |
| API key | the wrapper's shared key (`CLAUDE_API_KEY` in the wrapper's own `.env`, never in this repository) |
| Model | `sonnet` (or `opus` for longer investigations) |

then **Test connection**, and make it the default under **Manage → AI → Defaults**. The provider
lives in the workspace, not in `src/QuranKG.Definitions`: it carries a secret and names this
machine. Bind the wrapper's port to `127.0.0.1` unless something outside the Mac must reach it.

**Or a local model through Ollama**: **Manage → AI → Providers → add → Ollama**:

| Field | Value |
|---|---|
| Host | `http://host.docker.internal:11434` (not `localhost`: that is the container itself) |
| Model | `qwen3:32b` (or another from [models.md](models.md)) |
| Max input context | `32768` |
| Max output context | `4096` |

The defaults (8,192 / 1,024) are too small for verses with their context. Details and
alternatives in [models.md](models.md).

## 4. Day to day

```bash
just up / just down          # start / stop the workspace (data stays in .workspace/)
just status                  # counts, compile state, embedding progress
just deploy                  # after changing src/QuranKG.Definitions or src/QuranKG.FrontEnd
just clusters                # rebuild the passages and themes (after an ingest, or a change to the build)
just upload-frontend         # front end only (~15 s)
just ingest                  # after changing the connector
just call qkg/dashboard/corpus                      # an endpoint's JSON
just call qkg/verse/similar '{"Uid":"<verse uid>","Count":10}'
just export-definitions      # after changing anything in the admin UI → diff .export/ and commit
```

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `just tokens` says it cannot sign in | `.env`'s admin user/password only apply to a **fresh** `.workspace/` folder. Use the password you set, or `just down`, delete `.workspace/local`, `just up`. |
| A script says "no Library token" | Run `just tokens`. Session tokens do not survive a workspace restart; the minted tokens do. |
| An endpoint still answers the old way | Answers are cached for an hour, keyed on each endpoint's own code. After changing only `code/endpoints/qkg/_lib/dto.cs`, restart the workspace (`just down && just up`). |
| The front end looks unchanged | Hard-reload the browser (⌘⇧R): `index.html` carries a content hash. |
| *Similar verses* says "lexical only" | The embeddings index is still filling up: `just status`. With Ollama, check it answers: `curl localhost:11434/api/tags`. |
| The external `Verse.TextSimple` index stays at 0 | `just embedding-server status` — start it if it is down; its log is `tools/embedding-server/server.log`. |
| The Ollama index stays at 0 | The container cannot reach Ollama. On a Mac, Docker Desktop forwards `host.docker.internal` to the Mac's own `127.0.0.1`, where Ollama listens by default. On Linux the bridge does not reach loopback: start Ollama with `OLLAMA_HOST=0.0.0.0`. Check `OLLAMA_URL` in `.env` and re-run `just embeddings-ollama`. |
| `MSB3073 … tps: not found` | `dotnet tool install --global Transpose.Compiler` and put `~/.dotnet/tools` on `PATH`. |
| The container restarts in a loop on macOS | Keep `MSK_DISABLE_RESOURCE_MONITORING=true` (docker-compose.yml sets it). |
| Everything is slow | Do not set `MSK_LOW_MEMORY`: it made the dashboard endpoint six times slower. Give Docker 16 GB. |
