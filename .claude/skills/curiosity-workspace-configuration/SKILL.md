---
name: curiosity-workspace-configuration
description: How to treat a Curiosity workspace as version-controlled source — the definitions export/import cycle and the round-trip, the layout of a definitions folder, what each config file controls (general, chat-ai, search, search-node-types, llm-search, pipeline-mapping, interface, indexes, schemas, file indexing, chat policy) and the settings 26.10 rejects, node visibility and access, and the ordering rules that make an import succeed. Use when setting up a workspace, promoting configuration between environments, or when a setting must be reproducible.
---

# Workspace configuration — the workspace as source

A Curiosity workspace is configurable enough that "someone clicked it together" is a real
failure mode: nothing is reproducible, nothing is reviewable, and a fresh environment is a
week of archaeology. The fix is to keep the **whole workspace in the repository** and treat
the admin UI as an editor of files you own.

---

## 1. The cycle

```
   admin UI  ──export definitions──▶  a zip  ──unpack──▶  src/<App>.Definitions/  ──▶ git
        ▲                                                          │
        └──────────── import definitions ◀────── zip ◀─────────────┘
```

- **Export once, early**, as soon as the workspace has anything worth keeping. Unpack it into
  the definitions project and commit. From then on the repository is the source of truth.
- **Edit files, then import.** Editing in the UI is fine for exploration — but re-export and
  commit before you forget, or the next import overwrites it.
- Import is system-admin only and is done through the admin UI or the API
  (`POST /api/graph/import-definitions`, a zip of `code config nlp`; it answers with an event
  stream that ends in `"Success": true` or the list of errors).
- Everything is a plain file: JSON for configuration, `.cs` for code (assistants, agents and
  prompt templates included — a header of attributes, then markdown), `.json` for NLP.
- **Start every new definitions file from the target workspace's own export**, not from a doc
  page or another repository. The importer is strict about shapes (§3a) and the export is the
  only shape it is guaranteed to read.

**Round-trip after every import.** Export again and adopt the workspace's formatting for every
file the repository owns; take `config/schemas/` and `code/ai-assistants/` whole; leave out what
the workspace seeds itself (its built-in tools and prompt templates, the `_`-prefixed indexes and
spotters, its own endpoints). Re-import the synced tree once to prove it imports clean. Two
things the export rewrites that are not changes: a BOM at the top of every code file, and
`"Language": "ab"` on every `Type: 0` pipeline-mapping entry (the language is ignored there).

**Anything the export does not carry** — an AI provider and its key, users, secrets — goes in
the definitions README as a manual step. An unexported piece of configuration is the thing that
will be missing on the demo machine.

---

## 2. Layout of a definitions folder

```
src/<App>.Definitions/
├── README.md                      what is here, what is NOT exported, how to import
├── config/
│   ├── general.json               graph-level switches and IsPublicNodeType (§3)
│   ├── interface.json             which product features are on
│   ├── search.json                search engine tuning
│   ├── search-node-types.json     per-node-type search defaults (IgnoredByDefault, …)
│   ├── llm-search.json            what the built-in LLM search may see
│   ├── pipeline-mapping.json      which NLP pipeline parses which field
│   ├── file-indexing.json         which file types are extracted, image captioning
│   ├── chat-policy-enforcement.json  chat governance
│   ├── schemas/
│   │   ├── edges.json             every edge type
│   │   └── nodes/<Type>.cs        every node type, as a C# class (export format)
│   └── indexes/others/            one file per (index kind, node type, field)
├── nlp/
│   ├── spotters/                  one file per (node type, field) spotter
│   ├── linking/                   one file per (data node type × field), all its rules
│   └── pipelines/                 the parsing pipelines
└── code/
    ├── endpoints/<path>.cs        server-side endpoints          → curiosity-code-endpoints
    ├── chat-ai-tools/<uid>.cs     AI tools, consult readers, sandboxes → curiosity-ai-tools-and-agents
    ├── agents/<name>-<uid>.cs     agents                          → curiosity-ai-tools-and-agents
    ├── ai-assistants/<name>-<uid>.cs  the assistant: header + system prompt
    └── ai-prompt-templates/       prompt templates
```

The `config/schemas/` folder mirrors what the connector created. It is **generated output**,
not a place to design: change the connector's `[Node]` classes and re-run, then re-export. The
importer reads `nodes/<Type>.cs`; a `nodes/<Type>.json` "didn't match any importer" and is
skipped. Leave `chat-ai.json`, `audit.json`, `llm-costs.json` and `synonyms.json` out of the
repository unless the application really owns them (§3).

---

## 3. What each file controls

