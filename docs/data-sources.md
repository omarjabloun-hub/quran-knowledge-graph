# Data sources

Every source is listed in [`data/manifest.json`](../data/manifest.json) with its URL, its target
under `data/raw/` and its SHA-256. `just fetch` downloads and verifies them; nothing under
`data/raw/` is committed. `src/QuranKG.Dataset` reads and joins them, and refuses a corpus that
does not add up (6,236 verses in every text, every word's verse present, …).

| Source | What it gives the graph | Licence and attribution |
|---|---|---|
| **Kaggle — The Holy Quran Dataset** (yasirabdaali) | surah names (Arabic, scholarly transliteration, English), revelation place and order, per-verse transliteration | CC0 on Kaggle. Its Arabic is Tanzil's text, its English Qara'i's translation |
| **Tanzil metadata** (`quran-data.xml`) | surah and verse structure: juz, hizb, rub', manzil, ruku', page, sajda | Tanzil, CC BY 3.0 — attribute and link to tanzil.net |
| **Tanzil Uthmani text** | the text as displayed, with its marks | Tanzil, CC BY 3.0 — verbatim copies only, keep the notice |
| **Tanzil Simple-Clean text** | standard spelling, no diacritics — what search and the embeddings read | as above |
| **Saheeh International** (Tanzil copy) | an English gloss on the verse page | Tanzil translations: non-commercial use, with attribution |
| **Ali Quli Qara'i** (Tanzil copy, without footnotes) | a second English gloss | as above |
| **Quranic Arabic Corpus morphology** (v0.4, the `mustafa0x/quran-morphology` Arabic-script fork with root and lemma fixes) | every word's segments, part of speech, features, **root** and **lemma** — the backbone of the lexical layer | GNU GPL (corpus.quran.com) — attribution, verbatim |
| **QurSim** (Sharaf & Atwell, LREC 2012) | 7,295 verse pairs related by Ibn Kathir's tafsir, graded 0–2 — **the answer key** for similarity | research data; cite the paper |
| **QUL ayah topics** (Tarteel) — *manual download* | ~2,500 topics in a hierarchy, including the stories — the reference for clustering | per the resource card on qul.tarteel.ai |

The Kaggle dataset was the starting point; it is kept for what only it has (names, revelation
order, transliteration), and the Arabic and English come from their primary sources instead.
Two of its traps are handled in the reader: its rows are sorted by verse id **as text** (so the
ayah number is taken from the id, never from the row order), and its English carries inline
footnotes (so the clean Tanzil copy of Qara'i is used).

## Cleaning rules

- The Bismillah Tanzil prefixes to verse 1 of every surah but 1 and 9 is removed from both
  Arabic texts (112 verses) — in surah 1 it **is** verse 1, and surah 9 has none. The surah
  reading view draws it as a heading.
- A word's root and lemma come from its stem; where a word has several stems, from the one that
  carries a root.
- QurSim: 28 malformed or out-of-range rows and self-pairs are dropped; pairs are undirected and
  keep the highest degree given to them.

## Answer keys stay out of the AI's reach

QurSim is how the similarity ranking is measured, and the QUL topics are how clusters will be. Both
are in the graph so people can read them and so scores can be computed — but no similarity signal,
no AI tool, no index the AI can query and no document handed to a model may read them. The only
endpoint that returns QurSim pairs (`qkg/verse/words`, for the verse page) must never be exposed as
an AI tool.

## Worth adding next

| Dataset | Would add | Notes |
|---|---|---|
| Tafsir texts (Ibn Kathir, al-Jalalayn, al-Muyassar) — Tanzil / QUL | commentary per verse, for people and as context for the assistant | check each tafsir's licence; keep out of the similarity signals that QurSim measures |
| QUL word-by-word translations | an English gloss per word in the word-by-word view | QUL licence per resource |
| Asbab al-nuzul (occasions of revelation) | why a verse was revealed — a link from verses to events and people | needs a clean digital edition |
| Named entities (prophets, places, peoples) — the Quranic Arabic Corpus ontology | `Person` / `Place` nodes linked to verses: the backbone of the stories | the corpus ontology covers ~300 concepts |
| Recitation timings (Quran.com / EveryAyah) | play a verse in a chosen reciter | audio is large: link, do not ingest |
| Hadith collections (sunnah.com dumps) | hadith that cite a verse | a different corpus and its own authenticity grading |
