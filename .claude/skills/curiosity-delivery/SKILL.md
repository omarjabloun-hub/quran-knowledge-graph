---
name: curiosity-delivery
description: Building, ingesting, importing, verifying and publishing a Curiosity application — the deployment loop that works on a 26.10 workspace (ingest, import after ingest, recreate the parser indexes, check what compile-status hides, upload the front end with curiosity-cli, round-trip the export, measure retrieval), the API calls for each step, what to verify at each layer, the records a deployment leaves (docs/deployment.md, TODO.md), rebuilding an environment from the repository, and the pre-demo checklist. Use when standing up an environment, promoting a change, or preparing to show the application.
---

# Delivery — build, ingest, import, verify, record, show

The layers depend on each other in one direction only, and the deploy order is the same as the
build order. Doing it out of order produces failures that look like bugs: an endpoint that will
not compile, a spotter that finds nothing, a renderer that draws UIDs. This is the loop that
took eight applications onto a 26.10 workspace; every check in it caught something at least once.

---

## 1. The order, every time

```
 1. build          the corpus tools, the read model, the connector, the front end (Release)
 2. validate       corpus validator + schema check must exit 0
 3. dry-run        the connector, no writes — read the counters
 4. ingest         the connector against the workspace
 5. emit schema    regenerate N.cs from the connector; commit
 6. import         the whole definitions zip (code, config, nlp) — AFTER the ingest
 7. link           recreate the parser indexes; confirm _Mentions edges exist
 8. provider       configure the AI provider on this environment (not exported)
 9. check          compile-status, the tools compile-status hides, sandboxes, every endpoint
10. front end      build Release, upload
11. verify         every route, one record of every type, a chat turn, a sandbox turn (§4)
12. round-trip     export, adopt the workspace's formatting, re-import once
13. measure        retrieval against the ground truth, per discovery route
14. record         docs/deployment.md and TODO.md (§6)
```

**Why 4 before 6:** endpoints are compiled against the schema constants the workspace injects,
so an endpoint naming a node type the graph has never seen does not compile.

**Why 7 exists:** entity linking only runs for fields mapped to the pipeline with the spotters,
and a mapping imported onto an ingested graph changes nothing until the parser indexes are
rebuilt. Most of the deployed demos had **zero `_Mentions` edges** until this step.

**Why 9 exists:** an import that answers `"Success": true` has only proved the files parsed.
Code compiles on first use, and the consult tool's compile errors are not listed anywhere.

**Why 5 exists at all:** `N`/`E` are the shared vocabulary of three codebases. Stale constants
are how a renamed property becomes an empty label at run time instead of a build error.

---

## 2. Commands

```bash
# corpus
python3 tools/validate.py                                        # must exit 0
dotnet run --project src/<App>.DatasetCheck -- --root . --strict  # must exit 0

# connector
dotnet run --project src/<App>.Connector -- --root . --dry-run                # map, write nothing
dotnet run --project src/<App>.Connector -- --root . --dry-run --no-attachments
dotnet run -c Release --project src/<App>.Connector -- --root . --endpoint "$U" --token "$LIBTOK"
dotnet run --project src/<App>.Connector -- --root . --reset-cache            # full re-ingest

# schema constants
dotnet run --project src/<App>.Connector -- --emit-frontend-schema src/<App>.FrontEnd/src/Schema/N.cs

# front end
dotnet build src/<App>.FrontEnd/<App>.FrontEnd.csproj -c Release
```

The hash cache lives next to the connector binary and is gitignored. Losing it costs one full
re-ingest and nothing else.

### Against the workspace's API

