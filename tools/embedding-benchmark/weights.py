# Chooses each embedding's weight the way qkg/verse/similar uses it, without scoring on what chose it:
#   python -I weights.py [regex on vector names]          (or: just benchmark --weights [--only REGEX])
#
# For every out/vec/*.f32 (matching the regex), the fusion `embedding × W_SEM + shared lemmas × 1 +
# shared roots × 0.5` is scored for each W_SEM in WEIGHTS on the DEV half of the queries; the best
# (R@10, then strong R@10) is then scored once on the TEST half, next to the lexical baselines.
# Prints the dev grid and the test table (markdown, best R@10 first).
import glob, os, re, subprocess, sys

HERE    = os.path.dirname(os.path.abspath(__file__))
OUT     = os.path.join(HERE, 'out')
WEIGHTS = [0.25, 0.5, 1.0, 1.5, 2.0]
LEX     = 'shared_lemmas:1,shared_roots:0.5'

pick  = sys.argv[1] if len(sys.argv) > 1 else '.'
names = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(OUT, 'vec', '*.f32')))
names = [n for n in names if re.search(pick, n)]
if not names:
    sys.exit(f'No vectors in {OUT}/vec match {pick!r}: run the benchmark first.')

def run(split, specs):
    env  = dict(os.environ, SPLIT=split, ONLY='^(f_|base_)')
    args = [sys.executable, '-I', os.path.join(HERE, 'evaluate.py'), OUT] + [f'{k}={v}' for k, v in specs.items()]
    text = subprocess.run(args, env=env, check=True, capture_output=True, text=True).stdout
    rows = {}
    for line in text.splitlines():
        cells = [c.strip() for c in line.strip('|').split('|')]
        if len(cells) == 8 and cells[0] in specs:
            rows[cells[0]] = cells[1:]        # R@10, R@20, MRR, strong R@10, strong MRR, R@20 shared / no shared lemma
    return rows

base = {'base_lex': LEX, 'base_lem': 'shared_lemmas:1'}

dev_specs = dict(base)
for i, n in enumerate(names):
    for w in WEIGHTS:
        dev_specs[f'f_{i}_{w}'] = f'{n}:{w},{LEX}'
dev = run('dev', dev_specs)

chosen = {}
print('Dev half, R@10 per W_SEM:')
for i, n in enumerate(names):
    chosen[i] = max(WEIGHTS, key=lambda w: (float(dev[f'f_{i}_{w}'][0]), float(dev[f'f_{i}_{w}'][3])))
    print(f'  {n}: ' + '  '.join(f'{w}:{dev[f"f_{i}_{w}"][0]}' for w in WEIGHTS) + f'  -> {chosen[i]}', flush=True)
    if chosen[i] == WEIGHTS[-1]:
        print(f'    (the best weight is the largest tried: try more with --evaluate-only on SPLIT=dev)')

test_specs = dict(base)
for i, n in enumerate(names):
    test_specs[f'f_{i}_{chosen[i]}'] = f'{n}:{chosen[i]},{LEX}'
test = run('test', test_specs)

rows  = [('shared lemmas + 0.5 roots (no embedding)', '—', test['base_lex']), ('shared lemmas alone', '—', test['base_lem'])]
rows += [(n, chosen[i], test[f'f_{i}_{chosen[i]}']) for i, n in enumerate(names)]
print('\nTest half, at the weight the dev half chose:\n')
print('| Ranking | W_SEM | R@10 | R@20 | MRR | strong R@10 |')
print('|---|---|---|---|---|---|')
for n, w, r in sorted(rows, key=lambda r: (-float(r[2][0]), -float(r[2][3]))):
    print(f'| {n} | {w} | {r[0]} | {r[1]} | {r[2]} | {r[3]} |')
