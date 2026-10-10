---
name: curiosity-ai-tools-and-agents
description: How to build the AI layer of a Curiosity application — which work belongs to an assistant, an agent, a consult reader, a custom tool or a sandbox; writing per-node-type consult readers; the Search Records tool; custom tools over graph state; agent and assistant definitions; and the context rules that decide whether an answer is grounded and the answer key stays out of reach. Use when adding or changing anything under code/chat-ai-tools, code/agents or code/ai-assistants.
---

# The AI layer — assistants, agents, tools, readers

Four layers, each with a job. Conflating them is what produces an assistant that
confidently invents identifiers.

```
ASSISTANT   one definition (code/ai-assistants/): the system prompt and the tools and
            agents it carries. Knows the domain, the corpus, the flow, what to refuse,
            and which agent answers which shape of question. The method.
    │
    ├── AGENT        a sub-task with its own prompt and its own tools. Invoked by name for
    │                a multi-document judgement that deserves its own reasoning budget.
    │
    ├── CONSULT      per-node-type readers. "Read this record in full." Any record the
    │   READERS      user can access, by id. No reader for its type ⇒ "not consultable".
    │
    ├── SEARCH       Search Records — your own tool over the workspace search. Reaches
    │   RECORDS      records that are NOT in the conversation, and hands back their ids.
    │
    ├── CUSTOM       your own tools, over your own graph queries. Workload, state,
    │   TOOLS        aggregates — the questions that are about the user, not the corpus.
    │
    └── SANDBOX      the corpus as files, with bash, jq and python over it, for the
                     open-ended question no fixed tool answers → curiosity-sandboxes
```

**Every tool, agent and assistant file opens with its attribute header** (`[tools: …]`,
`[agent: …]`, `[assistant: …]`) — before any comment. The importer reads the header from the
top and rejects a file that does not start with it (`Invalid tool export, missing UID…`).

---

## 1. Consult readers — the grounding layer

The consult tool is the one built-in an admin can extend, and what is editable is *only* the
per-node-type readers. The tool surface — the entry point, UID parsing, access and
chat-context checks, and the switch on node type — is generated around your methods at
compile time. So the file declares **no class and no tool attribute**, only methods following
a fixed schema:

```csharp
object Consult<NodeType>(ToolScope scope, UID128 uid) { … }
```

Rules:

- **It lives at `code/chat-ai-tools/ConsuLt111111111111111.cs`, under that UID.** The server
  wraps readers into the consult surface only for the built-in's UID; readers in a file of your
  own are an ordinary tool with no functions.
- **Any record the user can access is consultable by id** — an attachment, a search hit, a uid
  a custom tool returned. Access control is the boundary (the generated entry point checks
  `CanAccess` before dispatching), not the conversation context.
- **One reader per node type the assistant should read.** A type with no reader is reported to
  the model as *not consultable*, and the model then answers by guessing from the title. The
  deliberate exception is the ground-truth type (§6): no reader, on purpose.
- **Include the revision types.** A comparison screen attaches a revision; without a reader
  the model can only read half the pair.
- **Helper methods must not start with `Consult`** — that prefix is what marks a reader, and a
  helper carrying it fails the schema check at save time.
- **Readers run in the tool scope**: `scope.Graph`, `scope.Q()`, `ReadOnlyNode` — never the bare
  `Graph` / `Q()` of an endpoint, never `Node`. The endpoint rules of `curiosity-code-endpoints`
  §3a apply: a `[Timestamp]` property is not a field (return `timestamp = node.Timestamp`
  instead of listing it), typed getters must match the field, `E._HasAttachment` and
  `N._FileEntry.OriginalName` / `ContentType` / `Size` are the attachment names.
