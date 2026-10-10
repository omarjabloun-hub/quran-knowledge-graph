---
name: curiosity-code-endpoints
description: How to write Curiosity server-side code endpoints — the definitions file format and attribute header, AccessMode and ReadOnly, the injected N/E/Q()/Graph identifiers, shared DTO libraries via ImportEndpoint, returning values not JSON strings, traversing instead of re-looking-up, and the UID-everywhere wire discipline. Use when adding or changing anything under code/endpoints.
---

# Code endpoints — the server side of the application

An endpoint is a C# script stored in the workspace, addressed by path, and executed in a
scope that hands it the graph, a query builder, the request body and the current user. It is
where every query, projection and user-decision write lives.

---

## 1. The file format

One file per endpoint, at `code/endpoints/<path>.cs` in the definitions folder. Attribute
lines first, then a **blank line**, then the code — no class, no method, no `using`s.

**The attribute lines are the first lines of the file** — before any comment, any licence
header, any explanation. The importer reads the header from the top; a file that opens with a
comment block is rejected with `Invalid endpoint export, missing key attributes` (`Invalid tool
export, missing UID…` for a tool). Put the explanatory comments *below* the header.

```csharp
[endpoint: Curiosity.Endpoints.Path("app/pawo/confirmed-impacts")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

var req     = Body.FromJson<TD.CaseRequest>();
var caseUID = req?.CaseUID ?? default(UID128);

if (caseUID == default(UID128))                    return BadRequest("caseUID is required");
if (!Graph.TryGet(caseUID, out var node)
    || node.Type != N.Case.Type)                   return NotFound($"Case '{caseUID}' not found.");

var uids = Q().StartAt(caseUID).Out(edgeType: E.ConfirmedImpact).AsUIDEnumerable().ToArray();

return new TD.ConfirmedImpactsDto { Uids = uids };
```

- **`Path`** is how the endpoint is resolved. It must match the constant the front end calls
  it by, exactly.
- **`AccessMode`** is one of `Unrestricted`, `AllUsers`, `AccessControlled`, `AdminOnly`.
- **`ReadOnly`** marks an endpoint that only queries. The runtime then hands it a read-only
  scope and a write attempt fails loudly. **Every write endpoint must NOT carry it** — this
  is a run-time failure, not a compile-time one, so it is easy to ship.

Available identifiers inside the body: `Body`, `Graph`, `Q()` / `Query()`, `CurrentUser`,
`Logger`, `CancellationToken`, the AI scopes, and a secret accessor.

---

## 2. `N` and `E` are injected — never write a string literal

The workspace prepends generated `N` and `E` helper classes to every endpoint script before
compiling it. So it is `N.Case.Type`, `N.Case.Status`, `E.InCategory` — **including for the
types the graph owns**, e.g. the user type's user-name field and the attachment edges.

A literal `"Status"` that no longer matches the schema returns nothing, forever, in silence.
`N.Case.Status` fails the import instead.

The corollary is an ordering rule: an endpoint naming a node type the connector has not
created will not compile. **Ingest before importing endpoints.**

**The injected `N` is the graph's, not the front end's.** It has one class per node type and
nothing else, so a set the front end's emitter adds (`Groups.DocumentTypes`, or an older
`N.DocumentTypes`) does not exist here — keep the server's list in the helper library (§4). The
graph-owned types are spelled with their underscore: `N._User`, `N._FileEntry`,
`E._HasAttachment`, `E._Mentions`, `E._AppearsIn`. And a `[Timestamp]` property has **no**
constant worth reading: it is the node's `Timestamp`, not a field (§3a).

---

## 3. Everything crossing the wire is a `UID128`

Requests carry `CaseUID`, `TargetUID`, `UserUID`. Responses carry `Uid`, `CategoryUID`,
`InboxUIDs`. Two reasons, both load-bearing:

- **The endpoint stops guessing.** Confirming a decision against a document identifier meant
  trying each of eight candidate node types in turn to work out what the string referred to. A
  UID says.
- **The response is directly renderable.** A list of UIDs goes straight into a neighbour-links
  component or a search area scoped to those UIDs, and **every row is drawn by that node
  type's own registered renderer** — so no title is ever copied through the wire to go stale,
  and a mixed-type list needs no special handling.

Use the `UID128` type itself in the DTOs on **both** sides. In the front end a `UID128` is a
JS string at run time; server-side it is a struct whose converter reads and writes the same
plain string. Identical bytes on the wire, and a compile error when a UID reaches the wrong
parameter.

