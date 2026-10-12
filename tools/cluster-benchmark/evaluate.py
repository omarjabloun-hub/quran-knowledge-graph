# Scores the clusters against references made by people — none of which the clusters read.
#
#   python -I evaluate.py <out>                       recompute everything the way the workspace does
#   python -I evaluate.py <out> --from build.json     score what one workspace build wrote
#   python -I evaluate.py <out> --target 180 --tau 0.12 --split dev
#
# <out> is tools/embedding-benchmark/out (verses.jsonl and gold.json, written by its prep.py); the
# ruku' come from data/raw/tanzil/quran-data.xml. build.json is the answer of `just clusters`.
#
# This file is the reference implementation of qkg/clusters/build: the same signals, constants,
# dynamic programme and average linkage, so a constant can be tried here first. A workspace build
# and this script find the identical passages and themes (`--from` checks that too).
#
#   PASSAGES  against the ruku', the traditional sections (556): Pk and WindowDiff (lower is
#             better), boundary F1 within one verse. Baseline: cutting each surah into the same
#             number of equal-length passages. --split dev|test scores one half of the surahs (a
#             stable hash of the surah number): choose a constant on dev, report on test.
#   THEMES    against QurSim, Ibn Kathir's related verses: the share of related pairs from
#             different surahs that land in the same theme, and how many times chance that is
#             (the same theme sizes, shuffled over the verses).
#   SURAHS    similar surahs against QurSim summed over pairs of surahs (pairs ÷ √(verses ×
#             verses)): Spearman per surah, and how many of its five most related surahs reach
#             the top five.
import collections, hashlib, json, math, os, sys
import xml.etree.ElementTree as ET
import numpy as np

args = sys.argv[1:]
if not args:
    sys.exit(__doc__ or 'usage: python -I evaluate.py <out> [--from build.json] [--target N] [--tau T] [--split dev|test|all]')
out = args[0]
opt = {k: v for k, v in zip(args[1::2], args[2::2])}
TARGET = float(opt.get('--target', 160))
TAU    = float(opt.get('--tau', 0.10))
SPLIT  = opt.get('--split', 'all')
root   = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
ruku_xml = opt.get('--ruku', os.path.join(root, 'data/raw/tanzil/quran-data.xml'))

# --- the constants of qkg/clusters/build -----------------------------------------------------------
WINDOW_WORDS, LAMBDA, W_LEMMAS, W_ROOTS, OPENER_BONUS = 50, 8.0, 1.0, 0.5, 2.0
OPENERS = ['يا أيها', 'وإذ ', 'واذكر', 'ولقد', 'ألم تر', 'هل أتاك', 'واتل', 'كذبت', 'وإلى ']

verses = [json.loads(line) for line in open(os.path.join(out, 'verses.jsonl'), encoding='utf-8')]
N      = len(verses)
keys   = [v['key'] for v in verses]
index  = {k: i for i, k in enumerate(keys)}
SUR    = [int(k.split(':')[0]) for k in keys]
AYA    = [int(k.split(':')[1]) for k in keys]
WORDS  = [len(v['words']) for v in verses]
LEM    = [sorted(set(v['lemmas'])) for v in verses]
ROO    = [sorted(set(r for r in v['roots'] if r)) for v in verses]
PLAIN  = [v['arabic_plain'] for v in verses]
by_surah = collections.defaultdict(list)
for i in range(N):
    by_surah[SUR[i]].append(i)

def idf_over_verses(sets):
    df = collections.Counter(t for s in sets for t in s)
    return {t: math.log(N / d) for t, d in df.items()}

IDF_L, IDF_R = idf_over_verses(LEM), idf_over_verses(ROO)

def block(idx, sets, idf):
    c = collections.Counter(t for i in idx for t in sets[i])
    return {t: n * idf[t] for t, n in c.items()}

def cosine(a, b):
    if not a or not b:
        return 0.0
    if len(a) > len(b):
        a, b = b, a
    dot = sum(w * b.get(t, 0.0) for t, w in a.items())
    na, nb = math.sqrt(sum(w * w for w in a.values())), math.sqrt(sum(w * w for w in b.values()))
    return dot / (na * nb) if na > 0 and nb > 0 else 0.0

