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
| `src/QuranKG.FrontEnd` | the dashboard (`Dashboard/`), the Quran View (`Reader/`), the Stories and themes page and its map (`Themes/`), renderers (`Renderers/`), the reader and shared components (`Components/`), the API layer | uploaded |
| `src/QuranKG.Workspace` | `tokens`, `status`, `call`, `warm`, `clusters`, `stage-definitions`, `use-embeddings` | yes (admin sign-in) |
| `tools/embedding-benchmark` | Python: embeds the verses with candidate models and scores them on QurSim | no |
| `tools/embedding-server` | Python: AraModernBert-STS behind the OpenAI embeddings API — what the workspace embeds the verses' Arabic with | called by it |
| `tools/cluster-benchmark` | Python + numpy: the reference implementation of the clusters build, scored on the ruku' and QurSim | no |
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
just clusters [local|prod]  # passages and themes, built inside the workspace
just upload-endpoints | upload-frontend
just call <endpoint> ['{"Uid":"…"}']
just emit-schema | sync-dto
just embedding-server [start|stop|status]
just embeddings-arabic | embeddings-ollama | embeddings-harrier
just benchmark [--only <regex>] [--skip-hf]
just benchmark-clusters [--split dev|test] [--from build.json]
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
| `qkg/verse/tafsir` | one verse: every tafsir work, and the chosen one's text in Arabic and English (**people only**) |
| `qkg/verse/similar` | similar verses: Arabic embedding + shared lemmas + shared roots, with each signal's contribution |

All read-only, `AllUsers`, sharing the DTO library `qkg/_lib/dto` (admin-only).

## The AI layer

The **AI Assistant** page (`src/QuranKG.FrontEnd/src/Chat/QuranChatView.cs`) is the Quran Research
Assistant with its four tools pinned on every turn — the stock page starts each chat with only
consult ticked.

| Definition (`src/QuranKG.Definitions/code/`) | What |
|---|---|
| `ai-assistants/quran-research-assistant-…1111.cs` | the assistant: the method for testing a claim (fix the counting rule, count both sides, list the verses, measure the baseline), one worked example |
| `chat-ai-tools/QuranTooLs111111111111.cs` | CountTerm (lemma / root / form / text, with every verse and how many lemmas share the count), FindVerses, ReadVerses, SameCount, SimilarVerses, SimilarSurahs |
| `chat-ai-tools/QuranFiLes111111111111.cs` | the sandbox `quran`: words / verses / surahs / lemmas / roots as TSV, bash and python, `app` for the read-only endpoints |
| `chat-ai-tools/SEARCHRecords111111111.cs` | the workspace search, fenced to Verse, Surah, Root, Lemma, Cluster, Juz |
| `chat-ai-tools/ConsuLt111111111111111.cs` | the consult readers for those types and Word |

None of them reads `QurSimPair`, `Topic`, `Tafsir`, `TafsirEntry`, `Verse.Ruku`, `Surah.RukuCount` or calls
`qkg/verse/words` / `qkg/verse/tafsir`
(CLAUDE.md, non-negotiable 2). Check a tool after an import:
`GET api/chatai/tools/get/function-names?toolUID=<uid>` is not `[]`, and
`POST api/chatai/tools/test?toolUID=<uid>&functionName=<f>` with the arguments as a JSON array of
JSON values (`["\"آدم\"", "\"lemma\"", "0", "5"]`).