---

## 3a. The scope's API, as 26.10 compiles it

The script runs against the workspace's scoped graph, not the connector's `Curiosity.Library`
client, and the two differ in exactly the places a model reaches for first. Every row below was
a compile or run-time failure in a deployed application.

| Write | Not | Why |
| --- | --- | --- |
| `Graph.TryGet(uid, out ReadOnlyNode n)`, `List<ReadOnlyNode>` | `Node` | reads hand out `ReadOnlyNode`; `Node` is a struct now and has no getters |
| `n.GetInt(N.X.Count)` on an `int` field | `GetString` on it | a getter of the wrong type throws `Could not find key 'Count' in schema X` |
| `n.Timestamp` (a `Time`: `.ToDateTimeOffset()`, or cast to `DateTimeOffset`) | `GetString(N.X.Released)` for a `[Timestamp]` property | the timestamp property is not a field |
| `Out(N.X.Type, E.Y)` / `Out(edgeType: E.Y)` | `Out(E.Y)` | the single positional argument is a **node type**; an edge passed there returns nothing |
| `Out(types, null, new[] { E.Y })` | `Out(types, E.Y)` | there is no `Out(string[], string)` overload (`CS1503`); the array form is include-types, exclude-types, edges |
| `CurrentUser` (already a `UID128`) | `CurrentUser.UID` | |
| `UID128.TryParse(s, out var uid)` | `UID128.IsValid(s)` | `IsValid` is gone |
| `Mosaik.GraphDB.IQuery`, `ReadOnlyNode` parameters in helpers | `dynamic` | `dynamic` compiles and then throws `RuntimeBinderException` (`'object' does not contain a definition for 'ToArray'`) on the first call — a home page that 500s for every user |
| pass `Q()` / `Graph` into a helper | call `Q()` inside a `static` helper class | the library's static classes cannot see the script's scope (`An object reference is required`) |
| `Q().StartAt(uid)` | `IQuery.V(uid)`, `Where(field, value)` | not on the server's `IQuery`; filter with a predicate on `AsEnumerable()` |

**Writes lock, mutate, commit.** The scoped graph has no `Link`, `Unlink` or
`CommitPendingAsync` — those belong to the connector. Take both ends of an edge under one lock,
change them, commit both:

```csharp
var (a, b) = await Graph.TryGetLockedAsync(caseUID, targetUID);   // ONE call: two calls can deadlock
try
{
    a.RemoveUniqueEdge(E.RejectedImpact,   b);  b.RemoveUniqueEdge(E.RejectedImpactOf,  a);
    a.AddUniqueEdge   (E.ConfirmedImpact,  b);  b.AddUniqueEdge   (E.ConfirmedImpactOf, a);
}
finally
{
    await Graph.CommitAsync(a, b);
}
```

Never lock the same UID twice in one call (a self-edge request deadlocks), and remove **both
directions** of an edge you are replacing — removing only the forward edge leaves the old
decision counting on the other side. A property write is the same shape with one node:
`var n = await Graph.TryGetLockedAsync(uid); n[N.X.Field] = v; await Graph.CommitAsync(n);`
(`GetOrAddLockedAsync(type, key)` creates one).

---

## 4. DTOs — declared twice, identically

Put the shared DTO types in a library endpoint (`app/_lib/helpers`), marked
`AccessMode("AdminOnly")` so it fails closed and cannot be called as an API. Import it into
every endpoint with `//ImportEndpoint("app/_lib/helpers")`, which appends the imported source.

Declare **the same classes again, identically**, in the front end's `Api/Dto.cs`. Both ends
serialise with the same default resolver, so the two declarations literally *are* the wire
format — and a field renamed on one side stops compiling instead of deserialising to null.

> Add a field to one, add it to the other. This is the discipline that replaces a shared
> assembly you cannot have across the Transpose boundary.

Also put in the library anything several endpoints agree on: the list of node types that
count as candidates (the server's own copy of the front end's `Groups` sets), the status
vocabulary, shared read helpers — typed, taking the `IQuery` or the node they work on.

---

## 5. Return a value, not a JSON string

The controller serialises whatever the script returns. Returning `x.ToJson()` sends it as
`text/plain` and — worse — lets the returned shape drift from the DTO the front end
deserialises.

This matters most for similarity: **return the engine's own result object unchanged**. That
is the type the front-end search area consumes, and a hand-built projection of it
deserialises into an empty result with no error anywhere. See
`curiosity-similarity-scenarios`.

