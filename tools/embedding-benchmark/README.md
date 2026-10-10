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
4-core cloud machine this repository was built on. Rows marked *pending* are for the Mac.

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
| Harrier 270M (workspace built-in, the default) | *pending* | | | | |
| qwen3-embedding:8b / 4b, bge-m3 (Ollama) | *pending* | | | | |

What this says so far:

- **No model beats the graph signals yet.** The Quranic Arabic Corpus morphology is a very strong
  baseline: a shared rare lemma is most of what makes two verses related in Ibn Kathir's eyes.
- **The modern-standard-Arabic models do not read Quranic text well**: the best of them
  (AraModernBert-STS) only matches the lemma signal, and none finds the pairs that share no lemma.
- **The open question for the Mac** is whether the large multilingual models (Qwen3-Embedding
  8B/4B) find those 2,888 pairs. If one does, it goes into `qkg/verse/similar` as the semantic
  signal — via `just embeddings-ollama` — and the fused ranking is measured here first:

```bash
just benchmark --evaluate-only fused=qwen3-embedding-8b__arabic_plain__ollama:1,shared_lemmas:1,shared_roots:0.5
```

Not benchmarked, on purpose: `Mathani-Ayat/rattil-ayah-embed`, which is fine-tuned on QurSim —
scoring it against QurSim would be circular.
