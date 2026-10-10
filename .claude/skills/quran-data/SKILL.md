---
name: quran-data
description: The Quran corpus behind this repository — sources and their traps, node keys, the Uthmani vs Simple-Clean text forms, the Quranic Arabic Corpus morphology (segments, stems, roots, lemmas, parts of speech), the Bismillah rule, QurSim and the QUL topics as answer keys, Arabic text normalisation, and the measured facts any new feature should agree with. Use before changing data/, src/QuranKG.Dataset, the connector, a similarity or clustering signal, or anything that counts words.
---

# The Quran corpus — what is in it and what bites

## 1. Sources (data/manifest.json)

| Source | Gives | Trap |
|---|---|---|
| Tanzil `quran-uthmani.txt` | display text, with all marks | prefixes the Bismillah to verse 1 of 112 surahs (§4); contains pause marks and small high letters as separate tokens |
| Tanzil `quran-simple-clean.txt` | standard spelling, no diacritics — search and embeddings | same Bismillah prefix |
| Tanzil `quran-data.xml` | surah metadata, juz / hizb / rub' / manzil / ruku' / page / sajda boundaries | boundaries are *starts*: a verse's juz is the last start at or before it |
| Kaggle (yasirabdaali) | names, revelation place and order, transliteration | rows sorted by verse id **as text** — take the ayah from the id's integer part, never from row order; its English has inline footnotes (use Tanzil's Qara'i) |
| Qara'i, Saheeh International (Tanzil) | English glosses | non-commercial, attribute |
| `mustafa0x/quran-morphology` (QAC v0.4, Arabic script) | per segment: form, tag, features (`ROOT:`, `LEM:`, `PREF`, `SUFF`, `VF:` …) | location `s:a:w:segment`; a word is several segments |
| QurSim (`ibn_kathir_similarity.csv`) | 7,703 rows → **7,295** undirected pairs, degree 0/1/2 | malformed rows, self-pairs, out-of-range verses; both directions present |
| QUL topics (`topics.db`, manual) | ~2,500 topics, hierarchical, Stories branch | needs a free account; schema read tolerantly |

Every download is checked against its SHA-256; a changed upstream file fails `just fetch` rather
than silently changing the graph.

## 2. Keys

| Node | Key | Example |
|---|---|---|
| Surah | number | `2` |
| Verse | `surah:ayah` | `2:255` |
| Word | `surah:ayah:position` (QAC word numbering) | `1:2:1` |
| Root | letters, unspaced | `رحم` (display `Spaced`: `ر ح م`) |
| Lemma | `lemma|root|coarse tag` | `رَحْمَة|رحم|N` — 13 spellings name two different words, so the lemma alone is not a key |
| Juz | number | `30` |
| QurSimPair | `a~b`, verse keys in mushaf order | `1:2~6:45` |

UIDs are derived from type + key, so a re-ingest updates nodes in place.

## 3. Two text forms, two jobs

- **Uthmani** (`Verse.TextUthmani`, `Word.TextArabic`) is what a reader sees: diacritics, the
  dagger alif, small high letters, pause marks. Display only.
- **Simple-Clean** (`Verse.TextSimple`, `Word.TextPlain`) is standard modern spelling without
  diacritics. Search, embeddings and "distinct form" counts read this: models are trained on
  undiacritised modern spelling, and the Uthmani form costs ~4× the tokens and differs in 17 % of
  word spellings (رحمٰن vs رحمن, صلوٰة vs صلاة).
- Full-text indexes use `NormalizeDiacritics` (strip nonspacing marks after FormD), so a query
  with or without tashkeel matches.
- Stripping marks by hand: remove U+0610–061A, U+064B–065F, U+0670, U+06D6–06ED and tatweel
  U+0640; map alif wasla U+0671 → U+0627 (`QuranKG.Dataset.Text.Arabic`).

## 4. The Bismillah

Tanzil writes «بسم الله الرحمن الرحيم» at the start of verse 1 of every surah except 1 (where it
*is* verse 1) and 9 (which has none). It is removed from both Arabic texts of those 112 verses
— otherwise they all look alike to every signal — and drawn as a heading in the reading view.
The morphology never counts it (its word numbering starts after it), so word counts agree.

## 5. Morphology

- A word = prefixes + stem + suffixes. Its **root and lemma are its stem's**; when a word has
  several stems, the one carrying a root.
- Coarse tag of the stem: `N`, `V`, `P`. Fine POS (readable names in `Word.PartOfSpeech`): Noun,
  Proper noun, Adjective, Active / Passive participle, Verbal noun, Pronoun, Demonstrative /
  Relative pronoun, Time / Location adverb, Interrogative / Conditional noun, and ~30 particle
  kinds.
- **Function nouns.** The corpus tags إِذَا, إِذ, قَبْل, بَيْن, مَن, مَا … as nouns (adverbs,
  relative / interrogative / conditional nouns). Any "most frequent nouns" list must leave them
  out (`QK.IsContentNoun`) or it is dominated by them.
- Verb form (I–XII) is on `Word.VerbForm` (0 = not a verb).

## 6. The answer keys

- **QurSim** (Sharaf & Atwell 2012): verses Ibn Kathir's tafsir relates. 2,852 pairs degree 2,
  3,580 degree 1, 863 degree 0. 3,943 verses have at least one degree ≥ 1 partner. It **scores**
  similarity; nothing that computes similarity, nothing an AI can reach, may read it.
- **QUL topics** will score clustering the same way.
- A model fine-tuned on QurSim (`Mathani-Ayat/rattil-ayah-embed`) cannot be evaluated on it.

## 7. Measured facts — a new feature should agree with these

| Fact | Value |
|---|---|
| surahs / verses / words | 114 / 6,236 / 77,429 |
| distinct forms (no diacritics) / lemmas / roots | 14,810 / 4,776 / 1,651 |
| nouns / verbs / particles (stem coarse tag) | 41,086 / 19,353 / 16,990 |
| Meccan: surahs / verses / words / words per verse | 86 / 4,613 / 47,439 / 10.28 |
| Medinan: surahs / verses / words / words per verse | 28 / 1,623 / 29,990 / 18.48 |
| most frequent roots | أ ل ه 2,851 · ق و ل 1,722 · ك و ن 1,390 · ر ب ب 980 · أ م ن 879 |
| graph | ~97,533 nodes, ~428,400 edges |

Similarity on QurSim (top-10 recall of related verses; fused rows on the held-out half):
shared lemmas 17.0–17.7, shared roots 17.0, the best Arabic sentence model alone 16.5
(AraModernBert-STS), the built-in Harrier alone **9.2** (last). Fused as `qkg/verse/similar`
fuses: Harrier × 0.25 + lemmas + 0.5 roots 17.5 (strong pairs 29.4); AraModernBert-STS × 1.0 +
lemmas + roots **20.2** (strong 34.5). 9,976 related pairs share a lemma and 2,888 share none —
and every method so far finds under 8 % of the second kind in its top 20. That column is what a
new signal has to move. Tune any weight on `SPLIT=dev`, report on `SPLIT=test`.

## 8. Adding a source

1. An entry in `data/manifest.json`: URL, target under `data/raw/`, SHA-256, licence (or
   `"manual": true` with steps).
2. A reader under `src/QuranKG.Dataset/Sources/`, joined in `CorpusLoader`, with a test for every
   cleaning rule it needs.
3. `just check-data` must still add up; then the connector (`Schema.cs` / `Edges.cs` /
   `Ingest.cs`), `just ingest --dry-run`, `just emit-schema`, a renderer for any new node type.
4. Its licence and what it gives in `docs/data-sources.md`.