def standardise(x):
    x = np.asarray(x, dtype=float)
    if len(x) < 2:
        return np.zeros_like(x)
    sd = x.std()
    return (x - x.mean()) / sd if sd > 0 else np.zeros_like(x)

def segment(idx, target=TARGET):
    """qkg/clusters/build's Segment: the local indices where passages start."""
    n = len(idx)
    if n < 2:
        return [0]
    lex, roo = [], []
    for g in range(n - 1):
        left, w, j = [], 0, g
        while j >= 0 and (w < WINDOW_WORDS or not left):
            left.append(idx[j]); w += WORDS[idx[j]]; j -= 1
        right, w, j = [], 0, g + 1
        while j < n and (w < WINDOW_WORDS or not right):
            right.append(idx[j]); w += WORDS[idx[j]]; j += 1
        lex.append(cosine(block(left, LEM, IDF_L), block(right, LEM, IDF_L)))
        roo.append(cosine(block(left, ROO, IDF_R), block(right, ROO, IDF_R)))
    cut = W_LEMMAS * standardise(lex) + W_ROOTS * standardise(roo)
    cut = [cut[g] - (OPENER_BONUS if any(PLAIN[idx[g + 1]].startswith(o) for o in OPENERS) else 0) for g in range(n - 1)]
    cum = [0]
    for i in idx:
        cum.append(cum[-1] + WORDS[i])
    best, back = [0.0] + [math.inf] * n, [0] * (n + 1)
    for end in range(1, n + 1):
        for start in range(end):
            off = (cum[end] - cum[start] - target) / target
            c = best[start] + LAMBDA * off * off + (cut[start - 1] if start > 0 else 0.0)
            if c < best[end]:
                best[end], back[end] = c, start
    starts, end = [], n
    while end > 0:
        starts.append(back[end]); end = back[end]
    return sorted(starts)

def equal_split(idx, parts):
    """The baseline: the same number of passages, of equal length in words."""
    n = len(idx)
    total = sum(WORDS[i] for i in idx)
    cum, starts, acc = [], [0], 0
    for i in idx:
        acc += WORDS[i]; cum.append(acc)
    for p in range(1, parts):
        b = int(np.searchsorted(cum, total * p / parts)) + 1
        if starts[-1] < b < n:
            starts.append(b)
    return starts

def passage_key(p):
    return f'P:{SUR[p[0]]}:{AYA[p[0]]}-{AYA[p[-1]]}'

# --- passages --------------------------------------------------------------------------------------
build = json.load(open(opt['--from'], encoding='utf-8')) if '--from' in opt else None
if build:
    passages = []
    for k in build['PassageKeys']:
        s, rng = k[2:].split(':')
        a, b = map(int, rng.split('-'))
        passages.append([index[f'{s}:{x}'] for x in range(a, b + 1)])
else:
    passages = []
    for s in sorted(by_surah):
        idx    = by_surah[s]
        starts = segment(idx) + [len(idx)]
        passages += [idx[a:b] for a, b in zip(starts, starts[1:])]
NP = len(passages)

ruku = set()
for r in ET.parse(ruku_xml).getroot().iter('ruku'):
    ruku.add((int(r.get('sura')), int(r.get('aya'))))

def in_split(s):
    return SPLIT == 'all' or (int(hashlib.md5(str(s).encode()).hexdigest(), 16) % 2 == 0) == (SPLIT == 'dev')

