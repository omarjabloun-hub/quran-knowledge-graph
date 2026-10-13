# Embedding benchmark — which model reads Quranic Arabic best?

The similarity endpoint (`qkg/verse/similar`) combines three signals read from the Arabic: an
embedding of the verse's plain text, shared lemmas and shared roots. This tool measures the
embedding models against a reference made by people, so the choice of model and the weights are
decisions, not guesses.

**The reference is QurSim** (Sharaf & Atwell, 2012): 7,295 pairs of verses that Ibn Kathir's
tafsir relates, each graded 2 (strongly related), 1 (related) or 0 (relation not obvious). It is
the *answer key*: no signal reads it, and no AI tool may reach it.

## Run it

```bash
just fetch                      # the corpus, once
just benchmark                  # every candidate not done yet, then the table → out/results.md
just benchmark --only qwen3     # a subset (regex on the model name)
just benchmark --skip-hf        # only the models pulled into Ollama (ollama pull <model> first)
just benchmark --weights        # then each model's W_SEM: chosen on the dev half, scored on the test half
just benchmark --weights --only "AraModernBert|qwen3-embedding-8b"     # the same, for two of them
```

The first run creates `.venv/` with the first working Python 3.11+ it finds (`python3.12`,
`python3.13`, `python3.11`, `python3`, then uv's — `uv python install 3.12`; Homebrew's Python on
macOS 26.2 cannot run pip, its `pyexpat` needing a newer system libexpat) and installs
`requirements.txt`. On a Mac, sentence-transformers
runs on the GPU (MPS); Ollama models run in Ollama. Everything written goes under `out/`
(gitignored): `verses.jsonl` and `gold.json` from `prep.py`, one `vec/<model>__<field>__<tag>.f32`
per job, and `results.md`.

Add a model by adding a line to [`candidates.tsv`](candidates.tsv). Models load with
`trust_remote_code=False`: a repository that needs its own code is skipped, not run.

## What the numbers mean

For each of the 3,943 verses QurSim relates to at least one other, all 6,235 other verses are
ranked:

| Column | Meaning |
|---|---|
| R@10 / R@20 | share of the verse's related verses in its top 10 / 20 |
| MRR | 1 / the rank of the first related verse |
| strong | the same over the 2,578 verses with a degree-2 partner |
| R@20 shared-lemma / no-shared-lemma | R@20 split by whether the pair shares a lemma |

**Read the last column first.** 9,976 of the related pairs share a lemma and 2,888 share none. The
lexical signals already find the first kind; an embedding earns its cost only by finding the
second — verses that say the same thing in different words.

## Results so far

Plain Arabic (`arabic_plain`, Tanzil Simple-Clean — what the workspace embeds): 24 embeddings,
measured on an Apple M4 Max (64 GB) on 2026-10-13 — the models of the first round, measured on
the 4-core cloud machine this repository was built on, gave identical numbers. **Fusion weights are
chosen on the dev half of the queries and reported on the test half** (`SPLIT=dev` / `SPLIT=test`,
a stable hash of the verse key; `just benchmark --weights` does both), so a tuned row is not scored
on what tuned it.

### Fused the way `qkg/verse/similar` fuses (test half, 1,993 queries)

`embedding × W_SEM + shared lemmas × 1 + shared roots × 0.5`, each signal z-scored; W_SEM chosen
among 0.25, 0.5, 1, 1.5 and 2 on the dev half:

