# Embeddings server — AraModernBert-STS for the workspace

The workspace embeds each verse's Arabic (`Verse.TextSimple`) for *Similar verses* and semantic
search. The embedding benchmark ([`tools/embedding-benchmark`](../embedding-benchmark/README.md))
found **`NAMAA-Space/AraModernBert-Base-STS`** reads Quranic Arabic best of 24 models: fused with
the morphology, top-10 recall 20.2 and 34.5 on the strongly related pairs, against 17.5 / 29.4 for
the workspace's built-in Harrier and 19.0 / 32.4 for the best model Ollama runs.

Ollama cannot run a sentence-transformers model, and the workspace's external embeddings index
speaks the OpenAI embeddings API. `server.py` is the bridge: one model, on the Mac's GPU (MPS),
behind `POST /v1/embeddings`.

## Run it

```bash
just embedding-server start      # creates .venv the first time, starts it in the background, waits for /health
just embeddings-arabic           # point Verse.TextSimple at it (deletes the other index; re-embeds the verses)
just status                      # Verse.TextSimple   External   6236 embedded
just embedding-server status     # or: stop
```

`just up` starts it too when `.env` says `EMBEDDING_PROVIDER=arabic`, so a reboot needs only
`just up`. Its log is `server.log`, its PID `server.pid` (both gitignored).

| `.env` | Default | |
|---|---|---|
| `EMBEDDING_SERVER_MODEL` | `NAMAA-Space/AraModernBert-Base-STS` | the model served, and named in the index |
| `EMBEDDING_SERVER_PORT` | `8765` | where it listens on the Mac (127.0.0.1 only) |
| `EMBEDDING_SERVER_URL` | `http://host.docker.internal:8765` | where the workspace container reaches it |

## What it guarantees

- **The vectors are the benchmark's.** No prefix, at most 512 tokens, L2-normalised, the same
  model revision: a verse embedded by the server has cosine 1.000 with the benchmark's vector, and
  `qkg/verse/similar` ranks exactly as the benchmark's fusion did (checked on five verses: the
  same top 20, in the same order).
- **It must be up when the workspace embeds.** Every verse the workspace (re-)embeds and every
  semantic search query goes through it. *Similar verses* reads the stored vectors and does not.
- **Local only.** It binds 127.0.0.1; Docker Desktop forwards `host.docker.internal` there. A
  production workspace needs the model served where it can reach it.

The API: `POST /v1/embeddings` with `{"input": "…" | ["…", …], "encoding_format": "float" | "base64"}`,
`GET /v1/models`, `GET /health`. Changing `EMBEDDING_SERVER_MODEL` changes what the workspace
ranks with: benchmark the model first, run `just embeddings-arabic` again (the index is rebuilt
only when the provider changes — `just embeddings-harrier` then `just embeddings-arabic` forces
it), and set `W_SEM` in `qkg/verse/similar` from that model's own row.