def score_segmentation(starts_of):
    pk = pk_n = wd = 0
    tp = fp = fn = 0
    for s, idx in by_surah.items():
        n = len(idx)
        if not in_split(s) or n < 3:
            continue
        ref = sorted({j for j, i in enumerate(idx) if (s, AYA[i]) in ruku} | {0})
        hyp = starts_of(s, idx)
        k = min(max(1, round(n / (2 * len(ref)))), n - 1)
        rl, hl = np.zeros(n, int), np.zeros(n, int)
        for m, b in enumerate(ref): rl[b:] = m
        for m, b in enumerate(hyp): hl[b:] = m
        rset, hset = set(ref) - {0}, set(hyp) - {0}
        for i in range(n - k):
            pk += (rl[i] == rl[i + k]) != (hl[i] == hl[i + k])
            wd += sum(i < b <= i + k for b in rset) != sum(i < b <= i + k for b in hset)
            pk_n += 1
        used = set()
        for b in sorted(hset):
            m = next((r for r in (b, b - 1, b + 1) if r in rset and r not in used), None)
            if m is None: fp += 1
            else: tp += 1; used.add(m)
        fn += len(rset - used)
    p, r = tp / max(1, tp + fp), tp / max(1, tp + fn)
    return pk / pk_n, wd / pk_n, 2 * p * r / max(1e-9, p + r)

local_starts = collections.defaultdict(list)
for p in passages:
    idx = by_surah[SUR[p[0]]]
    local_starts[SUR[p[0]]].append(idx.index(p[0]))

surahs_scored = sum(1 for s in by_surah if in_split(s) and len(by_surah[s]) >= 3)
print(f'## Passages against the ruku\' — {SPLIT} surahs ({surahs_scored} of them with 3 verses or more)\n')
print('| Cut | Passages | Pk | WindowDiff | Boundary F1 (±1 verse) |')
print('|---|---|---|---|---|')
ours = sum(len(local_starts[s]) for s in by_surah if in_split(s))
print('| equal-length, the same number of passages per surah | {} | {:.3f} | {:.3f} | {:.3f} |'.format(
    ours, *score_segmentation(lambda s, idx: equal_split(idx, len(local_starts[s])))))
print('| {} | {} | {:.3f} | {:.3f} | {:.3f} |'.format('this build' if build else f'vocabulary change (target {TARGET:g} words)', ours,
                                                       *score_segmentation(lambda s, idx: sorted(local_starts[s]))))

# --- themes ----------------------------------------------------------------------------------------
def passage_vectors(sets):
    tf = [collections.Counter(t for i in p for t in sets[i]) for p in passages]
    df = collections.Counter(t for c in tf for t in c)
    vocab = {t: j for j, t in enumerate(sorted(df))}
    X = np.zeros((NP, len(vocab)))
    for k, c in enumerate(tf):
        for t, n in c.items():
            if df[t] <= NP / 2:
                X[k, vocab[t]] = (1 + math.log(n)) * math.log(NP / df[t])
    norms = np.linalg.norm(X, axis=1, keepdims=True)
    return X / np.where(norms > 0, norms, 1)

def average_linkage(S, tau):
    """qkg/clusters/build's clustering: merge the two groups with the highest average cosine while it is ≥ tau."""
    S = S.copy()
    np.fill_diagonal(S, -np.inf)
    members = [[k] for k in range(len(S))]
    alive = np.ones(len(S), bool)
    while True:
        masked = np.where(np.outer(alive, alive), S, -np.inf)
        a, b = np.unravel_index(np.argmax(masked), masked.shape)
        if masked[a, b] < tau:
            break
        a, b = min(a, b), max(a, b)
        sa, sb = len(members[a]), len(members[b])
        merged = (sa * S[a] + sb * S[b]) / (sa + sb)
        S[a], S[:, a] = merged, merged
        S[a, a] = -np.inf
        members[a] += members[b]
        alive[b] = False
        S[b], S[:, b] = -np.inf, -np.inf
    return [sorted(m) for m, ok in zip(members, alive) if ok]

if build:
    key_of = {passage_key(p): k for k, p in enumerate(passages)}
    themes = [sorted(key_of[x] for x in t) for t in build['ThemePassages']]
else:
    S = (passage_vectors(LEM) @ passage_vectors(LEM).T + 0.5 * passage_vectors(ROO) @ passage_vectors(ROO).T) / 1.5
    themes = [g for g in average_linkage(S, TAU) if len({SUR[passages[k][0]] for k in g}) >= 2]

theme_of = np.full(N, -1)
for t, g in enumerate(themes):
    for k in g:
        theme_of[passages[k]] = t
