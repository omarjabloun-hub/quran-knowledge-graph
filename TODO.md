# TODO

What is built, what is next. Each item says what, why and where; tick it in the commit that
closes it.

## Built

- [x] Corpus: 9 sources with checksums (`data/manifest.json`), read, cleaned and joined with tests
      (`src/QuranKG.Dataset`, 25 tests)
- [x] Graph: 10 node types, ~97,500 nodes, ~428,400 edges, idempotent ingest with a dry run
      (`src/QuranKG.Connector`)
- [x] Workspace configuration as files: Arabic-first indexes (diacritics-insensitive full text,
      Harrier embeddings of the plain Arabic), styles, search settings (`src/QuranKG.Definitions/config`)
- [x] Endpoints: dashboard corpus, surah / root profiles, verse words, similar verses
      (`src/QuranKG.Definitions/code/endpoints/qkg`)
- [x] Front end: the pattern dashboard, a renderer per node type, the surah reading view and
      profile, the verse word-by-word view, similar verses, tafsir links, the root distribution
      (`src/QuranKG.FrontEnd`)
- [x] Local runbook for a Mac: compose, `.env`, Justfile, bash + PowerShell scripts, the
      workspace tool, the embeddings switch to Ollama (`docs/local-setup.md`)
- [x] Embedding benchmark against QurSim, dev/test protocol, first results — and the similarity
      weights set from it (`tools/embedding-benchmark`, `qkg/verse/similar`)

## Next — on the Mac

- [ ] **Run the benchmark with the large models** (`just models-pull`, `just benchmark`):
      `qwen3-embedding:8b` / `:4b` and `bge-m3` through Ollama, `Qwen3-Embedding-4B` through MPS.
      The bar: AraModernBert-STS fused with the morphology reaches top-10 recall 20.2 (strong
      pairs 34.5) on the held-out half, against 17.5 for the built-in Harrier at weight 0.25. Choose
      the model and its `W_SEM` on the dev half, report on the test half, switch with
      `just embeddings-ollama`, record the numbers in `tools/embedding-benchmark/README.md` and in
      the endpoint's header comment. Watch the *no-shared-lemma* column: nothing finds those
      pairs yet.
- [ ] **QUL topics**: download `topics.db` (manual, free account — see `data/manifest.json`), then
      `just check-data && just ingest`. `TopicIngest` and the `Topic` renderer exist; nothing has
      been ingested yet. Check the hierarchy (`ParentTopic`) and the Stories branch.
- [ ] **Clusters** — the Quran's stories, and each surah's passages. Written inside the workspace
      (a scheduled task or an admin endpoint, so Sudo can re-run and extend it), never by the
      connector:
  - per surah: contiguous segments — boundaries where adjacent-verse similarity (embedding +
    lexical cohesion) drops, TextTiling-style;
  - whole Quran: verses grouped across surahs (agglomerative / HDBSCAN on the fused similarity),
    keeping clusters that span several surahs — the recurring stories (Musa, Ibrahim, Yusuf…);
  - each cluster named in Arabic and English by the chat model from its distinctive roots and
    representative verses; `Cluster` nodes + `HasMember` / `ClusterOfSurah` edges;
  - scored against the QUL topics (purity, adjusted Rand) — which stay out of the clustering's
    reach, like QurSim out of the similarity's.
- [ ] **AI layer**: an Ollama chat provider (`docs/models.md`), then AI tools for the workspace
      assistant — search verses (Arabic first), read a verse with its words, a root's profile —
      none of which may reach `QurSimPair`, `Topic` (while it is the clustering reference) or
      `qkg/verse/words`. Add a skill the workspace assistant reads, describing this schema.
- [ ] **Similar words**: the dashboard counts words, forms, lemmas and roots; "similar words"
      beyond a shared root (near-synonyms such as خوف / خشية / رهبة) need a lemma-level similarity
      — embeddings of the lemmas' contexts, or co-occurrence — and a "word family" view.

## Later

- [ ] Tafsir texts (al-Muyassar, al-Jalalayn, Ibn Kathir) as reading context, after their licences
      are checked — and kept out of the similarity signals QurSim measures.
- [ ] Named entities from the Quranic Arabic Corpus ontology (persons, places, peoples) as nodes:
      the backbone of the stories.
- [ ] Word-by-word English gloss (QUL) in the verse view.
- [ ] An Arabic interface (RTL, `.t()` translations) for Arabic-speaking readers.
- [ ] CI: build the solution, run the tests, `sync-dto --check`, the dry-run ingest.
- [ ] Response-cache invalidation when only `_lib/dto.cs` changes (today: restart the workspace —
      see CLAUDE.md).
