# The graph model

Sixteen node types, written by [`src/QuranKG.Connector`](../src/QuranKG.Connector) from the sources in
[data-sources.md](data-sources.md). The connector's schema (`Schema.cs`, `Edges.cs`) is the
contract for everything above it: `just emit-schema` generates the `N`/`E` constants the front end
uses, and the workspace injects the same constants into every endpoint.

```mermaid
flowchart LR
    Revelation((Revelation<br/>Meccan · Medinan))
    Surah[Surah<br/>114] -- HasVerse --> Verse[Verse<br/>6,236]
    Surah -- SurahInJuz --> Juz[Juz<br/>30]
    Surah -- SurahRevealedIn --> Revelation
    Verse -- InJuz --> Juz
    Verse -- RevealedIn --> Revelation
    Verse -- HasWord --> Word[Word<br/>77,429]
    Surah -- SurahHasWord --> Word
    Word -- HasRoot --> Root[Root<br/>1,651]
    Word -- HasLemma --> Lemma[Lemma<br/>4,776]
    Lemma -- LemmaHasRoot --> Root
    Verse -- VerseHasRoot --> Root
    Verse -- VerseHasLemma --> Lemma
    Verse -- HasTopic / InTopicBranch --> Topic[Topic<br/>QUL]
    Topic -- ParentTopic --> Topic
    Verse -- InQurSimPair --> QurSimPair[QurSimPair<br/>7,295 · answer key]
    Verse -- HasTafsirEntry --> TafsirEntry[TafsirEntry<br/>28,486]
    TafsirEntry -- EntryOfTafsir --> Tafsir[Tafsir<br/>6 editions]
    Hadith[Hadith<br/>14,940] -- HasQuranQuote --> QuranQuote[QuranQuote<br/>2,385]
    QuranQuote -- QuoteOfVerse --> Verse
    Hadith -- HadithQuotesVerse --> Verse
    Hadith -- InHadithBook --> HadithBook[HadithBook<br/>154]
    HadithBook -- BookOfCollection --> HadithCollection[HadithCollection<br/>Bukhari · Muslim]
    Passage[Cluster: Passage<br/>561] -- HasMember --> Verse
    Passage -- ClusterOfSurah --> Surah
    Theme[Cluster: Theme<br/>119] -- HasPassage --> Passage
```

Every edge has a reverse (`InSurah`, `RootOfWord`, `LemmaInVerse`, …) with its own name, so a
traversal reads in the direction it goes.

| Node | Key | What it holds |
|---|---|---|
| **Surah** | `1`…`114` | names (Arabic, scholarly, Latin, English), revelation place and order, verse and ruku' counts |
| **Verse** | `surah:ayah` | **the Arabic** — Uthmani (display) and Simple-Clean (search, embeddings) — then transliteration, two English glosses, and its place: juz, hizb, rub', manzil, ruku', page, sajda |
| **Word** | `surah:ayah:position` | one occurrence: the form with and without diacritics, its segments and their tags, part of speech, features, verb form, root and lemma |
| **Root** | its letters (`رحم`) | the family a word is built on; `Spaced` (`ر ح م`) for display |
| **Lemma** | `lemma|root|tag` | a dictionary headword (13 spellings belong to two different words, hence the composite key) |
| **Juz** | `1`…`30` | the thirtieths, with the verse each starts at |
| **Revelation** | `Meccan` / `Medinan` | where a surah was revealed |
| **Topic** | QUL id | a theme, with its parent; verses link to their topic and every ancestor of it |
| **QurSimPair** | `a~b` | two verses Ibn Kathir relates, with the degree (0–2). **The answer key** |
| **Tafsir** | `mukhtasar-ar` … | a tafsir in one language — al-Mukhtasar, al-Jalalayn, Ibn Kathir, each in Arabic and English: names, author, attribution, `Rank` (the order a reader is offered them) |
| **TafsirEntry** | `ibn-kathir-ar:2:1-5` | one commentary, on a verse or on the run of verses it explains together (Ibn Kathir: up to 20); linked to **every** verse it covers. For people only: Ibn Kathir's text is what QurSim was drawn from |
| **HadithCollection** | `bukhari`, `muslim` | the collection: names, compiler, English translator |
| **HadithBook** | `bukhari:65` | a book (kitab), its English and Arabic names |
| **Hadith** | `bukhari:6018`, `muslim:93` | one hadith: `Reference` as cited (Bukhari 6018, Muslim 8a), the Arabic with its chain, `Matn` (the text without the chain — a heuristic; what the embeddings and the assistant read), the English, `IsVariant` (only a chain pointing to the hadith before), the translator's bracketed citations, `Keywords` (its distinctive words against the other hadith) |
| **QuranQuote** | `bukhari:4500>15:90` | where a hadith meets a verse: `Kind` quotation (braces, six words or more, a whole short verse), phrase (four to seven unmarked words, or a formula many hadith share) or translator (only the English cites it); the words quoted, the `RunId` of the ingest that found it. Quotations and phrases also link the hadith to the verse directly (`HadithQuotesVerse`) |
| **Cluster** | `P:7:103-126` · `T:{hash}` | written by `qkg/clusters/build` (`just clusters`), two kinds. A **Passage** is a run of consecutive verses of one surah — its sections (`SurahNumber`, `Ordinal`, `FirstVerseKey`, `LastVerseKey`). A **Theme** groups passages of several surahs that tell the same story or treat the same subject. Both carry an Arabic label (distinctive lemmas), an English one (a reading aid), the `Method` and the `RunId`. A third kind, **SimilarGroup** (`S:{hash}`), is written by `qkg/clusters/similar-build`: passages grouped by Arabic meaning, lemmas and roots, with `HasPassage` edges only (a passage does not name its group); `qkg/clusters/build` leaves them alone |

## Why it is shaped this way

- **Arabic first.** The verse's Arabic is its text; translations are reading aids and never a
  similarity signal. Search ignores diacritics and hamza forms, so a reader can type Arabic with or
  without tashkeel.
- **Location at every level.** A word links to its verse *and* its surah; a verse to its surah,
  juz and revelation place. A facet or a shared-neighbour signal only sees direct neighbours, so a
  fact needed at a level is an edge at that level.
- **Roots and lemmas at the verse level too.** `VerseHasRoot` / `VerseHasLemma` repeat the words'
  facts one level up, distinct per verse: "verses that share a rare root" and "verses with root X"
  are then one hop, which is what makes the similarity endpoint and the root pages fast.
- **Words are nodes.** 77,429 of them: every count on the dashboard ("most frequent roots", parts
  of speech, words per verse) is computed live from them, and each count can open what it counted.
- **The answer keys are in the graph but out of the AI's reach** (see data-sources.md).
- **Clusters are written inside the workspace, not by the connector**, because they are derived:
  they depend on the method, and are re-run as it improves — by an admin, or by Sudo, who can read
  and change `qkg/clusters/build`. The key of a passage is its verse range and the key of a theme a
  hash of its passages, so a rebuild that finds the same groups keeps the same nodes, and deletes
  the ones it no longer finds. Their sizes are not stored: every count is read from the edges.
- **Verse → passage → theme, not verse → theme.** A verse is in exactly one passage (`InCluster`)
  and a passage in at most one theme (`InTheme`); the build checks both after every run.