**A list a file leaves out, or sets to `null`, is left as the workspace has it; an empty list
is a real setting and clears it.** `"IsPublicNodeType": []` hides every type from ordinary
users. Settings that sit in the wrong file are ignored silently — the 26.10 split moved search
types, LLM search and the pipeline mapping out of `general.json`, and a `general.json` still
carrying them imports "successfully" and configures none of it.

### `general.json` — graph level

- **`IsPublicNodeType`** — the node types visible without per-record access control. **This is
  the setting people miss.** A node type that is not listed is invisible to ordinary users, so
  a renderer works for an admin and shows nothing for everyone else. When the corpus is
  workspace-wide, list every type you created.
- `ExtraCSS`, `ExtraHTML` and the commit/cache switches. `WorkspaceName` and `LicensedTo` are
  **never imported** — each environment keeps its own name.
- `NodeTypeAccessControl` / `FieldAccessControl` are read-only legacy: node-type and field access
  live as attributes on the schema class (`config/schemas/nodes/<Type>.cs`).

### `chat-ai.json` — the AI providers: usually leave it out

It names the default, admin and compaction **provider tasks** by UID, plus chat retention and the
deep-research caps. A provider task is per environment (its key is a secret), so a UID exported
from one workspace names nothing in the next — and the agents and the chat then have no model.
Do not commit it; if you must, use the all-ones sentinel `1111111111111111111111`, which means
"the workspace's default". Configure the provider on each environment by hand and list that as a
manual step in the definitions README. Agents follow the same rule (`ChatTask` left out —
`curiosity-ai-tools-and-agents` §4).

### `search-node-types.json`, `llm-search.json`, `pipeline-mapping.json`

- **`search-node-types.json`** — node types by role: `IgnoredByDefault` (left out of a search
  unless asked for), `SaveViewsOf`, `RelatedFacets`, `ExcludedFromTimestampFacet`.
- **`llm-search.json`** — `NodeTypes` and `Filters` the built-in LLM search exposes to an
  assistant; empty means the built-in defaults.
- **`pipeline-mapping.json`** — which NLP pipeline parses which field. **The importer replaces
  the whole mapping**, and a field with no entry is parsed by a pipeline without your spotters —
  so no `_Mentions` edge is ever written. See `curiosity-entity-linking-nlp` §4.

### `interface.json`

Feature switches: graph, teams, apps, sharing, contacts, inbox, calendar, notes, clipboard,
guest accounts, self-signup, uploads. **Turn off everything the use case does not use.** A
sidebar full of product features the demo never touches is the fastest way to make a focused
application look like a generic one. `Announcement` is an object (title, message, validity), not
a sentence; `DisableTransparency` is a desktop window setting and does not import.

### `search.json`

Engine tuning: concurrency, highlight limits, facet limits per type, minimum token length,
thresholds that decide when a query is handled as an AI search, similarity decay, timeouts.
Start from the export, and keep its shapes: `DefaultMergeToRelated` is an object `{}`,
`DefaultSkipMerge` a list `[]`.

The two worth revisiting per use case:

- **facet limits**, if your dimension nodes are numerous and users need to see them all;
- **the AI-search thresholds**, which decide when a long natural-language query is treated as
  a question rather than as keywords.

### `indexes/others/`

One file per (index kind, node type, field): `index-full-text-search.*`, `index-sentence-
embeddings.*`, `index-parser.*` (the `FieldToDocumentIndex` that feeds a field to its NLP
pipeline), `index-property-filter.*`, … — see `curiosity-entity-linking-nlp` §5 for what each
setting decides. Three 26.10 rules:

- **A setting the server no longer knows fails the whole import** with `Setting X not found`.
  Gone in 26.10: `MaximumBatchSize` and `Buckets` (every index), `NormalizeDiacritics` (sentence
  embeddings, property filters), `IndexParallelism`, and `ExtractAsMarkdownIfPossible` (file
  content); `search.json` lost `MaximumResultsAlwaysEnforcingOwnership`. The export adds
  `EnableRestrictedIndexes` and `PrefixFieldName`. **If a generator writes these files, fix the
  generator** — a hand-fixed tree is undone by the next regeneration.
- **Every sentence-embedding index needs `EnableAISearch: true`.** `StartAtSimilarTextAsync`
  without an index UID reads only AI-search-enabled indexes, so with it off every text-similarity
  signal is empty and nothing says why (`curiosity-similarity-scenarios`).
- `config/indexes.json` is not an import file — a plan written there creates no index. Do not
  ship a `_FileEntry` file-content index when the corpus has no attachments; the import keeps
  the environment's own.

### `file-indexing.json`

Which file categories are extracted and indexed, extractor concurrency, and image captioning.
Relevant whenever records carry attachments — if the published artefact is the thing users
read, its **content** must be indexed, not just its existence.

