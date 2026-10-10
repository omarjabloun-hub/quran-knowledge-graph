#!/usr/bin/env python3
"""Generate the application's sandbox AI tool from the generated schema constants.

usage: gen_sandbox.py --ncs <N.cs> --defs <Definitions dir> --uid <UID> --cls <Class> --func <name>
                      --display <name> --desc <tool description> --prefix <endpoint prefix/>
                      --exclude-types A,B --exclude-endpoints a/b,c/d --usecase docs/use-case.md
                      --readme-intro <text> --template <template.cs>

Writes <defs>/code/chat-ai-tools/<UID>.cs. Record types are every `Type = "..."` in N.cs that does
not start with '_' and is not excluded; endpoints are every endpoint under <defs>/code/endpoints
whose header carries [endpoint: Curiosity.Endpoints.ReadOnly], is not AdminOnly, starts with the
prefix, is not a `_lib`, and is not excluded. Pass the ground-truth types to --exclude-types and
the evaluation endpoint to --exclude-endpoints, and hand --usecase a copy of the brief with the
ground-truth sections removed (see gen.sh). Re-run after N.cs or the endpoint set changes.
Python 3 standard library only.
"""
import argparse,re,os,glob
ap=argparse.ArgumentParser()
for a in ['ncs','defs','uid','cls','func','display','desc','prefix','usecase','readme_intro','template']: ap.add_argument('--'+a.replace('_','-'),required=True)
ap.add_argument('--exclude-types',default=''); ap.add_argument('--exclude-endpoints',default='')
a=ap.parse_args()
ex_types=set(filter(None,a.exclude_types.split(','))); ex_ep=set(filter(None,a.exclude_endpoints.split(',')))
ncs=open(a.ncs,encoding='utf-8-sig').read()
types=sorted(set(t for t in re.findall(r'public const string Type\s*=\s*"([^"]+)"',ncs) if not t.startswith('_') and t not in ex_types))
eps=[]
for p in sorted(glob.glob(os.path.join(a.defs,'code','endpoints','**','*.cs'),recursive=True)):
    s=open(p,encoding='utf-8-sig').read()
    m=re.search(r'Curiosity\.Endpoints\.Path\("([^"]+)"\)',s)
    if not m or not re.search(r'^\[endpoint: Curiosity\.Endpoints\.ReadOnly\]',s,re.M) or 'AccessMode("AdminOnly")' in s: continue
    path=m.group(1)
    if not path.startswith(a.prefix): continue
    rel=path[len(a.prefix):]
    if rel.startswith('_') or rel in ex_ep: continue
    eps.append(rel)
def raw(text):
    runs=[len(x) for x in re.findall(r'"+',text)]; q='"'*max(3,(max(runs) if runs else 0)+1)
    return q+'\n'+text.rstrip('\n')+'\n'+q
usecase=open(a.usecase,encoding='utf-8-sig').read().replace('\r\n','\n')
readme=f"""# {a.display}

{a.readme_intro}

## Layout

- `/records/<Type>/` - one folder per record type ({len(types)} types). `_index.tsv` lists every record's id, label
  and uid (grep it first); `<id>.json` is one record - all its fields plus its neighbours - rendered when read.
- `/docs/schema.md` - every record type with its live count and the field its files are named by.
- `/docs/use-case.md` - what this application is for, and the questions it is built to answer.
- `/work/` - scratch space that persists for this conversation.

## Commands

- `search <words...>` - the workspace search, as the person chatting; `--type <Type>` narrows it, `--page 2` pages.
- `consult <uid> [--search <text>]` - one record in full, as the assistant's consult tool reads it.
- `query '<IQuery chain>'` - the graph query engine, read-only (`query --methods` lists what a chain may call),
  e.g. `query 'StartAt("{types[0]}").Take(5).Emit()'`.
- `app <endpoint> [json]` - this application's own read-only endpoints (`app list`); the numbers the screens show.
- `publish` - turn an HTML or markdown file under `/artifacts` into a document the reader can open.

## Rules

- Only these record types are here: {', '.join(types)}.{(' The evaluation ground truth (' + ', '.join(sorted(ex_types)) + ') is deliberately not.') if ex_types else ''}
- A folder lists at most 5,000 records; use `search` or `query` beyond that.
- Everything is read-only except `/work` and `/artifacts`.
"""
t=open(a.template).read()
def esc(s): return s.replace('\\','\\\\').replace('"','\\"')
rep={'UID':a.uid,'CLASS':a.cls,'FUNC':a.func,'DISPLAY':esc(a.display),'DESC':esc(a.desc),'SBXDESC':esc(a.desc),
     'PREFIX':a.prefix,'TYPES':', '.join(f'"{x}"' for x in types),'ENDPOINTS':', '.join(f'"{x}"' for x in eps),
     'EXCLUDED_TYPES':', '.join(sorted(ex_types)) or 'none','README':raw(readme),'USECASE':raw(usecase)}
for k,v in rep.items(): t=t.replace('@@'+k+'@@',v)
assert '@@' not in t
out=os.path.join(a.defs,'code','chat-ai-tools',a.uid+'.cs')
open(out,'w',encoding='utf-8').write(t)
print(out,len(types),'types',len(eps),'endpoints:',eps)
