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
```

The first run creates `.venv/` and installs `requirements.txt`. On a Mac, sentence-transformers
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

Plain Arabic (`arabic_plain`, Tanzil Simple-Clean — what the workspace embeds), measured on the
4-core cloud machine this repository was built on. **Fusion weights are chosen on the dev half of
the queries and reported on the test half** (`SPLIT=dev` / `SPLIT=test`, a stable hash of the
verse key), so a tuned row is not scored on what tuned it.

### Single signals (all 3,943 queries)

| Method | R@10 | R@20 | MRR | strong R@10 | R@20 no shared lemma |
|---|---|---|---|---|---|
| shared roots (graph signal) | 17.0 | 22.0 | 0.205 | 26.4 | 5.1 |
| shared lemmas (graph signal) | 17.0 | 21.7 | 0.207 | 27.1 | 2.3 |
| shared words | 16.6 | 20.3 | 0.203 | 24.1 | 7.3 |
| NAMAA-Space/AraModernBert-Base-STS | 16.5 | 21.2 | 0.193 | 28.3 | 2.7 |
| tf-idf lemmas | 15.0 | 19.9 | 0.177 | 25.4 | 2.3 |
| Omartificial/Arabic-Triplet-Matryoshka-V2 | 14.9 | 19.6 | 0.179 | 26.1 | 2.3 |
| Omartificial/GATE-AraBert-v1 | 13.8 | 18.1 | 0.169 | 23.9 | 2.3 |
| silma-ai/silma-embedding-matryoshka-v0.1 | 13.3 | 17.3 | 0.166 | 22.7 | 1.8 |
| EmbeddingGemma2 (workspace built-in) | 10.4 | 14.0 | 0.138 | 18.7 | 0.7 |
| **Harrier 270M (workspace built-in, the default)** | **9.2** | 12.0 | 0.126 | 16.3 | 1.0 |
| qwen3-embedding:8b / 4b, bge-m3 (Ollama), Qwen3-Embedding (MPS) | *for the Mac* | | | | |

### Fused the way `qkg/verse/similar` fuses (test half, 1,993 queries)

`embedding × W_SEM + shared lemmas × 1 + shared roots × 0.5`, each signal z-scored:

| Ranking | R@10 | R@20 | MRR | strong R@10 |
|---|---|---|---|---|
| AraModernBert-STS, W_SEM 1.0 | **20.2** | **25.9** | **0.226** | **34.5** |
| shared lemmas alone | 17.7 | 22.4 | 0.210 | 28.2 |
| **Harrier, W_SEM 0.25 — the endpoint's setting** | 17.5 | 21.8 | 0.204 | 29.4 |
| shared lemmas + 0.5 roots | 16.9 | 22.0 | 0.202 | 28.0 |
| Harrier, W_SEM 1.0 — the first setting | 16.6 | 21.2 | 0.192 | 28.3 |

On the dev half, Harrier's weight was best at 0.25 (0.1: 16.1, 0.25: 16.6, 0.5: 16.4 R@10) and
AraModernBert's at 1.0 (0.5: 18.7, 1.0: 19.2, 1.5: 19.1).

### What this says

- **The built-in embeddings do not read Quranic Arabic well.** Harrier alone ranks last; at full
  weight it made the fused ranking *worse* than lemmas alone. Down-weighted to 0.25 it is a
  tie-breaker: lemma-level recall, a little better on the strongly related pairs.
- **The Quranic Arabic Corpus morphology is a very strong baseline.** A shared rare lemma is most
  of what makes two verses related in Ibn Kathir's eyes.
- **A good Arabic embedding does add to it.** AraModernBert-STS fused with the morphology gains
  +2.5 points of top-10 recall and +6 on the strongly related pairs over lemmas alone. That is the
  bar for the models the Mac can run.
- **Nothing finds the pairs that share no lemma yet** (the last column). That is the open
  question for the large multilingual models.

### On the Mac

```bash
just models-pull                    # qwen3-embedding:8b and the chat model
ollama pull qwen3-embedding:4b && ollama pull bge-m3
just benchmark --skip-hf            # the Ollama models, exactly as the workspace calls them
just benchmark --only "Qwen3|harrier-oss-v1-0.6b|e5"     # the larger sentence-transformers models (MPS)
SPLIT=dev  just benchmark --evaluate-only "q8=qwen3-embedding-8b__arabic_plain__ollama:1,shared_lemmas:1,shared_roots:0.5"
SPLIT=test just benchmark --evaluate-only "q8=…"         # report the weight the dev half chose
```

If a model beats the AraModernBert row, `just embeddings-ollama` (with `OLLAMA_EMBEDDING_MODEL`
set to it) and put its weight in `W_SEM` (`code/endpoints/qkg/verse/similar.cs`). If
AraModernBert stays best, serve it with any OpenAI-compatible embeddings server and point
`OLLAMA_URL` at it — the workspace calls `<OLLAMA_URL>/v1/embeddings`, whoever answers.

Not benchmarked, on purpose: `Mathani-Ayat/rattil-ayah-embed`, which is fine-tuned on QurSim —
scoring it against QurSim would be circular.