```bash
U=https://<host>/<workspace>
TOK=$(curl -s -X POST $U/api/login/create -H 'Content-Type: application/json' \
      -d '{"UserName":"<admin>","Password":"<password>"}')                 # the body IS the JWT
api(){ curl -s -H "Authorization: Bearer $TOK" "$U/api/$1" "${@:2}"; }
LIBTOK=$(api "library/token?purpose=connector&duration=60")               # for the connector
FETOK=$(api "library/token-front-end-upload?purpose=frontend&duration=60")  # for the upload

# 6. import — one zip; an event stream ending in "Success": true or the errors
(cd src/<App>.Definitions && zip -qr /tmp/defs.zip code config nlp -x '*README.md')
api graph/import-definitions -N -X POST -F "file=@/tmp/defs.zip;type=application/zip"

# 7. link — recreate every parser index, wait, then look at a real record
api index/list/FieldToDocumentIndex                          # NodeType + UID of each
api "index/recreate?nodeType=<Type>&indexUID=<uid>" -X POST  # for each of them
api index/status/any                                         # poll until false
api node/<uid of a record whose prose names an identifier>/andedges   # _Mentions present

# 9. check
api build/compile-status                                     # {"FailingIds":[]}
api "chatai/tools/get/function-names?toolUID=ConsuLt111111111111111"   # not []
api chatai/sandboxes                                         # every DefinitionError: null
api endpoints/list                                           # then POST {} to each:
api endpoints/run/<path> -X POST -H 'Content-Type: application/json' -d '{}'
#   a read answers data or "400 … is required"; a CS1234 in the body is a compile error

# 10. front end (dotnet tool install --global Curiosity.CLI)
curiosity-cli upload-front-end -s $U/ -t "$FETOK" \
    -p src/<App>.FrontEnd/bin/Release/netstandard2.0/tps --timeout 600

# 12. round-trip
api graph/export-definitions -o /tmp/export.zip
```

A workspace that answers *"This workspace is not running"* is waking up — retry after ~20 s.

---

## 3. What `compile-status` does not tell you

`build/compile-status` lists failing endpoints and custom tools — but **not the built-ins**, and
the consult readers live in a built-in (`ConsuLt111111111111111`). A reader that fails to compile
leaves consult with **no functions**: every chat still runs, and every answer is a guess.

- Check every tool's function list: `GET chatai/tools/get/function-names?toolUID=<uid>` must not
  be `[]` — for consult, Search Records, the sandbox and each custom tool.
- **To see the compiler's message, re-save the tool**: `GET chatai/tools/get?toolUID=<uid>`,
  drop `CodeHash` from the JSON (a save without a hash is not concurrency-checked), and `POST`
  it back to `chatai/tools/update?toolUID=<uid>`. The response carries the error
  (`Can't parse tool (…)`).
- `Tool is null` means the script does not end with `return new X();`.
- `chatai/tools/test` answers 500 for tools that need a conversation (Search Records, consult,
  a sandbox) — verify those with a chat turn instead.

The compile errors the 26.10 deployments hit, by class, with their fixes, are in
`curiosity-code-endpoints` §3a and `curiosity-ai-tools-and-agents` §3. Several can be caught
offline before a workspace exists — a small checker over `code/` that fails on front-end-only
`N` sets, `[Timestamp]` properties read as fields, `Out(string[], string)`, `dynamic`
parameters and a header that is not on the first line pays for itself on the first import.

---

## 4. What to verify, layer by layer

Each of these has been a real failure. Check them in this order; a failure at one layer
explains everything below it.

**Corpus**
- validator and schema check both exit 0
- every ground-truth case still resolves

**Graph**
- dry-run node and edge counters are in the expected range — a count that halved is a mapping
  regression; the real run's counts match them
- spot-check one record: does it have the edges you expect, in both directions?
- a metadata dimension node exists per distinct value, not per row
- users exist and the assignment edges reach them

**Definitions**
- the import ends `"Success": true` with no warnings you cannot explain
- the export round-trips: re-importing the synced tree is clean

**Search & NLP**
- a full-text query returns results; an identifier query finds its record
- `_Mentions` edges exist on a record whose prose names an identifier
- a spotted identifier resolves to a node, and an unspottable one is *not* creating junk nodes

**Endpoints**
- every endpoint called with `{}` compiles; reads return their DTO shape, not a string
- a write endpoint actually writes (the read-only marking is a run-time failure)
- counts and the UID arrays beside them agree

**AI**
- consult, Search Records, the sandbox and every custom tool expose functions (§3)
- `chatai/sandboxes` shows `DefinitionError: null`
- the answer-key checklist passes (`curiosity-ai-tools-and-agents` §7)
- a domain question is answered from records it read — identifiers in the reply are links
- the assistant answers a workload question with the same numbers the dashboard shows
- a sandbox turn answers a counting question and matches the corpus
- an agent invoked by name actually runs, and quotes rather than paraphrases

**Front end** — drive the deployed app with a browser (Playwright), not only the build:
- every route the app registers, with waits long enough for its slowest endpoint
- **open one record of every node type**: a label, an icon, a footer, no console errors, no
  raw UIDs anywhere, no empty Overview on a key-only type
- every panel with data behind it actually fills — front-end and server DTOs drift silently
- every counter card opens a list of exactly the number it showed
- a similarity list does not contain the seed or its subject
- the published-file tab shows the real viewer
- read the screenshots — a page that loads with no errors can still be empty

