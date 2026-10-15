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
- [x] **Reading** (`qkg/read/text`, `Components/Reader.cs`, `ReadingPane.cs`, `Reader/QuranView.cs`):
      the Quran View (`#/read/2/255`) — the 114 surahs, each read as continuous Arabic in its
      passages, the English beside it on demand, a clicked verse opening its translations, words,
      passage, theme, closest verses and tafsir links. A surah's Read tab, a passage and a theme read
      through the same pane, with each passage's or the theme's key words marked
- [x] **Tafsir** (`TafsirSource`, `TafsirIngest`, `qkg/verse/tafsir`, `Components/TafsirView.cs`):
      al-Mukhtasar, al-Jalalayn and Ibn Kathir, each in Arabic and English (QUL via spa5k/tafsir_api,
      pinned), as `Tafsir` / `TafsirEntry` nodes linked to every verse they explain. In the verse
      panel and the verse page's Tafsir tab; a long commentary opens whole, Arabic beside English.
      Out of the AI's reach (Ibn Kathir is QurSim's source)
- [x] **Hadith and the Quran** (`HadithSource`, `Text/QuranQuotes.cs`, `HadithIngest`, `qkg/hadith/*`,
      `qkg/verse/hadith`, `Hadith/HadithStudyView.cs`, `HadiThTooLs…`): Bukhari and Muslim in Arabic
      and English; the verses each quotes (braces, runs, whole verses; phrases graded apart;
      measured on the translators' citations, 94% / 82%); the closest verses by meaning and
      distinctive words (`just benchmark-hadith`: 28% top-10 on the hard cases, chance 0.2%);
      a study page, hadith pages, a Hadith tab on verses; the assistant's Hadith tools and its
      method for the relation (quotes / agrees / explains / adds / apparent tension)
- [ ] **Hadith, next**:
  - the relation as data: run the assistant's method over the hadith that quote a verse (1,137)
    and store each verdict, its verses and its argument as a node, labelled as the model's
    reading — a sweep, to be scoped and costed before it runs;
  - the chain of narrators as a graph: narrators as nodes (isnad), so a hadith's transmission is
    read the way its text is;
  - the parallels: a hadith in both collections (muttafaq 'alayh) and its variants, found from the
    texts' meaning;
  - the matn heuristic measured (against sunnah.com's narrator/text split) and improved;
  - Abu Dawud, al-Tirmidhi, al-Nasa'i, Ibn Majah, with their gradings.
- [x] **The clusters map** (`qkg/clusters/map`, `Themes/ClusterMap.cs`): every surah in its passages,
      coloured by theme or by similarity group, on the Stories and themes page; hover lights a group
      across the Quran
- [x] **Similarity groups** (Sudo, in the workspace; now in git: `qkg/clusters/similar-build`,
      `-groups`, `-score`, `-sweep`): passages grouped by meaning + lemmas + roots, 141 at tau 1.5.
      Reviewed: at the same coverage they score as the themes (`just benchmark-clusters --groups`);
      `qkg/clusters/build` no longer deletes them; shown beside the themes on the map, in the reader
      and on their own pages
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
    verses — a provider is now configured; today's labels are its most distinctive content lemmas
    (particles and adverbs were dropped from the labels on 2026-10-14);
  - the grouping still reads every lemma: try leaving particles and adverbs out of the passage
    vectors too — rare ones (كلا, بل) group passages by style (themes such as "أدرى · نعيم · سجين")
    — and keep it only if the themes score as well against QurSim;
  - similarity groups: the sweep keeps the tau with the best lift, which is always the highest
    tried, and tunes on QurSim itself — choose by reading or on half the pairs; their labels still
    include pronouns and particles (إيا); `similar-groups` answers anonymous types, not DTOs;
  - a theme's key is a hash of its passages, so a change to any one of them gives the theme a new
    UID and a saved link to it breaks on the next build: key it on something steadier (its largest
    passage);
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
      Verse keys in a message (`21:30`, `2:255–257`) are links that open the verse in its surah over
      the chat (`Components/VerseReferences.cs`, the chat's `CustomMessageRenderer`; no `ParseAs`
      spotter needed).
- [ ] **AI layer, next**:
  - an "Ask the assistant" button on the verse, surah, root and theme pages — and in the reader's
    verse panel — that opens a chat with the node attached (`curiosity-chat-experience`, AiAssist);
  - saved findings: a claim, its counting rule, its counts, its verses and its baseline, kept as a
    node a reader can reopen and the assistant can build on;
  - an investigation agent for long pattern searches (hypothesis → count → baseline, many rounds),
    with its own budget, reporting the hypotheses that failed with the ones that held.
- [ ] **Similar words**: the dashboard counts words, forms, lemmas and roots; "similar words"
      beyond a shared root (near-synonyms such as خوف / خشية / رهبة) need a lemma-level similarity
      — embeddings of the lemmas' contexts, or co-occurrence — and a "word family" view.

- [ ] **Reading, next**: choose the translation shown beside the Arabic (Qarai, and the QUL
      word-by-word gloss once ingested); read by juz and by mushaf page (`Verse.Page`) as well as by
      surah; remember the last verse read.

## Later

- [ ] Tafsir, next: search inside the tafsirs for people (a full-text index on `TafsirEntry.Text`,
      kept out of Search Records); more works (al-Sa'di, al-Tabari, al-Qurtubi — Arabic only in QUL);
      decide whether al-Mukhtasar may become an AI reader (Ibn Kathir never: QurSim's source); check
      the English licences (Darussalam, Royal Aal al-Bayt) before a public deployment.
- [ ] Named entities from the Quranic Arabic Corpus ontology (persons, places, peoples) as nodes:
      the backbone of the stories.
- [ ] Word-by-word English gloss (QUL) in the verse view.
- [ ] An Arabic interface (RTL, `.t()` translations) for Arabic-speaking readers.
- [ ] CI: build the solution, run the tests, `sync-dto --check`, the dry-run ingest.
- [ ] Response-cache invalidation when only `_lib/dto.cs` changes (today: restart the workspace —
      see CLAUDE.md).
