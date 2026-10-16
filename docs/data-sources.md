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
| **Al-Tahrir wa al-Tanwir** by Muhammad al-Tahir ibn Ashur (Tunis, d. 1973), Arabic — QUL resource 25 | the main tafsir, the first one the verse panel opens: language, rhetoric and the order of each passage, the purposes of the law, earlier commentators weighed. About 78 MB, every verse explained (3,923 entries) | published by al-Dar al-Tunisiya lil-Nashr (1984); QUL's terms for it not yet checked — attribute it, and check before any public or commercial use |
| **Tafsir al-Mukhtasar** (Tafsir Center for Quranic Studies), Arabic and its English translation — QUL resources 251 / 266 | one short explanation per verse, the same work in both languages | free to use with attribution, text unaltered |
| **Tafsir al-Jalalayn** (al-Mahalli, al-Suyuti), Arabic and Feras Hamza's English — QUL 523 / 573 | a classical gloss of the verse's words, with grammar and readings | Arabic: public domain. English: © Royal Aal al-Bayt Institute (altafsir.com) — non-commercial, with attribution |
| **Tafsir Ibn Kathir**, Arabic and the abridged English (Darussalam) — QUL 22 / 35 | the classical tafsir by narration, passage by passage. **QurSim was drawn from it**, so it is for people only | Arabic: public domain. English: © Darussalam — non-commercial, with attribution |
| **Sahih al-Bukhari** and **Sahih Muslim**, Arabic and English ([fawazahmed0/hadith-api](https://github.com/fawazahmed0/hadith-api), pinned to a commit) | 14,940 hadith with their standard numbers (Bukhari 6018; Muslim's Abdul Baqi number, 8a) and books; the English of Muhsin Khan (Bukhari) and Abdul Hamid Siddiqui (Muslim) | hadith-api: Unlicense. The Arabic is in the public domain; the translations are their translators' and publishers' — non-commercial use, with attribution |
| **The books' Arabic names** ([AhmedBaset/hadith-json](https://github.com/AhmedBaset/hadith-json) v1.2.0, from sunnah.com) | كتاب الإيمان … for the 154 books (only the names are read; its hadith numbering is not the standard one) | sunnah.com content |
| **Sheikh Mohamed (Hamadi) Latrech's recitation**, Qalun from Nafi' ([qaloun.app](https://qaloun.app/en/reciters/mohamed-latrech)) | each surah's recording (streamed from the site's host, never copied) and where each of its verses starts — the fetcher keeps only those marks from each page | qaloun.app: sharing and quoting with attribution, no commercial use without permission — ask before a public deployment |
| **The Qalun text** (King Fahd Glorious Quran Printing Complex, v10, via [thetruetruth/quran-data-kfgqpc](https://github.com/thetruetruth/quran-data-kfgqpc), pinned) | the Quran in Qalun's reading — **the main text of the reader** — with its own verse numbering (6,214, the Madani count) and the pages, juz and lines of its mushaf; its words are linked one by one to the corpus's (Hafs) words, and its verse marks carry a Qalun recording over to Hafs's verses. Its font (qaloon.10.woff2, same commit) is loaded from jsDelivr | KFGQPC: free to use, text unaltered |
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
- Tafsir: the seven editions come from QUL through [spa5k/tafsir_api](https://github.com/spa5k/tafsir_api),
  one file per surah, pinned to a commit (the manifest's `parts`: the checksum covers the 114 files
  in order). A commentary repeated over consecutive verses is one entry over that range. The editor's
  manuscript notes in Ibn Kathir's Arabic (`[[في م، أ: …]]`) are removed, and so are the printed
  edition's 19,823 page numbers in Ibn Ashur's («صفحة ١٥٢» on a line of its own): about half fall
  mid-sentence, whose two halves are joined; after a sentence's end the page turn stays a paragraph
  break. Al-Jalalayn's Arabic passes
  over 226 verses it holds need no explanation; they have no entry, which the loader reports.

- Hadith: an entry with no text (Muslim's introduction, 203) is skipped; the 306 of Bukhari's that
  come without their book are put in the book whose number range holds them. `Matn` is the
  Arabic after the last chain marker (حدثنا، أخبرنا، عن …) before the first quotation mark or
  mention of the Prophet — a heuristic, right for most; 1,175 entries that only repeat a chain
  ("بهذا الإسناد مثله") are marked `IsVariant` and left out of "closest" lists.
- The verses a hadith quotes (`Text/QuranQuotes.cs`): both texts in one spelling; a run the
  edition sets in braces `{…}` is a quotation (down to two words); otherwise four words or more,
  not a formula (in more than three verses, or more than 25 hadith, or naming the Prophet), not
  inside a longer run; six or more, or a whole verse, is a quotation unless it recurs in more than
  10 hadith (then a phrase). Measured against the translators' bracketed citations, which the
  detector never reads: 94% of Bukhari's cited hadith quote the cited verse, 82% of Muslim's.

- A recitation (`Sources/Recitation.cs`): a Qalun recording counts its verses the Madani way —
  Al-Fatiha without the Bismillah, «الم» not a verse of its own, Al-Baqarah 285 verses — so its
  marks are not the corpus's verses. The Qalun text (same numbering) and the corpus's Simple-Clean
  are aligned word by word (longest shared runs, a vocative joined to its word, an unmatched first
  word placed at the most alike); each recorded verse's start becomes a word of the corpus. A
  corpus verse starting there takes the recording's time (6,156); one starting inside a recorded
  verse is placed by the letters recited before it (80 — `Recitation.EstimatedAyahs`). Checked on
  the audio: the quietest moment of the recitation falls about half a second after each mark —
  the pause between two verses.
- Qalun's verses (`RiwayaSource`): each of the 77,430 words of the Qalun text is linked to the
  corpus's word it is — the longest runs the two texts share first, then the words between two runs
  paired in order by spelling (351 of them: «مَلِكِ»/«مَٰلِكِ», «نَحْشُرُهُمْ»/«يَحْشُرُهُمْ»). 8 stay
  unlinked, where the readings part («أَوْ أَمِنَ», two words, for Hafs's «أَوَأَمِنَ»; «ءَالِ يَاسِينَ»
  for «إِلْ يَاسِينَ»). Every Hafs verse is in a Qalun verse but Al-Fatiha's Bismillah, no verse in Qalun.
  The verse numbers ending each line of the text are dropped; a verse over two pages keeps its first.

## Answer keys stay out of the AI's reach

QurSim is how the similarity ranking is measured, and the QUL topics are how clusters will be. Both
are in the graph so people can read them and so scores can be computed. The tafsirs are in the same
position: QurSim was drawn from Ibn Kathir's, so `Tafsir` and `TafsirEntry` have no consult reader,
Search Records cannot reach them and `qkg/verse/tafsir` is no AI tool — but no similarity signal,
no AI tool, no index the AI can query and no document handed to a model may read them. The only
endpoint that returns QurSim pairs (`qkg/verse/words`, for the verse page) must never be exposed as
an AI tool.

## Worth adding next

| Dataset | Would add | Notes |
|---|---|---|
| More tafsirs — al-Sa'di, al-Tabari, al-Qurtubi, al-Muyassar, Ibn Atiyya (QUL, Arabic) | more voices on a verse | the same `parts` source in the manifest and one line in `TafsirSource.Editions` |
| QUL word-by-word translations | an English gloss per word in the word-by-word view | QUL licence per resource |
| Asbab al-nuzul (occasions of revelation) | why a verse was revealed — a link from verses to events and people | needs a clean digital edition |
| Named entities (prophets, places, peoples) — the Quranic Arabic Corpus ontology | `Person` / `Place` nodes linked to verses: the backbone of the stories | the corpus ontology covers ~300 concepts |
| Recitation timings (Quran.com / EveryAyah) | play a verse in a chosen reciter | audio is large: link, do not ingest |
| More hadith collections (Abu Dawud, al-Tirmidhi, al-Nasa'i, Ibn Majah — hadith-api) | the other four of the six books, with their gradings | `HadithSource.Collections` and the manifest; their grades (sahih, hasan, da'if) become a field |
