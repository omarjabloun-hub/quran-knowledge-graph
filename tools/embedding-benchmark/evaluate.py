# Scores every embedding (<out>/vec/*.f32) and the lexical signals against QurSim.
#   python -I evaluate.py <out> [fusion specs...] [ctx:<embedding name>]
#
# For every verse QurSim relates to at least one other (the queries), all 6,235 other verses are
# ranked and we ask where the related ones land:
#   R@10 / R@20  share of a query's related verses ranked in its top 10 / 20 (averaged per query)
#   MRR          1 / rank of the first related verse
#   strong       the same over degree-2 ("strongly related") pairs only
# The last two columns split R@20 by whether the two verses share a lemma: the embeddings are
# only worth their cost on the pairs the lexical signals cannot see.
#
# A fusion spec "name=methodA:wA,methodB:wB" sums z-normalised similarity rows with weights,
# the way qkg/verse/similar combines its signals: e.g.
#   fused=harrier:1,shared_lemmas:1,shared_roots:0.5
# "ctx:<name>" adds <name>+ctx: each verse's vector plus half of its neighbours' in the surah.
import glob, hashlib, json, os, sys
import numpy as np
from sklearn.feature_extraction.text import TfidfVectorizer

bench = sys.argv[1]
verses = [json.loads(l) for l in open(os.path.join(bench, 'verses.jsonl'), encoding='utf-8')]
keys = [v['key'] for v in verses]
idx = {k: i for i, k in enumerate(keys)}
N = len(keys)

rel = {}            # query -> {partner: degree}
for a, b, d in json.load(open(os.path.join(bench, 'gold.json')))['pairs']:
    rel.setdefault(idx[a], {})[idx[b]] = d
    rel.setdefault(idx[b], {})[idx[a]] = d

def queries(min_degree):
    return {q: [p for p, d in parts.items() if d >= min_degree] for q, parts in rel.items()
            if any(d >= min_degree for d in parts.values())}

Q1, Q2 = queries(1), queries(2)
dev = {q for q in Q1 if int(hashlib.md5(keys[q].encode()).hexdigest(), 16) % 2 == 0}

overlap = {}         # (q, p) -> True when the two verses share at least one non-ubiquitous lemma

def metrics(score_fn, qs, subset=None):
    r10 = r20 = mrr = 0.0
    n = 0
    hit_ov = n_ov = hit_no = n_no = 0
    for q, parts in qs.items():
        if subset is not None and q not in subset:
            continue
        row = score_fn(q).astype(np.float64)
        row[q] = -np.inf
        s_rel = row[parts]
        ranks = (row[None, :] > s_rel[:, None]).sum(axis=1) + 1
        r10 += np.mean(ranks <= 10)
        r20 += np.mean(ranks <= 20)
        mrr += 1.0 / ranks.min()
        n += 1
        for p, r in zip(parts, ranks):
            if overlap.get((q, p)):
                n_ov += 1; hit_ov += r <= 20
            else:
                n_no += 1; hit_no += r <= 20
    return 100 * r10 / n, 100 * r20 / n, mrr / n, n, 100 * hit_ov / max(n_ov, 1), 100 * hit_no / max(n_no, 1), n_ov, n_no

methods = {}

def lexical(field):
    vec = TfidfVectorizer(analyzer=lambda x: x, sublinear_tf=True, norm='l2')
    m = vec.fit_transform([v[field] for v in verses]).tocsr()
    return lambda q: (m @ m[q].T).toarray().ravel()

def shared_rare(field):
    # the template's graph signal: sum of log(n/df)/log(n) over shared neighbours, saturated
    from scipy.sparse import csr_matrix
    vocab = {}
    rows, cols = [], []
    for i, v in enumerate(verses):
        for e in set(v[field]):
            rows.append(i)
            cols.append(vocab.setdefault(e, len(vocab)))
    X = csr_matrix((np.ones(len(rows)), (rows, cols)), shape=(N, len(vocab)))
    df = np.asarray(X.sum(axis=0)).ravel()
    w = np.log(N / df) / np.log(N)
    w[df > N / 2] = 0.0
    Xw = X.multiply(w).tocsr()
    return lambda q: 1.0 - np.exp(-1.5 * (X @ Xw[q].T).toarray().ravel())

for f in ('words', 'lemmas', 'roots'):
    methods[f'tfidf_{f}'] = lexical(f)
    methods[f'shared_{f}'] = shared_rare(f)

for q, parts in rel.items():
    row = methods['shared_lemmas'](q)
    for p in parts:
        overlap[(q, p)] = bool(row[p] > 0)

matrices = {}
for path in sorted(glob.glob(os.path.join(bench, 'vec', '*.f32'))):
    meta = json.load(open(path + '.json'))
    E = np.fromfile(path, dtype='<f4').reshape(meta['n'], meta['dim'])
    E = E / np.linalg.norm(E, axis=1, keepdims=True)
    matrices[os.path.basename(path)[:-4]] = E
    methods[os.path.basename(path)[:-4]] = (lambda E: (lambda q: E @ E[q]))(E)

surah_of = [k.split(':')[0] for k in keys]
def with_context(E, alpha=0.5):
    # each verse plus half of its neighbours in the same surah: a verse is often a fragment of a sentence
    C = E.copy()
    for i in range(N):
        if i > 0 and surah_of[i - 1] == surah_of[i]:
            C[i] += alpha * E[i - 1]
        if i + 1 < N and surah_of[i + 1] == surah_of[i]:
            C[i] += alpha * E[i + 1]
    return C / np.linalg.norm(C, axis=1, keepdims=True)

for spec in [a for a in sys.argv[2:] if a.startswith('ctx:')]:
    name = spec[4:]
    C = with_context(matrices[name])
    methods[name + '+ctx'] = (lambda C: (lambda q: C @ C[q]))(C)
sys.argv = sys.argv[:2] + [a for a in sys.argv[2:] if not a.startswith('ctx:')]

def zrow(fn):
    def g(q):
        r = fn(q).astype(np.float64)
        r[q] = np.nan
        mu, sd = np.nanmean(r), np.nanstd(r) or 1.0
        r = (r - mu) / sd
        r[q] = 0.0
        return r
    return g

def fused(spec):
    parts = [(m, float(w)) for m, w in (p.split(':') for p in spec.split(','))]
    fns = [(zrow(methods[m]), w) for m, w in parts]
    return lambda q: sum(w * f(q) for f, w in fns)

for spec in sys.argv[2:]:
    name, body = spec.split('=', 1)
    methods[name] = fused(body)

import re as _re
only = os.environ.get('ONLY')
rows = []
for name, fn in methods.items():
    if only and not _re.search(only, name):
        continue
    a = metrics(fn, Q1)
    s = metrics(fn, Q2)
    rows.append((name, a, s))

rows.sort(key=lambda r: -r[1][0])
print(f'queries: related(>=1) {len(Q1)}  strong(2) {len(Q2)}  candidates {N - 1}  '
      f'related pairs sharing a lemma {rows[0][1][6]} / sharing none {rows[0][1][7]}')
print('| method | R@10 | R@20 | MRR | strong R@10 | strong MRR | R@20 shared-lemma pairs | R@20 no-shared-lemma pairs |')
print('|---|---|---|---|---|---|---|---|')
for name, a, s in rows:
    print(f'| {name} | {a[0]:.1f} | {a[1]:.1f} | {a[2]:.3f} | {s[0]:.1f} | {s[2]:.3f} | {a[4]:.1f} | {a[5]:.1f} |')
