# Models — what to download and why

Two kinds of model do work here:

| Kind | Used for | Where it runs |
|---|---|---|
| **Embedding** | the meaning of each verse's Arabic — the semantic signal of *Similar verses*, semantic search | built into the workspace (CPU), or Ollama on the Mac's GPU |
| **Chat (LLM)** | the workspace assistant, Sudo (the admin assistant that extends the workspace), naming the themes | Ollama on the Mac, or a hosted provider |

**Much needs no model at all.** The dashboard, the passages and themes (`just clusters`), similar
surahs and the lexical half of similar verses read the Quranic Arabic Corpus morphology — roots and
lemmas — and are there as soon as `just bootstrap` ends. The models add the meaning of each verse
to *Similar verses*, and will name the themes and power the assistant.

Everything is **Arabic first**: the embeddings read `Verse.TextSimple` — Tanzil's Simple-Clean
text, standard spelling without diacritics, which is what the models were trained on (the
Uthmani text with its marks costs ~4× the tokens and changes 17 % of word spellings). The
translations are not a similarity signal.

## Embeddings

### Built in — nothing to install

| Model | Notes |
|---|---|
| **Harrier 270M** (`microsoft/harrier-oss-v1-270m`) | the default for `Verse.TextSimple`, `Verse.EnglishSaheeh` and `Surah.Introduction`. Runs on the CPU inside the container: embedding the 6,236 verses takes a while on a laptop CPU, and is done once. |
| EmbeddingGemma2 | the workspace's other built-in; scored lowest on Quranic Arabic (below) |

### On the Mac, through Ollama — recommended

```bash
ollama pull qwen3-embedding:8b      # then: just embeddings-ollama
```

| Model (`ollama pull …`) | Download | Dimensions | Measured (fused R@10, below) |
|---|---|---|---|
| **`qwen3-embedding:8b`** | 4.7 GB | 4,096 | 19.0 at W_SEM 0.5 — the best through Ollama; the default `OLLAMA_EMBEDDING_MODEL`; ~18 min for the 6,236 verses on an M4 Max |
| `qwen3-embedding:4b` | 2.5 GB | 2,560 | 18.1, twice as fast |
| `bge-m3` | 1.2 GB | 1,024 | 17.4 — no better than Harrier |
| `embeddinggemma` | 0.6 GB | 768 | 17.5 — no better than Harrier |

The workspace calls Ollama's OpenAI-compatible API (`OLLAMA_URL` + `/v1/embeddings`) with the bare
verse — no instruction prefix — so the benchmark measures Ollama models the same way.

**Measured, not guessed.** `just benchmark` scores every candidate against QurSim (Ibn Kathir's
related verses), and `just benchmark --weights` chooses each model's weight on half the queries
and reports it on the other half. 24 embeddings on an M4 Max ([details](../tools/embedding-benchmark/README.md)):

| Ranking (top-10 recall of related verses, held-out queries) | W_SEM | R@10 | strong pairs |
|---|---|---|---|
| shared lemmas + roots, no embedding | — | 16.9 | 28.0 |
| Harrier (built in) + lemmas + roots — **the endpoint today** | 0.25 | 17.5 | 29.4 |
| `bge-m3` (Ollama) + lemmas + roots | 1.0 | 17.4 | 29.7 |
| `qwen3-embedding:4b` (Ollama) + lemmas + roots | 1.0 | 18.1 | 30.9 |
| **`qwen3-embedding:8b` (Ollama) + lemmas + roots** — the best Ollama can serve | 0.5 | 19.0 | 32.4 |
| **AraModernBert-STS + lemmas + roots** — the best of all | 1.0 | **20.2** | **34.5** |

The built-in model adds little to the Quranic Arabic Corpus morphology; a good Arabic embedding
adds a lot. The large multilingual models are not the answer: a base-size Arabic model beats an 8B
one, and the Arabic BERT models — even CAMeLBERT-CA, pretrained on classical Arabic and never
trained for similarity — match `qwen3-embedding:8b` (18.8–19.3 against 19.0). Switch with
`just embeddings-ollama` and set the model's `W_SEM` in `qkg/verse/similar`.

The workspace's external embeddings index speaks the OpenAI API (`<OLLAMA_URL>/v1/embeddings`),
so any compatible server can stand in for Ollama — for a sentence-transformers model such as
AraModernBert-STS, serve it with an OpenAI-compatible embeddings server and point `OLLAMA_URL` at
it.

Not used: `Mathani-Ayat/rattil-ayah-embed`, a Quran-specific model fine-tuned **on QurSim** — the
very reference the similarity work is measured against.

## Chat models

### On the Mac, through Ollama

64 GB of unified memory holds the workspace (16 GB for Docker), an embedding model and one
large chat model at once:

| Model (`ollama pull …`) | Memory (Q4) | Use it for |
|---|---|---|
| **`qwen3:32b`** | ~20 GB | the default `OLLAMA_CHAT_MODEL`: Arabic is among its 119 languages; good at tool use and at short Arabic labels |
| `qwen3:30b` | ~19 GB | Mixture-of-experts (3B active): several times faster, a little weaker |
| `gemma3:27b` | ~17 GB | strong multilingual alternative |
| `qwen3:14b` | ~9 GB | fast fallback |

Arabic-specialised models (Jais, ALLaM 7B, Falcon-Arabic) are worth a try for naming clusters in
Arabic. Their availability in the Ollama library varies; any GGUF build can be imported with a
`Modelfile` (`ollama create`).

Configure one in the workspace: **Manage → AI → Providers → add → Ollama** — Host
`http://host.docker.internal:11434`, Model `qwen3:32b`, Max input context `32768`, Max output
context `4096` (the defaults, 8,192 and 1,024, are too small).

### Sudo, the admin assistant

Sudo builds and extends the workspace from inside it — schema, endpoints, indexes, AI tools —
working on the same definitions this repository holds. It needs a model that is good at long tool
sequences: a local Qwen3 32B manages small changes; for real work add a hosted provider (an
Anthropic or OpenAI key, stored as a workspace secret — never in this repository) and keep the
local model for the user-facing assistant. Whatever Sudo changes, bring it back into git with
`just export-definitions`.

### Rules for every AI feature

- **QurSim is the answer key.** No AI tool, prompt or index may read `QurSimPair` nodes or call
  `qkg/verse/words` (which returns them), or every similarity score becomes a tautology.
- An LLM's account of what a verse means is not tafsir. Anything the assistant says about meaning
  should cite the verses (UIDs) it read.

## Memory budget (64 GB)

| | GB |
|---|---|
| macOS and apps | ~8 |
| Docker VM (the workspace) | 16 |
| `qwen3-embedding:8b` (loaded while embedding; Ollama unloads idle models after 5 minutes) | ~6 |
| `qwen3:32b` + a 32k context | ~24 |
| **Total** | **~54** |
