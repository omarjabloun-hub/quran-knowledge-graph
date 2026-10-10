# Embeds one text field of verses.jsonl with one model, and writes <out>.f32 (+ .json metadata).
#
#   python -I embed.py hf <model_id> <verses.jsonl> <field> <out.f32> [--prefix TEXT]
#       a sentence-transformers model from Hugging Face, on the Apple GPU (MPS) when there is one
#   python -I embed.py ollama <model> <verses.jsonl> <field> <out.f32> [--url http://localhost:11434]
#       a model pulled into Ollama — exactly what the workspace will call once you switch to it
#
# --prefix is prepended to EVERY text: verse-to-verse similarity is symmetric, so an instruction
# model (Qwen3-Embedding, e5-instruct) gets the same instruction on both sides.
# Models are loaded with trust_remote_code=False: a repository that needs custom code is skipped.
import json, os, sys, time, urllib.request

import numpy as np

def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default

kind, model_id, jsonl, field, out = sys.argv[1:6]
prefix = (arg('--prefix', '') or '').replace('\\n', '\n')
texts  = [prefix + json.loads(l)[field] for l in open(jsonl, encoding='utf-8')]
t0     = time.time()

if kind == 'hf':
    import torch
    from sentence_transformers import SentenceTransformer

    device = 'mps' if torch.backends.mps.is_available() else 'cuda' if torch.cuda.is_available() else 'cpu'
    if device == 'cpu':
        torch.set_num_threads(int(os.environ.get('THREADS', os.cpu_count() or 4)))
    m = SentenceTransformer(model_id, device=device, trust_remote_code=False)
    m.max_seq_length = min(512, m.max_seq_length or 512)
    order = np.argsort([-len(t) for t in texts])          # longest first: little padding per batch
    vecs  = m.encode([texts[i] for i in order], batch_size=int(os.environ.get('BATCH', 32)),
                     normalize_embeddings=True, convert_to_numpy=True, show_progress_bar=True)
    result = np.empty_like(vecs)
    result[order] = vecs
elif kind == 'ollama':
    url, rows = arg('--url', 'http://localhost:11434').rstrip('/'), []
    for start in range(0, len(texts), 64):
        body = json.dumps({'model': model_id, 'input': texts[start:start + 64]}).encode()
        req  = urllib.request.Request(url + '/api/embed', data=body, headers={'Content-Type': 'application/json'})
        rows.extend(json.load(urllib.request.urlopen(req, timeout=600))['embeddings'])
        print(f'\r{len(rows)}/{len(texts)}', end='', flush=True)
    print()
    result = np.asarray(rows, dtype=np.float32)
    result /= np.linalg.norm(result, axis=1, keepdims=True)
    device = 'ollama'
else:
    sys.exit('kind must be hf or ollama')

result.astype('<f4').tofile(out)
json.dump({'n': len(texts), 'dim': int(result.shape[1]), 'seconds': round(time.time() - t0, 1),
           'model': model_id, 'kind': kind, 'device': device, 'field': field, 'prefix': prefix},
          open(out + '.json', 'w'))
print(f'{model_id} {field} dim={result.shape[1]} on {device} in {time.time() - t0:.0f}s')
