---
name: curiosity-sandboxes
description: How to give a Curiosity assistant a sandbox — an AI tool whose code returns an ISandbox, so the model gets bash, jq, awk and python over a filesystem the tool builds from the graph (a lazy /records mount, /docs, /work scratch, /artifacts with publish) plus search, consult, a read-only query engine and the application's own endpoints as commands. Covers when a sandbox beats a fixed tool, the mount kinds and limits, generating one from N.cs, keeping the answer key out, and verifying it. Use when the assistant has to count, join or compare many records, or when adding or regenerating code/chat-ai-tools/<sandbox>.cs.
---

# Sandboxes — the corpus as files, for the question nobody wrote a tool for

A fixed tool answers the question somebody thought of in advance, and grows a method per
question. A **sandbox** hands the model `ls`, `grep`, `jq`, `awk`, `sort` and `python` over the
data and lets it answer the one nobody wrote down: *how many passed deals per sector*, *which
parts appear in more than three open changes*, *the signals by programme and status*. In the
deployed demos a sandbox turn answered that kind of question with one `jq` over `/records` and
matched the corpus exactly (43 passed deals across six sectors); through search and consult the
same question is dozens of calls, each a chance to miscount.

It is an ordinary AI tool — same editor, access mode, versioning, export, tool picker — whose
code **returns an object implementing `ISandbox`** instead of a class with `[Tool]` methods.
The workspace synthesizes the tool surface from it, runs each call as a bash script in an
in-process interpreter (no host disk, no processes, no network), and keeps a session per chat
**and** tool. Verified against `Mosaik/Graph/src/Sandbox/` (26.10).

```
ASSISTANT ── SearchRecords / consult / custom tools    reading and judging single records
         └── SANDBOX                                    counting, joining, comparing many
                 /records    lazy: one folder per type, one JSON file per record
                 /docs       README.md, schema.md (live counts), use-case.md
                 /work       scratch, kept with the conversation
                 /artifacts  HTML / markdown the agent writes, then `publish`es
                 search · consult · query · app <endpoint>
```

---

## 1. The interface

```csharp
public class AppSandbox : ISandbox
{
    public string Description => "…what the filesystem holds and which questions it answers…";
    public string FunctionName => "app-sandbox";                  // what the model calls; default "sandbox"

    public SandboxMount[] Mounts => new[]
    {
        SandboxMount.Lazy("/records", "…", listDirectory: (scope, dir) => …, readFile: (scope, path) => …),
        SandboxMount.Live("/docs", "…", ReadDocs),
        SandboxMount.Scratch("/work", "…"),
    };

    public SandboxCommand[] Commands => new[]
    {
        SandboxCommand.Search(RecordTypes),
        SandboxCommand.Consult(RecordTypes),
        SandboxCommand.Create("app", "app <endpoint> [json]: …", async call => …),
    };

    public SandboxQueryAccess Query  => SandboxQueryAccess.For(RecordTypes).WithMaxNodes(200);
    public ArtifactType[] Artifacts  => new[] { ArtifactType.Html, ArtifactType.Pdf };
}

return new AppSandbox();
```

Everything but `Description` and `Mounts` has a default.

**Mounts** — a mount is a flat list of files at one absolute single-segment path; directories
are synthesised from the names. Which factory decides when the files are built:

| Kind | Built | Use it for |
| --- | --- | --- |
| `Lazy(path, desc, listDirectory, readFile)` | one directory and one file at a time, as the script walks into them | **the records** — any set too large to name up front. Every other kind returns the whole path list on every script, which for thousands of records costs a full sweep for `pwd` |
| `Live(path, desc, read)` | before every script | small views read out of the graph: docs with live counts |
| `Cached(path, desc, read)` / `Files(path, desc, files…)` | once per session | documentation, a schema, a reference file |
| `Writable(path, desc, read)` | before every script, plus what the sandbox wrote over it | a working copy the agent edits |
| `Scratch(path, desc)` | empty until written | `/work` |

`SandboxFile.Lazy(path, () => text)` defers a file's content until it is read. A lazy
directory that does not exist is `SandboxDirectory.None` (so `ls` reports no such path), which
is a different answer from an empty one.

**Writes never leave the sandbox.** They land in the session's overlay and are kept with the
conversation; nothing reaches the workspace. A sandbox that should *change* something gets a
`SandboxCommand` whose name says what it does — never make a file write mean it.

**Commands:**