---

## 6. Traverse, do not re-look-up

The graph is the point. Where a property *names* something that is also on the other end of an
edge, use the edge:

```csharp
// the subject of a case
var subject = Q().StartAt(caseUID).Out(N.Change.Type, E.CaseOf).AsUIDEnumerable().FirstOrDefault();

// what a user owns — not by comparing a name
var mine    = Q().StartAt(userUID).Out(N.Case.Type, E.HasAssignment).AsUIDEnumerable();

// rules that bind this case, via its category
var rules   = Q().StartAt(caseUID).Out(N.Category.Type, E.InCategory)
                                  .Out(N.Rule.Type,     E.HasRule).AsUIDEnumerable();
```

This is what guarantees that a dashboard count and the list behind it cannot disagree: both
are the same traversal. A property-based version of either is a second copy of the fact.

---

## 7. The endpoint set — the shapes that recur

Every application of this kind ends up with roughly these:

| Shape | Read-only | Notes |
| --- | --- | --- |
| `dashboard/summary` | yes | The counts behind the metric tiles — **and the UID arrays behind each count**, so the tile and the list it opens are the same set |
| `dashboard/performance` | yes | Throughput per period, average duration, outcome split — all computed live |
| `dashboard/users` | yes | The people the app can render as; each dimension returned **twice**, as UIDs (for rendering) and as text (for client-side search) |
| `<unit>/decisions` | yes | Everything already decided, in **one** call, so rows can be badged before the first one draws |
| `<unit>/set-decision` | **no** | One decision for a whole selection, batched |
| `<unit>/matrix` | yes | A cross-tab projection for the summary screen |
| `<unit>/draft-report` | **no** | An AI-drafted written output |
| `similarity/*` | yes | Similarity scenarios; return the engine's result unchanged |
| `rules/applicable` + `rules/add` | yes / **no** | Applicable guidance, and proposing one against the unit |
| `<record>/revisions` | yes | Revision history, newest first, for a comparison screen |

Two design notes that were worth the trouble:

- **Return UID arrays next to every count.** A count whose list is recomputed by a different
  query somewhere else is a count that will eventually lie.
- **Record the negative decision too.** If "confirmed" is an edge and "rejected" is the absence
  of one, a dismissed candidate is indistinguishable from one nobody has looked at — which
  makes a long review impossible to pause and resume. Model both; make them mutually exclusive
  by having the write clear the other.

---

## 8. Errors

- `BadRequest(...)` for a missing or malformed parameter.
- `NotFound(...)` for a UID that does not resolve, or resolves to the wrong node type — check
  the type, not just the existence.
- Distinguish **401** ("we cannot tell who you are") from **403** ("we know, and you may not")
  where you write status codes directly. Clients branch on the difference: a 401 can log a
  valid user out.

---

## 9. Checklist

- [ ] Attribute header on the **first lines**, comments below it, blank line, then code
- [ ] `ReadOnlyNode`, typed getters matching the field type, `node.Timestamp` for the timestamp
- [ ] No `dynamic`, no `Out(string[], string)`, no `Link` / `Unlink` / `CommitPendingAsync`
- [ ] Writes lock both ends in one call, replace both edge directions, and commit
- [ ] `Path` matches the front-end constant exactly
- [ ] `AccessMode` set deliberately; the shared library is `AdminOnly`
- [ ] Write endpoints are **not** marked `ReadOnly`
- [ ] No node type or field name written as a string literal
- [ ] Every identifier crossing the wire is a `UID128`
- [ ] DTOs declared identically in the library and in the front end
- [ ] Returns a value, never a JSON string; similarity returns the engine's own result
- [ ] Relationships traversed, not re-derived from properties
- [ ] Counts ship the UIDs they counted
- [ ] Imported after an ingest has created the schema

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`_lib.helpers.cs`](examples/_lib.helpers.cs) | The shared `AdminOnly` DTO library — requests, responses, shared constants and reads |
| [`dashboard.summary.cs`](examples/dashboard.summary.cs) | A read-only endpoint: traversal over lookup, and shipping the UIDs behind every count |
| [`case.set-decision.cs`](examples/case.set-decision.cs) | A **write** endpoint: no `ReadOnly` marking, batched, mutually-exclusive edges |

The similarity endpoint template lives with its own skill, in [`../curiosity-similarity-scenarios/examples/`](../curiosity-similarity-scenarios/examples/).