- **A broken consult is silent.** `build/compile-status` does not list built-ins, so a reader
  that fails to compile leaves the tool exposing no functions and every answer becomes a guess.
  Check `GET chatai/tools/get/function-names?toolUID=ConsuLt111111111111111` is not `[]` after
  every import (`curiosity-delivery` §3 has the re-save that prints the compiler's message).

**What a reader should return** — three blocks, and all three matter:

| Block | Why |
| --- | --- |
| `fields` | Every named field that has a value; skip the empty ones rather than sending nulls |
| `related` | What this record points at, grouped by kind — categories, parts, components, locations, systems, applicability. **This is the half a reader skims past and the half a model cannot infer from prose**, and it is exactly what a "do these two overlap?" question turns on. Cap it per kind: the answer is about the overlap, not the inventory |
| `attachments` | The published files hanging off the node, each with its uid, name, content type and page count, **marked consultable**. Consulting the record then consulting its attachment is how the model reads the document as published rather than as a field dump |

Also set a human-readable display name for the tool call (`"Reading 'Cargo Door Surround
Reinforcement'"`), so the user watching the conversation can see what it is doing.

**The attachments point is worth restating.** Where the published artefact is what a human
actually reads, a model that only ever sees fields will only ever reason about fields, and the
detail that decides the answer usually lives in the paragraph that was rendered, not in the
metadata.

---

## 2. Search Records — how the assistant reaches what it was not handed

The assistant needs one way to find a record that is not in the conversation, by identifier or
by subject, and get back an id consult can read. The workspace's built-ins do not give it one
for a corpus of records:

- **Search Files** (`AiTooLsEarCh1111111111`) searches *files* only. A corpus of graph records
  has none, and the model then confuses its `ConsultFile` with `consult`.
- **`search`** (`LLMSearCh1111111111111`) interprets the question through an agent over the
  `llm-search.json` types. In the deployed demos it found nothing for exact identifiers, and one
  chat looped on a document id for seventeen minutes.

So ship **Search Records** (`examples/search-records.cs`, UID `SEARCHRecords111111111`): it runs
the ordinary workspace search **as the user** (`scope.Graph.CreateSearchAsUserAsync`, so access
control and the configured indexes apply exactly as in the search box) and returns each hit's
`id`, `type` and `label`. Consult then reads any id it returns. Give it to the assistant and to
every agent in place of Search Files, and say in both prompts that `SearchRecords` is the search
and that identifiers search better than prose. An unknown `nodeType` from the model should fall
back to an unfiltered search and say so, not fail the call.

It only finds what is **indexed** — so it is also a leak path for anything indexed that should
not be read (§6).

---

## 3. Custom tools — the operational layer

Questions about *the user's own work* ("what is on my desk", "what is urgent", "what could I
pick up") must not go through search. Search ranks documents by text; these read edges.

Write them as a normal class with tool-attributed static methods, and **end the script by
returning an instance** — a script that only declares the class compiles and loads as
`Can't parse tool (Tool is null)`, with no functions:

```csharp
[tools: Curiosity.ChatAITools.UID("<random 22-char uid>")]
[tools: Curiosity.ChatAITools.DisplayName("Case Tools")]
[tools: Curiosity.ChatAITools.Description("…when to use this, and what it returns…")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

[ToolSystemPrompt("""
                  Workload questions are answered with these tools, never with a search.
                  Every row carries the case's uid: consult it to read the case in full.
                  """)]
public class CaseTools
{
    [Tool("Lists the cases open on an engineer's desk … Returns uid, id, title, status, …")]
    public static string ListInbox(ToolScope scope,
        [Parameter("Whose inbox — user name or full name. Empty for the current user.", required: false)] string engineer = null,
        [Parameter("Maximum to return (default 25).", required: false)] int limit = 25) { … }

    [Tool("…")]
    public static string Explain(ToolScope scope,
        [Parameter("The case's uid, exactly as a tool returned it.", required: true)] string uid) { … }
}

return new CaseTools();
```

What the 26.10 tool scope requires:

- **Every `[Parameter]` says `required:`** — `true` or `false`, explicitly. The one-argument form
  no longer compiles into a tool.
- **`UID128.TryParse(s, out var uid)`** to validate an id the model passed; `UID128.IsValid` is
  gone. Answer an invalid id with a message saying ids are 22 characters and case-sensitive.
- **`scope.CurrentUser` is the user's `UID128`** (not `.UID` of anything); reads go through
  `scope.Graph` / `scope.Q()` and hand out `ReadOnlyNode`; no `dynamic` — see
  `curiosity-code-endpoints` §3a for the whole table.

Four things this project learned:

- **Compute exactly what the dashboard computes**, from the same edges with the same
  definitions. The user is looking at those counters on the home page while they talk to the
  assistant; an assistant that quietly counts differently is worse than one that cannot count.
- **Return the uid on every row**, next to enough to answer the question outright. Consult reads
  any record the user can access by id, so the uid is how the model goes from a row to the full
  text — say so in the description and in the `[ToolSystemPrompt]`.
- **The description is the routing logic.** Say when to use it, when *not* to (i.e. instead of
  search), and list the fields it returns. This is read by the model on every turn.
- **Mint a random UID** for a tool only this application has. A readable, fixed UID is right for
  a tool every application ships under the same id (Search Records, a sandbox) — but not every
  22-char string round-trips through the UID parser (`BastionExposure1111111` does not), so check
  `UID128.Parse(s).ToString() == s` before using one.

---

## 4. Agents

An agent is a sub-task with its own prompt, its own tool set and its own reasoning budget,
invoked by name. Use one when the question is a **multi-document judgement** — not for a
lookup the assistant can settle with one consult.

```csharp
[agent: Curiosity.Agents.UID("<uid>")]
[agent: Curiosity.Agents.Name("Impact Analysis Agent")]
[agent: Curiosity.Agents.Description("One sentence for the people maintaining it.")]
[agent: Curiosity.Agents.Tool("SEARCHRecords111111111")]
[agent: Curiosity.Agents.Tool("ConsuLt111111111111111")]

…the agent's prompt, in markdown…
```

- **Give it Search Records and consult** — consult is what puts the readers in its hands — plus
  the custom tools its judgement needs. Not Search Files (§2).
- **Leave `ChatTask` out** (or the all-ones sentinel `1111111111111111111111`): the agent then
  runs on the workspace's default provider. A provider task UID copied from another environment
  names nothing there.
- **Everything below the header is the prompt**, comments included: an `<!-- … -->` block of
  maintainer notes is sent to the model with the rest. Put the note in `Description`.

A good agent prompt has five parts, in this order:

1. **The job, in one sentence**, and explicitly what it is *not* (it judges; it does not
   design, cost or approve).
2. **What the corpus holds** — the types and what each carries. The model cannot infer this.
3. **How to work**, as numbered steps. The two that matter most:
   - *consult everything before judging anything* — "a verdict about a document you have not
     read is a guess with a confident tone";
   - *look at the structured overlap first, then read for the conceptual overlap*. Shared
     identifiers are the half a reader skims; shared meaning with no shared words is the half
     a keyword search misses. Naming both stops the model doing only one.
4. **What to report**, in a fixed order: verdict first, then severity, then **the named
   mechanism**, then **the evidence as quotations from each document**, then what has to
   happen next.
5. **How to be uncertain**: say "uncertain", say what would settle it. "A confident wrong
   verdict is worse than an honest gap" — and mean it, because this is what a decision gets
   taken on.

Two agents covered the reference implementation: one that judges impact across candidates,
one that reports what changed between two revisions. That is a good default pair for any
"analyse a change against a corpus" application.

---

## 5. The assistant — a definition, not a pasted prompt

The assistant is a file in the definitions folder, imported with everything else:
`code/ai-assistants/<name>-<UID>.cs`, a header and then the system prompt in markdown.

```csharp
[assistant: Curiosity.Assistants.UID("defaULtAiASSistaNt1111")]
[assistant: Curiosity.Assistants.Name("Analysis Assistant")]
[assistant: Curiosity.Assistants.PublicAccess("true")]
[assistant: Curiosity.Assistants.Tool("SEARCHRecords111111111")]
[assistant: Curiosity.Assistants.Tool("ConsuLt111111111111111")]
[assistant: Curiosity.Assistants.Tool("<workload tool uid>")]
[assistant: Curiosity.Assistants.Tool("<sandbox uid>")]
[assistant: Curiosity.Assistants.Agent("<impact agent uid>")]

You are the **Analysis Assistant**. …
```

- **The UID** is the workspace's default assistant (`defaULtAiASSistaNt1111`) when this
  application is the workspace's whole purpose, or a minted one when the front end pins its own.
  Either way the front end names the same UID (`curiosity-chat-experience`).
- **The header pins its tools and agents**, so an import configures what it can use. The chat
  view pins the same set again (`curiosity-chat-experience` §2); the two are deliberately
  redundant. A prompt pasted by hand drifts from the repository the first time someone edits it
  in the admin UI, and a fresh environment does not have it at all.
- `Curiosity.Assistants.Skill("<uid>")` adds a skill the same way.

One system prompt, and it is the whole method. Sections that earned their place:

| Section | Content |
| --- | --- |
| **Identity + job** | Who it is, and the exact step of the process it supports |
| **What the workspace holds** | A table of record types and what each is. Plus: *never invent an identifier — one you did not read in a tool result does not exist* |
| **Your tools** | Each one by the name the model sees (`SearchRecords`, `consult`, …): what it does, when to use it, and the trap. Be explicit that attached items are listed with an id and a title but **not** their content, that consult reads **any** record id the user can access, and that ids are exact-length and case-sensitive — copy them verbatim, never guess |
| **The flow, end to end** | **One worked example**, numbered, with real-shaped identifiers, from question to answer. This is the single most effective part of the prompt |
| **In scope** | A table of question shapes → which tool or agent answers it. Cover the workload questions, the read questions, the judgement questions and the counting / comparing questions the sandbox is for separately |
| **Out of scope** | Named, with the instruction to say so in one sentence and stop — no apology, no length. Include anything safety-, legally- or contractually-determining, with "you inform that decision, you never make it" |
| **How to answer** | Lead with the answer; the user has the documents open. Quote, do not paraphrase. Name the identifier two records share. **Write identifiers as plain text, never in backticks** |

**Plain-text identifiers are what make the reply navigable.** The chat parses every reply
against the corpus (`ParseAs`) and turns each identifier into a link to its record — except
inside a code span. A model copies the prompt's own formatting, so a prompt that shows ids in
backticks gets every id back in backticks and not one link. Write the prompt's examples
without them, and say it in "How to answer".

---

## 6. Grounding rules — the ones that decide whether it works

- **Attach the evidence, do not describe it.** A question typed into a bare chat makes the
  assistant hunt for documents by title. The same question with the nodes attached lets it
  consult them by id. Every AI entry point in the UI should attach the records the question is
  about — see `curiosity-chat-experience`.
- **Attach the subject too, not only the candidates.** A verdict about a change the assistant
  cannot read is a guess.
- **Every node type the assistant should read needs a reader.**
- **Prefer identifiers over prose when searching**, and say so in the prompt.
- **Say what the model may not invent.** Identifiers, agent names, tool names.

---

## 7. Keep the answer key out of reach

A demo corpus carries its ground truth — the findings a review is expected to make — so the
retrieval and the agents can be scored. The score means something only while the assistant
**cannot read the answer key**: an agent that consults the expected findings and repeats them
is a tautology that looks like a result. The key leaks through more doors than the obvious one;
check all of them before the first chat turn and after every change to the AI layer:

- [ ] **Consult readers** — no reader for the ground-truth type, and no reader of another type
      that returns its fields, its counts (`ExpectedFindingCount`) or traverses its edges.
- [ ] **Indexes** — no full-text, embedding or parser index on the ground-truth type or its
      fields. Search Records finds whatever is indexed, as does the sandbox's `search`.
- [ ] **Linking and spotters** — no spotter built from it, no linking rule into it.
- [ ] **Custom tools and endpoints the AI can call** — none returns finding counts or targets;
      the sandbox's `app` command excludes the scoring endpoint.
- [ ] **The sandbox** — the ground-truth types excluded from every mount and from `search`,
      `consult` and `query` (`curiosity-sandboxes`).
- [ ] **Documents handed to the model** — the sandbox's `/docs` copy of `docs/use-case.md` with
      the ground-truth and hero-scenario sections stripped; no published attachment that prints
      the expected findings; no prompt or agent that names them.
- [ ] **The candidate endpoint itself** — reads neither the ground-truth type nor its edges; only
      the evaluation endpoint (or an offline script reading `data/`) does.

The connector may still write the ground truth into the graph — the evaluation endpoint and the
review screen need it — but nothing on the AI side can reach it.

---

## 8. Checklist

- [ ] Every tool, agent and assistant file opens with its attribute header
- [ ] Consult readers in `ConsuLt111111111111111.cs`, one per type the assistant reads, revisions
      included; its function list checked non-empty after import
- [ ] Every reader returns `fields`, `related` and consultable `attachments`
- [ ] No helper method named with the reader prefix
- [ ] Search Records shipped and given to the assistant and agents; Search Files is not
- [ ] Custom tools end with `return new X();`; every `[Parameter]` says `required:`
- [ ] Custom tools compute exactly what the dashboard computes, from the same edges
- [ ] Custom-tool rows carry the uid, and the prompt says consult reads it
- [ ] Tool descriptions say when to use **and when not to** use each tool
- [ ] Agents carry Search Records and consult; no foreign `ChatTask`; no `<!-- -->` in a prompt
- [ ] The assistant is a definition under `code/ai-assistants/`, pinning its tools and agents
- [ ] The assistant prompt contains one full worked example and asks for plain-text identifiers
- [ ] Out-of-scope and never-invent rules are explicit
- [ ] The answer-key checklist (§7) passes
- [ ] Custom UIDs are randomly minted (or verified to round-trip) and written down once per side

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`consult-readers.cs`](examples/consult-readers.cs) | The fixed reader schema, and the `fields` / `related` / `attachments` response |
| [`search-records.cs`](examples/search-records.cs) | Search Records: the workspace search as the user, returning id, type and label |
| [`workload-tool.cs`](examples/workload-tool.cs) | A custom tool over graph state, with the tool system prompt and self-sufficient rows |
| [`impact-agent.cs`](examples/impact-agent.cs) | An agent definition and the five-part prompt structure |
| [`assistant.cs`](examples/assistant.cs) | The assistant as a definition: header pinning tools and agents, then the full prompt with its worked example |

The sandbox is its own skill: [`curiosity-sandboxes`](../curiosity-sandboxes/SKILL.md).