- `SandboxCommand.Search(types)` — the workspace's own search tool as `search <words…> [--type T]
  [--page N]`, run as the person chatting, fenced to the types you name.
- `SandboxCommand.Consult(types)` — the workspace's consult tool as `consult <id> [--search …]`,
  reading through the same readers the chat uses; an id of any other type is refused.
- `SandboxCommand.Create(name, usage, call => …)` — your own. `call.Arguments`, `call.StdinText`,
  `call.Scope` (the tool scope), `call.Files` (read and write the sandbox's own files),
  `call.Fail(message)` for a usage error (non-zero exit, so `&&` stops). `usage` is the only
  documentation the model gets — write it as a manual-page line.

**Query** — `SandboxQueryAccess.For(types)` gives the agent `query '<IQuery chain>'`: the same
fluent chain an endpoint is written with, interpreted (not compiled), read-only, as the user,
with the type allow-list re-applied after every step. A chain starting at a type outside the
list is refused by name — "there are none" and "you may not look" must not read the same.
`null` (the default) means no query engine.

**Artifacts** — declaring any `ArtifactType` adds the `/artifacts` mount and a `publish`
command: the agent writes `report.html` or `report.md`, publishes it, and the chat shows a
document the reader opens beside the conversation (HTML in a frame with no network, markdown as
a PDF). Leave it empty for a sandbox that only answers questions.

**Limits** (`SandboxDefaults`; a sandbox's own values are clamped to the ceilings): 60 s per
script by default, 300 s at most (`MaxDurationSeconds`); 256 KB of output by default, 4 MB at
most (`MaxOutputBytes` — what the script prints is what the model reads, so it is a context
budget); 50 nodes per `query` section by default, 500 at most. Sessions stay hydrated 30 idle
minutes; `KeepSessionBetweenCalls => false` makes every call start clean.

The workspace validates the declaration (a duplicate mount, a command shadowing a shell
built-in, a home directory under no mount) and reports it per sandbox at
`GET /api/chatai/sandboxes` as `DefinitionError` — compiling proves only that the code builds.

---

## 2. Generate it from the schema

Write the sandbox once, as a template, and generate the tool from `N.cs` and the endpoint folder
— the reference set in [`examples/`](examples/) is what the eight deployed demos used:

| File | Goes to | What it is |
| --- | --- | --- |
| [`sandbox-template.cs`](examples/sandbox-template.cs) | `tools/sandbox/sandbox-template.cs` | the commented sandbox, with `@@…@@` placeholders |
| [`gen_sandbox.py`](examples/gen_sandbox.py) | `tools/gen_sandbox.py` | fills it: record types from `N.cs`, read-only endpoints from `code/endpoints`, the README and the use case |
| [`gen.sh`](examples/gen.sh) | `tools/sandbox/gen.sh` | the call, with this application's settings and the ground-truth stripping |
| [`sandbox.cs`](examples/sandbox.cs) | — | what it generates for the template domain |

What the template builds:

- **`/records/<Type>/`** — `_index.tsv` (id, label, uid: the file the agent greps first) and one
  `<id>.json` per record: every field (the query engine's own JSON) plus its neighbours of the
  record types, capped at 150. Files are named by the node's **key** when that is a usable file
  name, else by uid, so `cat /records/Change/CHG-166204.json` works.
- **`/docs`** — `README.md` (the layout, the commands, the rules), `schema.md` (each type with
  its live count and the field its files are named by) and `use-case.md`.
- **`app <endpoint> [json]`** — this application's **read-only** endpoints, called as the user:
  the numbers and ranked lists the screens show, so the agent does not recompute a metric by hand
  and get a different one. The generator lists only endpoints whose header says `ReadOnly`, that
  are not `AdminOnly`, not a `_lib`, and not excluded.

Re-run `tools/sandbox/gen.sh` after the schema or the endpoint set changes, and commit the
generated `code/chat-ai-tools/<uid>.cs` with it. Give it a readable UID that round-trips
(`curiosity-workspace-configuration` §5) — the chat view and the assistant pin it by that UID.

---

## 3. The answer key stays out

A sandbox is the widest door in the AI layer, so the ground-truth rule
(`curiosity-ai-tools-and-agents` §7) is enforced here three ways:

- `--exclude-types <ground-truth types>` — absent from `/records`, from `search`, `consult` and
  `query` alike (all three are fenced to the same `RecordTypes`), and dropped from every record's
  neighbour list.
- `--exclude-endpoints <evaluation endpoint>` — `app` cannot call the scorer.
- `--usecase` gets a **copy of the brief with the ground-truth and hero-scenario sections
  removed** (`gen.sh` does it with two regexes and an assertion). The brief names the planted
  findings; a sandbox serving it verbatim hands the model the answers.

---

## 4. Wire it in, then prove it

1. **Assistant** — add the sandbox's UID as an `[assistant: Curiosity.Assistants.Tool(…)]` and a
   line in the prompt: what it is for (counting, joining, comparing many records), to read
   `/docs/README.md` first, and never to use it to read one record (consult does that).
2. **Chat view** — pin it with the other tools (`curiosity-chat-experience` §2), and offer one
   welcome prompt that is a counting question.
3. **Import, then check** — `GET /api/build/compile-status` has `FailingIds: []`;
   `GET /api/chatai/sandboxes` lists it with `DefinitionError: null`;
   `GET /api/chatai/tools/get/function-names?toolUID=<uid>` is not `[]`.
4. **One real turn** — ask a counting question whose answer you can compute from `data/`, and
   compare. `chatai/tools/test` answers 500 for a sandbox (it needs a conversation), so the chat
   turn *is* the test.

---

## 5. Checklist

- [ ] The tool's code returns an `ISandbox`; header first, `return new X();` last
- [ ] `/records` is a `Lazy` mount; `/docs` is `Live`; `/work` is `Scratch`
- [ ] `search`, `consult` and `query` are fenced to the same record types
- [ ] Ground-truth types, the evaluation endpoint and the brief's ground-truth sections excluded
- [ ] Generated from `N.cs` by `tools/sandbox/gen.sh`; regenerated after a schema change
- [ ] A readable UID that round-trips, pinned by the assistant and the chat view
- [ ] `chatai/sandboxes` shows `DefinitionError: null`; a counting turn matched the corpus
