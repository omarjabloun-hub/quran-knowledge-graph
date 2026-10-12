# Clusters benchmark — are the passages, themes and similar surahs any good?

The clusters build (`qkg/clusters/build`, `just clusters`) cuts every surah into **passages** and
groups passages of different surahs into **themes**; `qkg/surah/similar` ranks **similar surahs**.
All three read only the Arabic morphology — each verse's lemmas and roots. This tool scores them
against references made by people, which none of them read:

| What | Reference | Why it fits |
|---|---|---|
| Passages | the **ruku'** — the 556 traditional sections (Tanzil's `quran-data.xml`) | a human division of every surah into passages of a subject each |
| Themes | **QurSim** — the verse pairs Ibn Kathir's tafsir relates | related verses of different surahs should land in the same theme |
| Similar surahs | QurSim summed over pairs of surahs | surahs Ibn Kathir links often should rank high |

`evaluate.py` is also the reference implementation of the build: the same signals, constants,
dynamic programme and average linkage. A workspace build and this script find the identical 561
passages and 119 themes, which `--from` checks.

## Run it

```bash
just fetch                                   # the corpus, once
just benchmark-clusters                      # recompute and score everything (a few seconds)
just benchmark-clusters --split test         # passages scored on the held-out half of the surahs
just benchmark-clusters --target 180 --tau 0.12   # try other settings before changing the build
dotnet run --project src/QuranKG.Workspace -c Release -- clusters --save build.json
just benchmark-clusters --from build.json    # score what the workspace actually wrote
```

The first run creates `.venv/` (numpy only) and, if it is missing, the verse table with
`tools/embedding-benchmark/prep.py`.

## How the build works

- **Passages.** At every gap between two verses, the ~50 words before it are compared with the
  ~50 words after it: the cosine of their rarity-weighted lemmas plus half the cosine of their
  roots, standardised within the surah (TextTiling, Hearst 1997). A dynamic programme picks the
  cuts that minimise that cohesion plus 8 × the squared relative distance of each passage from 160
  words, less a bonus of 2 where a verse opens the way a section does in the Quran (يا أيها, وإذ,
  واذكر, ولقد, ألم تر, هل أتاك, واتل, كذبت, وإلى).
- **Themes.** Each passage is a tf-idf vector of its lemmas (and of its roots at half weight).
  Average-linkage clustering merges groups while their average cosine is at least 0.10; a group
  spanning two surahs or more is a theme.
- **Labels.** The most distinctive lemmas (up to two proper nouns first), and as a reading aid the
  most distinctive words of the Saheeh International English.

## Results

### Passages against the ruku'

The constants (target length, weight of the length prior, window, opener bonus) were chosen on
the dev half of the surahs (`--split dev`, a stable hash of the surah number); the test half was
scored once.

| Cut (test half: 60 surahs, 269 passages) | Pk ↓ | WindowDiff ↓ | Boundary F1 (±1 verse) |
|---|---|---|---|
| equal-length passages, the same number per surah | 0.399 | 0.400 | 0.367 |
| **vocabulary change — the build** | **0.334** | **0.338** | **0.480** |

On the dev half: 0.400 / 0.405 / 0.358 against 0.341 / 0.347 / 0.478. Over all 114 surahs the build
finds 561 passages to the ruku's 556.

The equal-length cut is a strong baseline, because the ruku' were laid out partly for recitation
and run to similar lengths. Choosing the cut points by greatest depth alone (classic TextTiling,
the number of passages fixed) scored *worse* than it on the dev half (Pk 0.38–0.45); trading
cohesion against a length prior is what beats it. Adding the verse embeddings to the cohesion gave
no reliable gain: Harrier × 0.25 moved Pk from 0.341 to 0.346 on the dev half and from 0.334 to
0.325 on the test half; AraModernBert-STS × 1.0 scored 0.338 on both. The build stays lexical,
which also lets it run before the embeddings index has filled.

### Themes against QurSim

| Themes | Verses in a theme | Related pairs of different surahs kept together | By chance | Times chance |
|---|---|---|---|---|
| 119 | 5,269 of 6,236 | 402 of 6,022 (6.7 %) | 59 | **6.8** |

The threshold τ (the average cosine at which two groups still merge) trades reach for tightness:

| τ | Themes | Verses in a theme | Biggest theme (verses) | Widest theme (surahs) | Times chance |
|---|---|---|---|---|---|
| 0.07 | 64 | 94 % | 677 | 34 | 3.6 |
| 0.08 | 90 | 93 % | 295 | 18 | 5.1 |
| 0.09 | 108 | 88 % | 266 | 16 | 6.4 |
| **0.10** | **119** | **84 %** | **216** | **13** | **6.8** |
| 0.12 | 141 | 75 % | 142 | 9 | 8.4 |
| 0.15 | 133 | 57 % | 142 | 4 | 10.3 |

0.10 was chosen by reading the themes, not by maximising the last column, which only rises as fewer
verses are grouped: below it themes turn into blobs of several hundred verses, above it the stories
the Quran tells in many surahs split into single episodes. A few of the 119:

| Theme | Surahs |
|---|---|
| موسى · فرعون · ساحر — Moses, Pharaoh, the magicians | 7, 10, 20, 26, 27, 28, 40, 43 |
| لوط · مرسل · قوم — Lot and the messengers | 7, 11, 15, 26, 27, 29, 51 |
| مريم · مسيح · ابن — Mary and the Messiah | 2, 3, 4, 5, 9, 33, 61 |
| إبراهيم · يعقوب · حنيف — Abraham, the upright religion | 2, 3, 4, 6, 10, 16, 30 |
| جناح · نساء · معروف — marriage and divorce | 2, 4, 24, 33, 60, 65 |
| منافق · حلف · رسول — the hypocrites | 4, 5, 9, 58, 63 |
| تحت · نهر · خالد — gardens beneath which rivers flow | 13 surahs |

### Similar surahs against QurSim

| Ranking | Spearman | Top-5 related in our top 5 | in our top 10 |
|---|---|---|---|
| random | 0.02 | 4 % | 9 % |
| shared lemmas | 0.40 | 22 % | 34 % |
| shared roots | 0.40 | 21 % | 32 % |
| **lemmas + 0.5 roots — `qkg/surah/similar`** | **0.41** | **24 %** | **34 %** |

Adding the themes two surahs share as a third signal (0.25–1.0) left Spearman at 0.41 and the top
five at 22–24 %, so the endpoint shows shared themes as a reason and does not count them.

## Not done yet

- **Scoring against the QUL topics**, the clustering reference planned from the start: they need a
  manual download (see `data/manifest.json`). Like QurSim, they must stay out of what they score.
- **A second level of themes**: cutting the same tree at 0.15 splits the stories into episodes
  (Moses and the magicians; Moses at the appointed time; the calf).
- **Names from the chat model**, from each theme's key words and opening verses, once a provider
  is configured (`docs/models.md`).
