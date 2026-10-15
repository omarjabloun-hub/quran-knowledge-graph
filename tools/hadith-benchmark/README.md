# Hadith benchmark

How well `qkg/hadith/profile` ranks the verse a hadith is about, when the hadith does not quote it.

```bash
python3 tools/hadith-benchmark/evaluate.py      # against the local workspace (.env)
```

**The reference.** The English translators of Bukhari (Muhsin Khan) and Muslim (Siddiqui) cite
verses in brackets — "(15.90)", "(xv. 90)". Where the hadith's Arabic does not quote the cited verse
(`QuranQuote.Kind == "translator"`), the quotation detector cannot have found it and shared words
cannot simply find the quotation: the "closest verses" ranking has to. The translators' citations
are never read by the ranking or the detector.

**Result** (85 hadith, October 2026, AraModernBert-STS for both the hadith and the verses):

| semantic | lexical | cited verse in the top 10 | MRR@10 |
|---|---|---|---|
| 1 | 0 | 18.8% | 0.103 |
| 0 | 1 | 22.4% | 0.116 |
| **1** | **1** | **28.2%** | **0.151** |
| 1 | 0.5 | 31.8% | 0.149 |
| 1 | 0.25 | 32.9% | 0.145 |

Chance is 10 in 6,236 (0.16%). The endpoint uses 1 / 1, the best MRR; lighter word weights put the
verse in the top 10 slightly more often but lower. A small reference: read it as a direction, not a
decimal.

The same reference measures the quotation detector itself (the corpus loader reports it): the
Arabic quotes a verse the translator cites in 94% of Bukhari's cited hadith and 82% of Muslim's.
