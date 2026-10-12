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
- [x] Clusters, first version, built inside the workspace (`qkg/clusters/build`, `just clusters`,
      part of `just bootstrap`): every surah cut into passages where its vocabulary changes (561),
      passages of different surahs grouped into themes (119) — from the lemmas and roots, no model.
      Scored on references they do not read (`tools/cluster-benchmark`): passages against the ruku'
      (Pk 0.334 vs 0.399 for equal-length cuts, held-out surahs), themes against QurSim (6.8× chance).
      Shown on the Stories and themes page, the surah's Passages tab, the theme and passage pages
- [x] Similar surahs (`qkg/surah/similar`): shared distinctive lemmas and roots, with the themes
      both tell as the reason — Spearman 0.41 against QurSim summed over pairs of surahs

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
- [ ] **Clusters, next** (measure each with `just benchmark-clusters`, choose on `--split dev`):
  - name each theme in Arabic and English with the chat model, from its key words and opening
    verses, once a provider is configured — today's labels are its most distinctive lemmas;
  - score the passages and themes against the QUL topics once `topics.db` is downloaded (purity,
    adjusted Rand) — which, like QurSim and the ruku', stay out of the clustering's reach;
  - a second level: the same tree cut at 0.15 splits the stories into their episodes (Moses and
    the magicians, Moses at the appointed time, the calf);
  - try the large embedding models in the cohesion and in the passage vectors — Harrier and
    AraModernBert-STS gave no reliable gain to the passages.
- [ ] **AI layer**: an Ollama chat provider (`docs/models.md`), then AI tools for the workspace
      assistant — search verses (Arabic first), read a verse with its words, a root's profile —
      none of which may reach `QurSimPair`, `Topic` (while it is the clustering reference),
      `Verse.Ruku` (the passages' reference) or `qkg/verse/words`. Add a skill the workspace
      assistant reads, describing this schema.
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
