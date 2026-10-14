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

- [x] **Run the benchmark with the large models** on the Mac: 24 embeddings — the Ollama models,
      Qwen3-Embedding 0.6B / 4B on MPS, multilingual e5 / LaBSE / Arctic, the Arabic models (Arabic
      Harrier, GATE, NLI-triplet, CAMeLBERT-CA) — each weight chosen on the dev half, reported on
      the test half (`just benchmark --weights`; `tools/embedding-benchmark/README.md`).
      AraModernBert-STS stays best (20.2, strong 34.5); `qwen3-embedding:8b` is the best through
      Ollama (19.0 / 32.4 at W_SEM 0.5); nothing finds the pairs that share no lemma.
- [x] **Switch the verse embeddings** to AraModernBert-STS (`W_SEM = 1.0`: 20.2, strong 34.5),
      served on the Mac by `tools/embedding-server` (`just embedding-server`, started by `just up`)
      behind the OpenAI embeddings API; `just embeddings-arabic`. The workspace ranks exactly as the
      benchmark (same top 20 on five verses).
- [ ] **Serve the embeddings model for production**: the server binds the Mac's 127.0.0.1; a
      production workspace needs it hosted where it can reach it (`EMBEDDING_SERVER_URL`).
- [x] **Similar surahs from the embeddings**: each seed verse's 10 nearest verses in the other
      surahs, counted per surah, × 1.5 (`qkg/surah/similar`, `tools/cluster-benchmark`): held-out
      Spearman 0.40 → 0.41, top 5 in our top 10 32 → 36 % (+4, CI +1 to +7). Averaged surah vectors
      score below the lexical signals. The closest verse pairs are shown on each similar surah.
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
- [x] **AI layer**: the Quran Research Assistant on its own chat page, with the Quran tools
      (count by lemma / root / form / text with the verses and the same-count baseline,
      co-occurrence, read verses word by word, similar verses and surahs), a sandbox over the
      corpus as files, Search Records and the consult readers — none reaching `QurSimPair`, `Topic`,
      `Verse.Ruku` or `qkg/verse/words`. Chat through Claude via a local wrapper (`docs/local-setup.md`).
- [ ] **AI layer, next**:
  - an "Ask the assistant" button on the verse, surah, root and theme pages that opens a chat with
    the node attached (`curiosity-chat-experience`, AiAssist);
  - verse keys in replies as links (`ParseAs` needs a spotter that recognises `2:255`);
  - saved findings: a claim, its counting rule, its counts, its verses and its baseline, kept as a
    node a reader can reopen and the assistant can build on;
  - an investigation agent for long pattern searches (hypothesis → count → baseline, many rounds),
    with its own budget, reporting the hypotheses that failed with the ones that held.
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
