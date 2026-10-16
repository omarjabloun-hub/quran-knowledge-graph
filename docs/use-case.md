# Use case

**Study the Quran as a knowledge graph — Arabic first — and let anyone see its patterns.**

The meaning of the Quran is in its Arabic. The graph is built from the original text and its
morphology: every one of the 77,429 words carries its root and lemma (Quranic Arabic Corpus), so
the questions below are answered from the Arabic, with translations as reading aids.

## Questions it answers

| Question | Where |
|---|---|
| What is the text made of — how many surahs, verses, words, distinct forms, lemmas, roots; nouns, verbs, particles? | the dashboard's totals |
| How do the Meccan and Medinan surahs differ — in length, in vocabulary? | dashboard: Meccan and Medinan, most Meccan / Medinan roots |
| How does the verse length move through the book, and through the order of revelation? | dashboard: verse length (two charts) |
| Which roots and words carry the text? Where in the Quran does each root live? | dashboard: vocabulary, the root map (a cell opens its verses) |
| What is one surah about — which roots does it use far more than the rest? | the surah page, Profile tab |
| How is one verse built, word by word? | the verse page, Verse tab |
| Where is this verse — 2:255, Al-Kahf, page 50? Which verses say «الرحمن», or "mercy"? | the search box on the home page |
| How does this surah sound? Follow the text while it is recited | the Quran View's Listen: Sheikh Latrech, Qalun from Nafi', the verse being recited marked |
| Read the Quran as it is read in Tunisia | the Quran View in Qalun from Nafi' — its text, numbering and mushaf pages — with Hafs one click away, the place kept; Sheikh Latrech's recitation following Qalun's own verses exactly |
| How do I recite this correctly? | the Tajweed switch in the reader: every letter in the colour of its rule, as the Dar al-Maarifah Qalun mushaf prints it; the Tajweed page: each rule explained, counted and shown in words to read and hear |
| What does this verse mean? | the verse panel and the verse page: Ibn Ashur's *al-Tahrir wa al-Tanwir* first, then al-Mukhtasar, al-Jalalayn and Ibn Kathir, each in Arabic and English |
| Which hadith quote this verse, or speak to it? Is this hadith backed by the Quran? | the verse's Hadith tab; a hadith's page (the verses it quotes, the closest verses); the assistant argues the relation verse by verse |
| Where else does the Quran say this? | the verse page: Similar verses (Arabic meaning + shared lemmas + roots), Ibn Kathir links (QurSim) |
| Where does a root occur, in which forms, under which lemmas? | the root page |
| I want to read the Quran, and see what a verse says and what it is tied to | the Quran View (Read the Quran): click a verse |
| What are the Quran's stories, and where does it tell each one? | the Stories and themes page — the map lights a theme across the surahs; a theme's page reads its passages one after another |
| How is one surah built — what are its sections? | the surah page, Passages tab (and each passage's page) |
| Which surahs are like this one? | the surah page, Similar surahs (shared distinctive lemmas and roots, verses close in meaning, and the themes both tell) |

## Principles

1. **Arabic first.** Search, similarity and clustering read the Arabic; the translations are
   glosses for readers.
2. **Every number is counted live from the graph**, and every count opens the nodes it counted.
3. **Measured, not asserted.** Similarity is scored against QurSim (Ibn Kathir's related verses),
   the passages against the ruku' (the traditional sections), the themes against QurSim and, once
   downloaded, the QUL topics — and every reference stays out of what it measures and out of the
   AI's reach.
4. **The workspace lives in git.** Anything changed in the admin UI is exported and committed;
   a new machine is rebuilt from this repository with `just bootstrap`.
5. **Local by default.** The models run on the Mac (Ollama); nothing has to leave it.

## Users

- **Readers** — anyone curious: the dashboard, a surah read in order, a verse word by word.
- **Students of Arabic and of the Quran** — roots, lemmas, forms and where they occur.
- **Researchers** — the patterns, the similarity ranking and how it is measured; the benchmark.
- **Builders** — extending the graph, with Sudo (the workspace's admin assistant) or by hand, with
  the skills in `.claude/skills/`.
