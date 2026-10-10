# QuranKG.Definitions — the workspace as source

Everything configured in the Curiosity workspace, as files. `just upload-endpoints` stages
`code/` and `config/` (with the embeddings file `EMBEDDING_PROVIDER` names) and imports them with
`curiosity-cli import-workspace-definitions`; the import is idempotent. **Ingest first**
(`just ingest`): the workspace injects `N`/`E` constants generated from the graph schema above
every endpoint, so an endpoint naming a type the connector has not created does not compile.

```
config/
├── general.json              IsPublicNodeType: every node type of this app (else ordinary users see nothing)
├── interface.json            product features this app does not use are switched off
├── search-node-types.json    Word and QurSimPair left out of search by default; Surah/Juz/Revelation/Root/Topic as facets
├── schemas/                  generated from the connector (re-export after a schema change); styles/ = icon, colour, label
└── indexes/others/           26 indexes — see below
code/endpoints/qkg/
├── _lib/dto.cs               shared DTOs (AdminOnly library) — `just sync-dto` generates src/QuranKG.FrontEnd/src/Api/Dto.cs
├── dashboard/corpus.cs       the pattern dashboard: totals, per-surah stats, root heat maps, Meccan vs Medinan
├── root/profile.cs           one root: spread over surahs, lemmas, forms, verses
├── surah/profile.cs          one surah: verse lengths, top and DISTINCTIVE roots, lemmas
├── verse/words.cs            one verse: words in order, roots, lemmas, Ibn Kathir's related verses
└── verse/similar.cs          similar verses, Arabic first: embedding + shared lemmas + shared roots
variants/embeddings-ollama/   the optional local-model embeddings index (`just embeddings-ollama`)
```

## Indexes — Arabic first

| Kind | On | Why |
|---|---|---|
| Full text, diacritics-insensitive (`NormalizeDiacritics`) | `Verse.TextSimple` (boost 2), `Surah.NameArabic`, `Root.RootLetters`, `Lemma.LemmaText`/`LemmaPlain`, `Word.TextPlain`, `Topic.TopicNameArabic`, `Cluster.LabelArabic` | type Arabic with or without tashkeel and hamza forms and still match |
| Full text, English | `Verse.EnglishSaheeh`, `Verse.EnglishQarai`, `Surah.NameEnglish`/`Introduction`, `Topic.TopicName`, `Cluster.Label`/`Description` | a way in for readers who search in English |
| Full text, Latin | `Verse.Transliteration`, `Surah.NameLatin`/`NameScholarly` | |
| Embeddings (Harrier, built in) | `Verse.TextSimple` — **the Arabic**; `Verse.EnglishSaheeh`; `Surah.Introduction` (chunked) | semantic search and the similarity signal. `MinimumLength` is 1: the default (20 characters) would skip short verses like الم |
| Facets | `Word.PartOfSpeech`, `Word.PosTag`, `Lemma.PartOfSpeech`, `Verse.SajdaType` | narrowing lists of one type |

Embeddings read `TextSimple` — Tanzil's Simple-Clean text (standard spelling, no diacritics) —
because models are trained on undiacritised modern spelling: the Uthmani text with its marks
costs ~4× the tokens and changes 17 % of the word spellings. Uthmani stays for display.

### Switching the Arabic embeddings to a local model

The built-in Harrier model works with no setup. To use a stronger model served by Ollama on
your machine (see `docs/models.md` for which one), run `just embeddings-ollama`: it deletes the
Harrier `Verse.TextSimple` index and imports `variants/embeddings-ollama/` with the model named
in `.env` (`OLLAMA_EMBEDDING_MODEL`). Only one embeddings index may exist on a field —
`Graph.Embeddings.GetIndex(type, field)` returns the first one it finds — so the switch replaces
rather than adds, and `EMBEDDING_PROVIDER=ollama` in `.env` makes later uploads keep it that way.

## Endpoints

All `ReadOnly`, `AllUsers`, cached for an hour (`CacheFor(3600)`; an import clears the cache).
Every metric is counted live from the graph; every count ships the UID of what it counted.
Call one from the shell: `just call qkg/dashboard/corpus`, or with a body:
`just call qkg/verse/similar '{"Uid":"…","Count":10}'`.

The answers are cached **per user**, keyed on each endpoint's own code: a change to
`_lib/dto.cs` alone leaves its dependents serving the old answer for up to an hour — restart the
workspace. After changing a DTO, `just sync-dto` regenerates the front end's copy.

**QurSim is the answer key.** `verse/words` returns Ibn Kathir's related verses for people to
read. No similarity signal reads it, and no AI tool may call that endpoint or expose those
nodes — otherwise every similarity score becomes a tautology.

## What the import does NOT carry (manual steps)

- **An AI provider** for chat and for naming clusters: Admin → AI → Providers. Its key is a
  secret per environment. See `docs/models.md` (a local Ollama LLM or a hosted model).
- **Tokens**: `just tokens` mints the two the scripts need and writes them into `.env`.

## Round trip

After changing anything in the admin UI: `just export-definitions` (writes `.export/`), diff
against this folder, copy what you mean to keep, commit. Never edit `config/schemas/` by hand —
change the connector, re-ingest, re-export.