| Ranking | Runs | W_SEM | R@10 | R@20 | MRR | strong R@10 |
|---|---|---|---|---|---|---|
| **NAMAA-Space/AraModernBert-Base-STS** | MPS, 71 s | 1.0 | **20.2** | **25.9** | **0.226** | **34.5** |
| Omartificial/Arabic-Triplet-Matryoshka-V2 | MPS, 61 s | 2.0 | 19.3 | 24.6 | 0.216 | 33.0 |
| Omartificial/Marbert-all-nli-triplet-Matryoshka | MPS | 1.0 | 19.3 | 24.3 | 0.214 | 32.5 |
| CAMeL-Lab/bert-base-arabic-camelbert-ca (classical Arabic, no similarity training) | MPS | 1.0 | 19.2 | 24.5 | 0.216 | 32.4 |
| Omartificial/GATE-AraBert-v1 | MPS, 60 s | 1.0 | 19.1 | 23.7 | 0.218 | 32.5 |
| **qwen3-embedding:8b — the best through Ollama** | Ollama, 1,054 s | 0.5 | 19.0 | 24.0 | 0.220 | 32.4 |
| Omartificial/Arabert-all-nli-triplet-Matryoshka | MPS | 1.0 | 18.8 | 24.7 | 0.212 | 32.0 |
| Qwen/Qwen3-Embedding-4B (STS instruction) | MPS fp32, 1,017 s | 1.0 | 18.7 | 23.4 | 0.214 | 32.2 |
| silma-ai/silma-embedding-matryoshka-v0.1 | MPS | 1.5 | 18.4 | 24.1 | 0.211 | 31.1 |
| intfloat/multilingual-e5-large-instruct (STS instruction) | MPS | 0.5 | 18.2 | 22.5 | 0.208 | 30.9 |
| qwen3-embedding:4b | Ollama, 526 s | 1.0 | 18.1 | 23.1 | 0.199 | 30.9 |
| Snowflake/snowflake-arctic-embed-l-v2.0 | MPS | 1.0 | 18.0 | 23.0 | 0.205 | 30.6 |
| shared lemmas alone | — | — | 17.7 | 22.4 | 0.210 | 28.2 |
| Omartificial/Harrier-Arabic-Matryoshka-0.6B (STS prompt) | MPS | 0.25 | 17.7 | 21.5 | 0.202 | 30.0 |
| Qwen/Qwen3-Embedding-0.6B (STS instruction) | MPS | 0.5 | 17.7 | 22.0 | 0.200 | 30.1 |
| Omartificial/Harrier-Arabic-Matryoshka-0.6B | MPS | 0.25 | 17.6 | 21.2 | 0.206 | 29.8 |
| embeddinggemma | Ollama, 66 s | 1.0 | 17.5 | 22.2 | 0.197 | 29.8 |
| microsoft/harrier-oss-v1-0.6b | MPS | 0.25 | 17.5 | 21.8 | 0.203 | 29.5 |
| **microsoft/harrier-oss-v1-270m — built in, the endpoint today** | workspace CPU | 0.25 | 17.5 | 21.8 | 0.204 | 29.4 |
| bge-m3 (Ollama, and BAAI/bge-m3 on MPS: the same scores) | Ollama, 105 s | 1.0 | 17.4 | 22.7 | 0.200 | 29.7 |
| Omartificial/Harrier-Arabic-Matryoshka-270m | MPS | 0.25 | 17.4 | 21.6 | 0.205 | 29.7 |
| intfloat/multilingual-e5-large ("query: ") | MPS | 0.5 | 17.4 | 21.5 | 0.205 | 29.4 |
| sentence-transformers/LaBSE | MPS | 0.25 | 17.3 | 21.2 | 0.204 | 29.1 |
| Omartificial/Arabic-labse-Matryoshka | MPS | 0.25 | 17.2 | 21.3 | 0.201 | 28.9 |
| shared lemmas + 0.5 roots (no embedding) | — | — | 16.9 | 22.0 | 0.202 | 28.0 |

Arabic-Triplet-V2 peaked at the top of the grid; on the dev half 2.5 and 3.0 scored lower (18.8,
18.4 against 19.0). Times are for the 6,236 verses on the M4 Max (with another model embedding at
the same time); the first run of a model also downloads it.

### Single signals (all 3,943 queries)