**Retrieval** — run the evaluation over every seed with ground truth and record recall @10 and
@20 per discovery route (`curiosity-similarity-scenarios` §5). A number you did not measure does
not go in the deployment record.

---

## 5. Rebuilding an environment from the repository

Someone has to have done this, once, before the demo. The steps are §1 with nothing skipped,
against a fresh workspace. What it catches:

- configuration that only exists in the old workspace because someone clicked it
- anything the definitions export does not carry (the AI provider, users' passwords)
- ordering assumptions that only hold on an already-populated graph
- UIDs that were assumed stable and are not — a provider task, a pipeline mapping's search
  defaults, anything per workspace

Write the result down in the definitions folder's README as the canonical sequence, including
every manual step.

---

## 6. The records a deployment leaves

Two files, written by whoever deployed, in the same commit series
as the fixes:

**`docs/deployment.md`** — what was actually run, so the next person can re-run it:

1. The live URL and the engine version.
2. Toolchain and package versions (SDK, `tps`, `Curiosity.CLI`, `Curiosity.Library`,
   `Curiosity.FrontEnd*`, Tesserae, Transpose).
3. The sequence — the commands of §2, with this application's paths, copy-pasteable.
4. **What had to change** for this server version — a table of area · symptom · fix. It is the
   most useful table in the file for the next upgrade.
5. The AI layer: the assistant definition, its tools and agents, the sandbox, what is excluded
   from the AI and why.
6. **Verification** — a table of check · result, with real numbers: node and edge counts, import
   result, compile status, `_Mentions` counts, every page and node type visited, the chat and
   sandbox turns in one line each, and the retrieval table.
7. Known gaps, each pointing at `TODO.md`.

**`TODO.md`** — a checklist of pending actions and improvements, grouped by area (Data and
connector; Definitions and endpoints; Retrieval quality; AI layer and sandbox; Front end;
Deployment and operations). Each item says **what**, **why** (the evidence: a number, a screen,
an error) and **where** (files). Tick items off in the commit that does them; link the file from
the README and from `docs/deployment.md`. A gap found during a deployment and written nowhere is
found again at the next one.

A README "Live demo" section with the link and a screenshot table (`docs/screenshots/`) is the
third, smaller record.

---

## 7. Iterating without a full re-ingest

- **`--dry-run` for mapping changes.** Every handler runs, counters add up, nothing is written
  and no workspace is needed. This is the development loop.
- **Bump one handler's `Version`** to re-ingest just that input type.
- **`--no-attachments`** for everything that is not about the attachments; rendering them is
  the only slow step by an order of magnitude.
- **Front-end changes need no re-ingest at all** unless the schema moved — rebuild Release and
  upload.
- **Endpoint and tool changes need a re-import**, not a re-ingest — and then §3 again.
- **A changed pipeline mapping or spotter needs the parser indexes recreated** (§2 step 7).

---

## 8. Before a demo

- [ ] Full rebuild from the repository done on the demo machine, from scratch
- [ ] The five-minute narrative walked end to end, on this data, today
- [ ] Every record named in the narrative opens, and looks right
- [ ] The assistant answers the narrative's question correctly **twice in a row**
- [ ] Every product feature the demo does not use is switched off in the interface settings
- [ ] Node visibility set so the demo user — not just an admin — sees every type
- [ ] The AI provider is configured on this environment, reachable and has quota
- [ ] Attachments are present (they are the first thing skipped and the first thing noticed)
- [ ] No raw UIDs, no empty facets, no card with a broken label anywhere on the path
- [ ] A fallback: a screenshot or recording of the narrative, in case the network is not there

---

## 9. Checklist

- [ ] Ingest before importing endpoints, always; one zip for the whole import
- [ ] Parser indexes recreated after a mapping change; `_Mentions` edges confirmed
- [ ] `compile-status` clean **and** every tool's function list non-empty (consult included)
- [ ] `chatai/sandboxes` clean; every endpoint called once
- [ ] Front end built Release and uploaded; every route and every node type opened in a browser
- [ ] Export round-tripped into the repository and re-imported once
- [ ] Retrieval measured against ground truth, per route
- [ ] `docs/deployment.md` and `TODO.md` written, with real numbers
- [ ] Schema constants regenerated and committed after every schema change
- [ ] Everything the export does not carry is in the definitions README, with a note
- [ ] A rebuild-from-scratch sequence exists and has been proved
- [ ] Each layer verified before blaming the one below it