### `chat-policy-enforcement.json`

How the chat policies are applied as a set. Set it deliberately for a customer demo.

---

## 4. Ordering — what must exist before what

Import failures are almost always ordering.

1. **The graph schema must exist before endpoints import.** The workspace injects generated
   `N` / `E` constants above every endpoint script, so an endpoint naming a node type the
   connector has not created **does not compile**. → **Ingest before importing endpoints.**
2. **Nodes must exist before spotters are built.** A spotter built from an empty catalogue is
   an empty spotter and needs rebuilding after the ingest.
3. **Indexes should exist before the bulk ingest** where you can manage it, so indexing happens
   as records land rather than as one enormous catch-up.
4. **Spotters must be in a pipeline, and the parsed fields mapped to that pipeline,** before
   linking produces anything. A mapping imported onto a graph that is already ingested changes
   nothing until the parser indexes are rebuilt (`POST index/recreate` per
   `FieldToDocumentIndex` — `curiosity-entity-linking-nlp` §4).
5. **Anything referenced by UID must be imported before the thing referencing it** — a tool
   before the agent that names it, an agent before the assistant that names it. One import of
   the whole `code config nlp` zip orders this itself; importing `code/` in pieces does not.

---

## 5. UIDs — which to hand-craft and which to mint

Some things are referenced by a stable UID from code: an agent named by a chat view, a tool
pinned in a post-message call, an assistant named by a front-end button.

- **Built-in product objects** have recognisable, stable UIDs and are re-seeded on every boot;
  refer to them by those.
- **Objects you define** are created once by an import and referred to by whatever id they were
  given. **Mint a random UID** for them. Do not hand-shape a readable one: not every 22-char
  string round-trips through the UID parser (`BastionExposure1111111` does not), and there is
  nothing a readable id buys for an object only this application has.
- **The exception is a definition every application ships under the same id** — Search Records
  (`SEARCHRecords111111111`), a sandbox, the default assistant (`defaULtAiASSistaNt1111`) — so
  the chat view and the prompts can name it the same way everywhere. Check a readable UID
  round-trips (`UID128.Parse(s).ToString() == s`) before you commit it.
- **Whatever the UID, write it down in exactly one place per side** — a `const` in the front
  end, the filename in the definitions folder — and cross-reference the two in comments. A UID
  duplicated in three files is three places to get it wrong.

---

## 6. Access

- **Endpoint `AccessMode`** — `Unrestricted`, `AllUsers`, `AccessControlled`, `AdminOnly`.
  Pick per endpoint; a shared library endpoint that is not meant to be callable is `AdminOnly`.
- **`IsPublicNodeType`** — as above, per node type.
- **AI tool `AccessMode`** — same idea for tools.
- Where the customer needs per-record visibility, that is access groups plus per-node access
  control, and it needs to be in the discovery brief (round 4, question 7) — retrofitting it
  changes every query.

---

## 7. Promoting between environments

```bash
cd src/<App>.Definitions && zip -qr /tmp/defs.zip code config nlp -x '*README.md'
curl -sN -H "Authorization: Bearer $TOK" -X POST "$U/api/graph/import-definitions" \
     -F "file=@/tmp/defs.zip;type=application/zip"              # event stream; ends "Success": true
```

Then, in order: ingest → import definitions → recreate the parser indexes if the mapping or the
catalogue changed → configure the AI provider by hand → smoke-test one query, one endpoint, one
chat turn → export and round-trip (§1). `curiosity-delivery` has the whole loop with the checks
at each step.

Keep a short `README.md` in the definitions folder listing exactly this sequence and anything
manual. On a demo machine, that file is what saves the morning.

---

## 8. Checklist

- [ ] Definitions exported, unpacked into the repo, and committed
- [ ] `README.md` in the definitions folder lists the import sequence and every manual step
- [ ] Everything the export does **not** carry is in the repo with a note (the AI provider)
- [ ] No setting the server dropped (`MaximumBatchSize`, `Buckets`, `NormalizeDiacritics`, …);
      every embedding index has `EnableAISearch: true`; schemas are `nodes/<Type>.cs`
- [ ] Search types, LLM search and the pipeline mapping in their own files, not `general.json`
- [ ] No `chat-ai.json` or agent `ChatTask` naming another environment's provider
- [ ] The export round-tripped into the repo after the last import
- [ ] `IsPublicNodeType` lists every node type the corpus needs visible
- [ ] `interface.json` has every unused product feature switched off
- [ ] Every index, spotter, linking rule and pipeline is a committed file
- [ ] Endpoints import **after** an ingest has created the schema
- [ ] Custom UIDs are randomly minted, and each is written in exactly one place per side
- [ ] A fresh environment can be rebuilt from the repo alone, and someone has proved it
