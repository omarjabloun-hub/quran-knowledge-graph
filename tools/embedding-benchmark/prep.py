# Builds the benchmark inputs from data/raw (run `just fetch` first).
#   python -I prep.py <data/raw> <out>
# Writes:
#   verses.jsonl  one verse per line, mushaf order: key, arabic_plain (Tanzil Simple-Clean, the
#                 text the workspace embeds), arabic_uthmani (words only), english_saheeh, and the
#                 words / lemmas / roots of the Quranic Arabic Corpus stems
#   gold.json     QurSim cleaned: undirected pairs with the highest degree given to them
#
# The Bismillah Tanzil prefixes to verse 1 of every surah but 1 and 9 is removed, as the
# connector removes it: otherwise 112 verses would all look alike.
import collections, csv, json, os, re, sys

data, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

TASHKEEL = re.compile(r'[ؐ-ًؚ-ٰٟۖ-ۭـ]')
def plain(x):
    return TASHKEEL.sub('', x).replace('ٱ', 'ا')

def skeleton(x):
    x = x.replace('ـٔ', 'ء').replace('ٔ', 'ء')
    return re.sub(r'[^ء-غف-يٱ]', '', x).replace('ٱ', 'ا')

def read_pipe(path):
    rows = {}
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if not line or line.startswith('#'):
            continue
        s, a, t = line.split('|', 2)
        rows[(int(s), int(a))] = t
    return rows

simple  = read_pipe(os.path.join(data, 'tanzil/quran-simple-clean.txt'))
uthmani = read_pipe(os.path.join(data, 'tanzil/quran-uthmani.txt'))
sahih   = read_pipe(os.path.join(data, 'translations/en.sahih.txt'))
assert len(simple) == len(uthmani) == len(sahih) == 6236, 'expected 6,236 verses in each text'

BISM_PLAIN = 'بسم الله الرحمن الرحيم'
letters = re.compile(r'[ء-يٱ]')
stripped = 0
for (s, a) in list(simple):
    if a == 1 and s not in (1, 9):
        t = simple[(s, a)]
        assert t.startswith(BISM_PLAIN), (s, a)
        simple[(s, a)] = t[len(BISM_PLAIN):].strip()
        toks = uthmani[(s, a)].split()
        n = 0
        while n < len(toks) and sum(1 for w in toks[:n] if letters.search(w)) < 4:
            n += 1
        assert ' '.join(skeleton(w) for w in toks[:n] if letters.search(w)) == BISM_PLAIN, (s, a)
        uthmani[(s, a)] = ' '.join(toks[n:])
        stripped += 1
assert stripped == 112

# morphology: per word, the stem's lemma and root (the root-bearing stem when there are several)
W = collections.defaultdict(list)
for line in open(os.path.join(data, 'morphology/quran-morphology.txt'), encoding='utf-8'):
    if not line.strip() or line.startswith('#'):
        continue
    loc, form, tag, f = line.rstrip('\n').split('\t')
    s, a, w, g = map(int, loc.split(':'))
    W[(s, a, w)].append((g, form, tag, f.split('|')))

per_verse = collections.defaultdict(lambda: {'words': [], 'lemmas': [], 'roots': []})
for (s, a, w) in sorted(W):
    segs  = sorted(W[(s, a, w)])
    form  = ''.join(x[1] for x in segs)
    stems = [x for x in segs if 'PREF' not in x[3] and 'SUFF' not in x[3]]
    st    = next((x for x in stems if any(t.startswith('ROOT:') for t in x[3])), stems[0] if stems else None)
    v = per_verse[(s, a)]
    v['words'].append(plain(form))
    if st:
        lem  = [t[4:] for t in st[3] if t.startswith('LEM:')]
        root = [t[5:] for t in st[3] if t.startswith('ROOT:')]
        if lem:
            v['lemmas'].append(lem[0] + '|' + (root[0] if root else '') + '|' + st[2])
        if root:
            v['roots'].append(root[0])

keys = sorted(simple)
with open(os.path.join(out, 'verses.jsonl'), 'w', encoding='utf-8') as fh:
    for (s, a) in keys:
        u = ' '.join(w for w in uthmani[(s, a)].split() if letters.search(w))
        fh.write(json.dumps({
            'key': f'{s}:{a}',
            'arabic_plain': simple[(s, a)],
            'arabic_uthmani': u,
            'english_saheeh': sahih[(s, a)],
            **per_verse[(s, a)],
        }, ensure_ascii=False) + '\n')

# QurSim: drop malformed rows and self pairs; undirected; keep the highest degree of a pair
index = {f'{s}:{a}' for (s, a) in keys}
pairs, bad = {}, 0
for r in csv.DictReader(open(os.path.join(data, 'qursim/ibn_kathir_similarity.csv'), encoding='utf-8-sig')):
    try:
        a = f"{int(r['Source_Sura'])}:{int(r['Source_Verse'])}"
        b = f"{int(r['target_sura'])}:{int(r['Target_verse'])}"
        d = int(r['relevance_degree'])
    except (ValueError, TypeError, KeyError):
        bad += 1
        continue
    if a == b or a not in index or b not in index or d not in (0, 1, 2):
        bad += 1
        continue
    k = tuple(sorted((a, b), key=lambda x: tuple(map(int, x.split(':')))))
    pairs[k] = max(pairs.get(k, -1), d)

json.dump({'pairs': [[a, b, d] for (a, b), d in sorted(pairs.items())]}, open(os.path.join(out, 'gold.json'), 'w'))
print(f'verses {len(keys)} (Bismillah removed from {stripped})  qursim pairs {len(pairs)} '
      f'(dropped rows {bad})  degrees {dict(collections.Counter(pairs.values()))}')
