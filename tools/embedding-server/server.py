# Serves one sentence-transformers model over the OpenAI embeddings API, so the workspace's external
# embeddings index can use a model Ollama cannot run — AraModernBert-STS, the best embedding of
# Quranic Arabic the benchmark found (tools/embedding-benchmark).
#
#   python -I server.py [--model NAMAA-Space/AraModernBert-Base-STS] [--host 127.0.0.1] [--port 8765]
#
#   POST /v1/embeddings  {"model": …, "input": "text" | ["text", …], "encoding_format": "float" | "base64"}
#   GET  /v1/models      the one model served
#   GET  /health         200 once the model is loaded
#
# Every vector is encoded exactly as the benchmark encoded it (embed.py): no prefix, at most 512
# tokens, L2-normalised — so what the workspace ranks with is what was measured. The model loads
# with trust_remote_code=False. The workspace container reaches this as host.docker.internal.
import argparse, base64, json, os, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np
import torch
from sentence_transformers import SentenceTransformer

parser = argparse.ArgumentParser()
parser.add_argument('--model', default=os.environ.get('EMBEDDING_SERVER_MODEL', 'NAMAA-Space/AraModernBert-Base-STS'))
parser.add_argument('--host',  default=os.environ.get('EMBEDDING_SERVER_HOST', '127.0.0.1'))
parser.add_argument('--port',  default=int(os.environ.get('EMBEDDING_SERVER_PORT', 8765)), type=int)
args = parser.parse_args()

device = 'mps' if torch.backends.mps.is_available() else 'cuda' if torch.cuda.is_available() else 'cpu'
model  = SentenceTransformer(args.model, device=device, trust_remote_code=False)
model.max_seq_length = min(512, model.max_seq_length or 512)
lock   = threading.Lock()            # one GPU, one batch at a time
BATCH  = int(os.environ.get('BATCH', 32))

def embed(texts):
    with lock:
        return model.encode(texts, batch_size=BATCH, normalize_embeddings=True, convert_to_numpy=True).astype('<f4')

class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def reply(self, status, body):
        data = json.dumps(body, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def error(self, status, message):
        self.reply(status, {'error': {'message': message, 'type': 'invalid_request_error'}})

    def do_GET(self):
        if self.path.rstrip('/') == '/health':
            self.reply(200, {'status': 'ok', 'model': args.model, 'device': device})
        elif self.path.rstrip('/') == '/v1/models':
            self.reply(200, {'object': 'list', 'data': [{'id': args.model, 'object': 'model', 'owned_by': 'local'}]})
        else:
            self.error(404, f'no route {self.path}')

    def do_POST(self):
        if self.path.rstrip('/') not in ('/v1/embeddings', '/embeddings'):
            return self.error(404, f'no route {self.path}')
        try:
            request = json.loads(self.rfile.read(int(self.headers.get('Content-Length') or 0)) or b'{}')
        except json.JSONDecodeError:
            return self.error(400, 'the body is not JSON')

        texts = request.get('input')
        texts = [texts] if isinstance(texts, str) else texts
        if not isinstance(texts, list) or not texts or not all(isinstance(t, str) for t in texts):
            return self.error(400, '"input" must be a string or a list of strings')

        vectors = embed(texts)
        as_b64  = request.get('encoding_format') == 'base64'
        data    = [{'object': 'embedding', 'index': i,
                    'embedding': base64.b64encode(v.tobytes()).decode() if as_b64 else v.tolist()}
                   for i, v in enumerate(vectors)]
        tokens  = sum(len(t.split()) for t in texts)
        self.reply(200, {'object': 'list', 'data': data, 'model': args.model,
                         'usage': {'prompt_tokens': tokens, 'total_tokens': tokens}})

    def log_message(self, fmt, *a):
        if os.environ.get('EMBEDDING_SERVER_LOG_REQUESTS'):
            super().log_message(fmt, *a)

embed(['تهيئة'])                    # warm up the device before the first request
print(f'{args.model} on {device}, dim {model.get_sentence_embedding_dimension()}: '
      f'http://{args.host}:{args.port}/v1/embeddings', flush=True)
ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()
