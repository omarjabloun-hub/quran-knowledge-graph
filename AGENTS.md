# AGENTS.md

Orientation for anyone — person or agent — working in this repository. The working agreement
(non-negotiables, order of a change, gates) is [CLAUDE.md](CLAUDE.md).

## What is where

| Path | What | Talks to the workspace? |
|---|---|---|
| `data/manifest.json` | every source: URL, target under `data/raw/`, SHA-256, licence | — |
| `src/QuranKG.Dataset` | reads, cleans and joins the sources into a validated corpus (`CorpusLoader.Load`) | no |
| `src/QuranKG.Dataset.Tests` | the cleaning rules and the corpus counts (xUnit v3, Microsoft.Testing.Platform) | no |
| `src/QuranKG.Tools` | `fetch`, `check`, `sync-dto` | no |
| `src/QuranKG.Connector` | the graph schema (`Schema.cs`, `Edges.cs`), the ingest (`Ingest.cs`), `--dry-run`, `--emit-schema` | yes (Library token) |
| `src/QuranKG.Definitions` | the workspace as files — `config/` (indexes, styles, settings, schemas), `code/endpoints/qkg/`, `variants/` | imported |
| `src/QuranKG.FrontEnd` | the dashboard (`Dashboard/`), renderers (`Renderers/`), shared components, the API layer | uploaded |
| `src/QuranKG.Workspace` | `tokens`, `status`, `call`, `warm`, `stage-definitions`, `use-embeddings` | yes (admin sign-in) |
| `tools/embedding-benchmark` | Python: embeds the verses with candidate models and scores them on QurSim | no |
| `scripts/` | every task as `.sh` (bash 3.2) and `.ps1`, called by the `Justfile` | some |
| `docs/` | use case, data sources, graph model, models, local setup | — |
| `.claude/skills/` | `quran-data` (this corpus) and the `curiosity-*` platform skills | — |

## Commands

`just` lists them all. The ones used daily:

```bash
just bootstrap              # from nothing to the dashboard, on the local workspace
just up | down | status     # the local workspace
just fetch && just check-data
just ingest [local|prod] [--dry-run]
just deploy [local|prod]    # definitions + front end + warm
just upload-endpoints | upload-frontend
just call <endpoint> ['{"Uid":"…"}']
just emit-schema | sync-dto
just embeddings-ollama | embeddings-harrier
just benchmark [--only <regex>] [--skip-hf]
just test
```

## Toolchain

.NET 10 SDK (`global.json`, roll-forward), `Transpose.Compiler` (`tps`) for the front end,
`Curiosity.CLI` for uploads, Docker for the workspace (`curiosityai/curiosity`, pinned build in
`.env`), Ollama for local models, Python 3.11+ for the benchmark only. Package versions are pinned
in the csproj files: Curiosity.Library 26.10.6546, Curiosity.FrontEnd* 26.10.6571, Tesserae
2026.10.71964 (the version the Curiosity packages were built against), Transpose 26.9.

## Endpoints

| Path | Returns |
|---|---|
| `qkg/dashboard/corpus` | everything the dashboard draws, from one pass over the words (~1.7 s, cached an hour) |
| `qkg/surah/profile` | one surah: verse texts and lengths in order, parts of speech, top / distinctive roots, lemmas |
| `qkg/root/profile` | one root: per-surah spread, Meccan / Medinan, lemmas, forms, verses |
| `qkg/verse/words` | one verse: its words with morphology, roots, lemmas, and its QurSim pairs (**people only**) |
| `qkg/verse/similar` | similar verses: Arabic embedding + shared lemmas + shared roots, with each signal's contribution |

All read-only, `AllUsers`, sharing the DTO library `qkg/_lib/dto` (admin-only).
