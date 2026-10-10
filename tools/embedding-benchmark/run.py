# Runs every job in candidates.tsv whose vectors are missing, then scores everything.
#   python -I run.py [--only REGEX] [--skip-ollama] [--skip-hf] [--evaluate-only] [fusion specs...]
#
# Inputs come from data/raw (run `just fetch` first); everything is written under out/ (gitignored):
#   out/verses.jsonl, out/gold.json   prep.py
#   out/vec/<model>__<field>__<tag>.f32  one per job
#   out/results.md                    the table, best R@10 first
import os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT  = os.path.join(HERE, 'out')
VEC  = os.path.join(OUT, 'vec')
PY   = sys.executable

flags  = {a for a in sys.argv[1:] if a.startswith('--') and a != '--only'}
only   = sys.argv[sys.argv.index('--only') + 1] if '--only' in sys.argv else None
fusion = [a for a in sys.argv[1:] if not a.startswith('--') and a != only]

os.makedirs(VEC, exist_ok=True)

if not os.path.exists(os.path.join(OUT, 'verses.jsonl')):
    subprocess.run([PY, '-I', os.path.join(HERE, 'prep.py'), os.path.join(ROOT, 'data', 'raw'), OUT], check=True)

failed = []
if '--evaluate-only' not in flags:
    for line in open(os.path.join(HERE, 'candidates.tsv'), encoding='utf-8'):
        if not line.strip() or line.startswith('#'):
            continue
        cols = line.rstrip('\n').split('\t') + [''] * 6
        kind, model, field, tag, prefix = cols[:5]
        if only and not re.search(only, model):            continue
        if kind == 'ollama' and '--skip-ollama' in flags:  continue
        if kind == 'hf' and '--skip-hf' in flags:          continue

        out = os.path.join(VEC, f"{model.replace('/', '--').replace(':', '-')}__{field}__{tag}.f32")
        if os.path.exists(out + '.json'):
            continue

        print(f'\n== {kind} {model} ({field}, {tag})', flush=True)
        cmd = [PY, '-I', os.path.join(HERE, 'embed.py'), kind, model, os.path.join(OUT, 'verses.jsonl'), field, out]
        if prefix:
            cmd += ['--prefix', prefix]
        if subprocess.run(cmd).returncode != 0:
            failed.append(model)

table = subprocess.run([PY, '-I', os.path.join(HERE, 'evaluate.py'), OUT, *fusion], check=True, capture_output=True, text=True).stdout
open(os.path.join(OUT, 'results.md'), 'w', encoding='utf-8').write(table)
print('\n' + table)
if failed:
    print('Failed (see above): ' + ', '.join(failed))