| Method | R@10 | R@20 | MRR | strong R@10 | R@20 no shared lemma |
|---|---|---|---|---|---|
| shared roots (graph signal) | 17.0 | 22.0 | 0.205 | 26.4 | 5.1 |
| shared lemmas (graph signal) | 17.0 | 21.7 | 0.207 | 27.1 | 2.3 |
| shared words | 16.6 | 20.3 | 0.203 | 24.1 | 7.3 |
| NAMAA-Space/AraModernBert-Base-STS | 16.5 | 21.2 | 0.193 | 28.3 | 2.7 |
| tf-idf lemmas | 15.0 | 19.9 | 0.177 | 25.4 | 2.3 |
| Omartificial/Arabic-Triplet-Matryoshka-V2 | 14.9 | 19.6 | 0.179 | 26.1 | 2.3 |
| qwen3-embedding:8b (Ollama) | 14.5 | 18.7 | 0.180 | 25.2 | 1.0 |
| Omartificial/GATE-AraBert-v1 | 13.8 | 18.1 | 0.169 | 23.9 | 2.3 |
| Qwen/Qwen3-Embedding-4B | 13.7 | 18.3 | 0.174 | 24.5 | 1.9 |
| Omartificial/Arabert-all-nli-triplet-Matryoshka | 13.7 | 18.2 | 0.163 | 23.4 | 2.2 |
| silma-ai/silma-embedding-matryoshka-v0.1 | 13.3 | 17.3 | 0.166 | 22.7 | 1.8 |
| Omartificial/Marbert-all-nli-triplet-Matryoshka | 13.1 | 17.4 | 0.161 | 22.7 | 2.3 |
| qwen3-embedding:4b (Ollama) | 13.0 | 17.1 | 0.157 | 22.6 | 0.8 |
| intfloat/multilingual-e5-large-instruct | 12.5 | 16.7 | 0.160 | 22.0 | 1.2 |
| Snowflake/snowflake-arctic-embed-l-v2.0 | 12.4 | 16.3 | 0.155 | 21.6 | 1.0 |
| bge-m3 (Ollama / MPS) | 11.4 | 15.4 | 0.150 | 19.7 | 1.0 |
| embeddinggemma (Ollama) | 11.2 | 14.5 | 0.142 | 19.1 | 1.0 |
| CAMeL-Lab/bert-base-arabic-camelbert-ca | 11.0 | 14.7 | 0.145 | 18.2 | 2.2 |
| EmbeddingGemma2 (workspace built-in, first round) | 10.4 | 14.0 | 0.138 | 18.7 | 0.7 |
| Omartificial/Harrier-Arabic-Matryoshka-0.6B | 10.3 | 13.8 | 0.136 | 18.4 | 0.8 |
| intfloat/multilingual-e5-large | 10.2 | 13.4 | 0.131 | 17.6 | 1.3 |
| Omartificial/Harrier-Arabic-Matryoshka-270m | 9.5 | 13.0 | 0.129 | 16.9 | 1.0 |
| microsoft/harrier-oss-v1-0.6b | 9.4 | 12.6 | 0.132 | 16.9 | 1.0 |
| **Harrier 270M (workspace built-in, the default)** | **9.2** | 11.9 | 0.126 | 16.2 | 1.0 |
| Qwen/Qwen3-Embedding-0.6B | 9.1 | 12.2 | 0.122 | 16.5 | 0.9 |
| sentence-transformers/LaBSE | 8.9 | 12.3 | 0.124 | 15.5 | 0.4 |
| Omartificial/Arabic-labse-Matryoshka | 8.6 | 11.4 | 0.118 | 14.9 | 1.0 |

### What this says

- **AraModernBert-STS stays the best embedding for Quranic Arabic**, and by a margin: fused with the
  morphology, +2.7 points of top-10 recall over today's endpoint (17.5 → 20.2) and +5.1 on the
  strongly related pairs (29.4 → 34.5). A base-size Arabic model beats every multilingual one, up
  to 8B parameters.
- **The best model Ollama can serve is `qwen3-embedding:8b`**, at W_SEM 0.5: 19.0 / 32.4, 1.2 points
  behind AraModernBert, and fifteen times slower to embed. Its 4B sibling (18.1) and bge-m3 (17.4)
  add little; embeddinggemma (17.5) no more than Harrier.
- **Arabic pretraining matters more than the similarity fine-tune.** The Arabic BERT family fused
  lands at 18.8–19.3 whatever it was tuned on (NLI triplets, STS); CAMeLBERT-CA, pretrained on
  classical Arabic and never trained for similarity, reaches 19.2.
- **Fine-tuning Harrier on Arabic does not fix it.** Omartificial's Arabic Harrier models
  (270M, 0.6B, either prompt) score like Harrier itself (17.4–17.7), and so do LaBSE and its Arabic
  fine-tune: their best weight is the smallest tried, i.e. they are tie-breakers at most.
- **Two embeddings are not better than one.** On the dev half, AraModernBert plus a second model
  (qwen3-embedding:8b, Arabic-Triplet-V2 or CAMeLBERT-CA) gained at most 0.3 points of R@10 —
  noise, for a second index on a second field. Each verse's neighbours (`ctx:`) moved it by ±0.3
  (in place of the verse, or beside it): noise too.
- **The morphology is still most of the answer, and nothing finds the pairs that share no lemma.**
  The best single embedding puts 2.7 % of those pairs in its top 20, below plain shared words
  (7.3 %); fused, they stay out (≈0 %), because the lexical z-scores dominate the bottom of the
  ranking. The embedding re-orders the verses the morphology already found.

### In the workspace

AraModernBert-STS embeds the verses' Arabic, served on the Mac by
[`tools/embedding-server`](../embedding-server/README.md) (`just embedding-server start &&
just embeddings-arabic`), with `W_SEM = 1.0` in `qkg/verse/similar`. The workspace's ranking is
the fusion measured here: on five verses, the same top 20 in the same order. The alternative that
needs no server of our own is `qwen3-embedding:8b` through Ollama (`just embeddings-ollama`,
`W_SEM = 0.5`: 19.0 / 32.4).

Not benchmarked, on purpose: `Mathani-Ayat/rattil-ayah-embed`, which is fine-tuned on QurSim —
scoring it against QurSim would be circular.