gold = json.load(open(os.path.join(out, 'gold.json'), encoding='utf-8'))['pairs']
cross = [(index[a], index[b]) for a, b, d in gold if d >= 1 and SUR[index[a]] != SUR[index[b]]]
kept = sum(theme_of[i] >= 0 and theme_of[i] == theme_of[j] for i, j in cross)
rng = np.random.default_rng(1)
chance = np.mean([sum(p[i] >= 0 and p[i] == p[j] for i, j in cross) for p in (rng.permutation(theme_of) for _ in range(20))])
print(f'\n## Themes against QurSim — {"this build" if build else f"tau {TAU:g}"}\n')
biggest = max((sum(len(passages[k]) for k in g) for g in themes), default=0)
widest  = max((len({SUR[passages[k][0]] for k in g}) for g in themes), default=0)
print('| Themes | Verses in a theme | Biggest (verses) | Widest (surahs) | Related pairs across surahs kept together | By chance | Times chance |')
print('|---|---|---|---|---|---|---|')
print(f'| {len(themes)} | {(theme_of >= 0).sum():,} of {N:,} | {biggest} | {widest} | {kept:,} of {len(cross):,} ({100 * kept / len(cross):.1f} %) | {chance:.0f} | {kept / max(1e-9, chance):.1f} |')

# --- similar surahs --------------------------------------------------------------------------------
NS = 114
def surah_cosines(sets):
    tf = [collections.Counter(t for i in by_surah[s] for t in sets[i]) for s in range(1, NS + 1)]
    df = collections.Counter(t for c in tf for t in c)
    vocab = {t: j for j, t in enumerate(sorted(df))}
    X = np.zeros((NS, len(vocab)))
    for k, c in enumerate(tf):
        for t, n in c.items():
            if df[t] <= NS / 2:
                X[k, vocab[t]] = (1 + math.log(n)) * math.log(NS / df[t])
    X /= np.maximum(np.linalg.norm(X, axis=1, keepdims=True), 1e-9)
    return X @ X.T

def by_row(M):
    Z = np.zeros_like(M)
    for i in range(NS):
        o = np.delete(M[i], i)
        Z[i] = (M[i] - o.mean()) / o.std() if o.std() > 0 else 0
    return Z

def ranks(x):
    """Ranks with ties averaged — most pairs of surahs share no related verse at all."""
    x = np.asarray(x)
    r = np.empty(len(x)); r[np.argsort(x, kind='stable')] = np.arange(len(x))
    _, groups, sizes = np.unique(x, return_inverse=True, return_counts=True)
    return (np.bincount(groups, weights=r) / sizes)[groups]

ref = np.zeros((NS, NS))
for a, b, d in gold:
    i, j = SUR[index[a]] - 1, SUR[index[b]] - 1
    if d >= 1 and i != j:
        ref[i, j] += 1; ref[j, i] += 1
nv = np.array([len(by_surah[s]) for s in range(1, NS + 1)])
ref /= np.sqrt(np.outer(nv, nv))
CL, CR = surah_cosines(LEM), surah_cosines(ROO)
print('\n## Similar surahs against QurSim summed over pairs of surahs\n')
print('| Ranking | Spearman | Top-5 related in our top 5 | in our top 10 |')
print('|---|---|---|---|')
for name, M in (('random', np.random.default_rng(0).random((NS, NS))), ('shared lemmas', CL), ('shared roots', CR),
                ('lemmas + 0.5 roots — qkg/surah/similar', by_row(CL) + 0.5 * by_row(CR))):
    rho, top5, top10 = [], [], []
    for i in range(NS):
        if ref[i].sum() == 0:
            continue
        o = [j for j in range(NS) if j != i]
        rho.append(np.corrcoef(ranks(M[i, o]), ranks(ref[i, o]))[0, 1])
        want = set(sorted(o, key=lambda j: -ref[i, j])[:5])
        got = sorted(o, key=lambda j: -M[i, j])
        top5.append(len(want & set(got[:5])) / 5); top10.append(len(want & set(got[:10])) / 5)
    print(f'| {name} | {np.mean(rho):.2f} | {100 * np.mean(top5):.0f} % | {100 * np.mean(top10):.0f} % |')
