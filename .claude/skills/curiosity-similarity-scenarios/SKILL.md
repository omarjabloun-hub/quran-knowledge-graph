---
name: curiosity-similarity-scenarios
description: How to build ranked candidate lists with the Curiosity similarity engine — scenarios, scored text and graph signals (stored vectors searched exactly, rarity-weighted shared entities), weights, fusion by route, type diversity, rules, two-hop projections, wiring the result into a front-end search area, and the measure-then-tune method against ground truth that took two demos from 20 % to 69 % and 24 % to 42 % recall at 20. Use when writing a "what else is related to this" endpoint, tuning a ranking, or explaining why a result surfaced.
---

# Similarity scenarios — ranked candidates, with reasons

This is where the application earns its claim. A similarity scenario fuses several
independent signals into one ranked list, keeps the **per-signal attribution** so the UI can
say *why* a result surfaced, and applies hard filters after fusion.

Write scenarios. Do not hand-roll a loop that scores candidates — you lose the attribution,
the candidate management and the front-end integration all at once.

---

## 1. The shape

```csharp
var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o
        .MaxCandidates(400)
        .MaxCandidatesPerSignal(150)
        .TrackProgress(async p => await RelayStatusAsync(p.Message)))

    .AddSignal("SectionText", s => s
        .Describe("Its sections read like the change's reason, description and effects")
        .Weight(1.0f)
        .From(ctx => Scaled(TextScores())))                 // IEnumerable<ScoredUID>, §2

    .AddSignal("SharedPart", s => s
        .Describe("Shares a rarely shared part number with the change")
        .Weight(0.9f)
        .From(ctx => Scaled(SharedRare(N.PartNumber.Type, E.UsesPart, E.UsedByDocument))))

    .AddSignal("ReferencedByChange", s => s
        .Describe("The change's own text cites it")
        .Weight(0.8f)
        .From(ctx => ctx.Graph.Query().StartAt(seedUID)
            .Out(TD.ImpactableTypes, null, new[] { E.ReferencesDoc }).AsUIDEnumerable()))

    .Fuse(Fusion.Sum)

    .AddRule("SamePartition", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query()
            .StartAt(candidates).IsRelatedTo(partitionUID).AsUIDEnumerable()))

    .ExecuteAsync(CancellationToken);

return result;                                   // the engine's own result, unchanged
```

`result.Scores` is `Dictionary<UID128, ScoreInfo>`; `ScoreInfo.Score` is the fused score and
`ScoreInfo.Components` is **signal name → contribution** (a dictionary — read `Key` / `Value`;
older code that read a list of component objects no longer compiles). `AddNegativeSignal`,
`FuseFinal(Fusion.Discount | Subtract | Suppress | Decay(r))`, `FilterAsUser(uid)` and
`TrackTimings()` (fills `result.Timings`) are the rest of the surface. A rule is
`AddRule(name, r => r.Filter((ctx, candidates) => …))` — not `r.Exclude(uid)`.

**Return the engine's result object unchanged.** It is the type the front-end search area
consumes; a hand-built `{ source, hits }` projection of it deserialises into an empty result
with no error anywhere.

---

## 2. Signals

A signal is one independent reason two things might be related. Give each a **name**, a
**`Describe`** (it is what the UI shows the user) and a **weight**.

**Hand the engine scores, not hits.** A signal sourced from `IEnumerable<UID128>` or an `IQuery`
scores every hit **1.0**. Then a category forty records share counts exactly as much as a part
two records share, and `MaxCandidatesPerSignal` trims the ties *arbitrarily*. Compute the score
yourself and use the `From(ctx => IEnumerable<ScoredUID>)` / `FromAsync` overload:

```csharp
new ScoredUID(new TypedUID128(0, uid), score)   // the engine reads only the UID; there is no
                                                // UID128 -> TypedUID128 conversion
```

Scale each scored signal to its own best candidate (best = 1) before the weight applies, so a
signal's `Weight` is the whole of what it can add and the weights are comparable.

### Text signals

The strongest single signal and the **only** one that can reach a record sharing no identifier
at all. Three choices decide whether it works, each measured:

- **Field against field, from stored vectors.** `Graph.Embeddings.GetIndex(nodeType, field)`
  returns the field's `ITextSimilarityIndex`; `index.TryGetVector(uid, out float[] v)` reads the
  vector it already holds for a node (`false` on a `ChunkText` index, which stores none per node);
  `index.FindSimilar(default(UID128), v, count, null, uidsToKeep)` searches another field's index
  with it. Every index on one model shares one vector space, so the seed's *effects* can be
  searched against every manual's *procedure*. One concatenated seed string averages the specific
  risk away; re-encoding the seed as a query (`PredictVectorAsync`, `StartAtSimilarTextAsync`)
  was worse in both demos that tried it, and ten times slower.
- **Exact, not approximate.** Pass `uidsToKeep` = every node of the index's type: below
  `ChatAI.HNSW_INDEX_CUTOFF_BRUTE_FORCE_SEARCH` (1,000 UIDs) the index then scores every vector
  exactly instead of walking its HNSW graph. On a hosted workspace the graph walk returned a
  record's own summary in its own top 5 for **4 queries in 40**; the exact path for 40 in 40.
  Above the cutoff it silently walks the graph again — check your type sizes.
  *Why it misses (engine 26.10, `HNSW` 26.9.5494):* the index segments are built with
  `OptimizeForFiltering = true, Gamma = 12, Mb = 64`. Layer-0 neighbour lists then hold up to
  2·M·Gamma = 768 entries and are only re-sorted by distance when they overflow, but the search
  reads just the first `Mb` = 64 — so nodes added later are never reached. Measured on real
  vectors: ~65–80 records per index reachable whatever its size (top-10 recall 0.14 at 490
  vectors, 0.39 at 5,000); `OptimizeForFiltering = false` or `Gamma = 1` gives 1.00. This is an
  engine bug, not a configuration you can change from a workspace — keep the exact path until
  the engine is fixed and the indexes recreated, then re-run the self-query check below. Search each index
  yourself and keep the max per candidate: a multi-index search keeps the *first* score it sees
  per UID, not the best.
- **Relative, not raw.** Cosine scores sit in a narrow band that moves per seed and mostly
  measures "same partition". Count what a candidate scores **above the seed's N-th best** (or
  subtract its mean similarity to the seed's siblings and standardise per seed-field ×
  candidate-field pair), and sum over the seed fields. In the pharma demo that lifted text-only
  recall from 42 to 60 of 132.

`StartAtSimilarTextAsync(text, …)` is still the tool for free text (a user's question). Without
an index UID it reads **only embedding indexes with `EnableAISearch: true`** — with it off,
every text signal returns nothing and nothing says why. Sections shorter than one chunk need no
`ChunkText`, and a chunked index has no per-node vector to seed from.

### Graph signals

**Weight a shared neighbour by its rarity.** Walk it by hand — the seed's neighbours, then each
neighbour's holders over the reverse edge — and give each shared neighbour
`log(n / df) / log(n)`, `df` being how many candidates of that type hold it, measured **within
the candidate's own type** (rarity among protocols and among reports are different questions,
and a type that carries far more entities would otherwise dominate). Skip an entity carried by
more than half of a type. Sum per candidate, optionally saturate (`1 − e^(−1.5x)`) so eight
common entities cannot outscore one rare one. The candidate must hold the neighbour over the
**same** edge type — in a typical connector an edge exists on one node type only, so such a
signal can only ever return that type.

`HasCommonNeighborsWith(seedUID, nodeType, edgeType)` is the binary version — fine for a first
draft, and the thing to replace first. **This is why metadata is modelled as nodes.** A category
that lives in a property is invisible to both.

One signal per shared dimension, so the attribution names the dimension:

| Signal | Typical weight | Reasoning |
| --- | --- | --- |
| Shares a precise entity (a part, a component, a contract, a customer) | 0.9–1.0 | Two records touching one identified thing are related whatever they say |
| The source states it (a cited record, one hop) | 0.8 | The author said so |
| Names it in prose (`_Mentions` / `_AppearsIn`) | 0.7 | The NLP layer's half — never filter it out |
| Same deepest category level | 0.6–0.8 | Precise |
| Same middle level | 0.45 | |
| Same top level | 0.1–0.2 | Weak but real |
| Shares a location/zone | 0.35 | Physical proximity is a reason to look even across systems |

**Split a hierarchy into one signal per level with descending weights**, rather than one
signal with a branch inside it. The weights then order themselves and the attribution says
which level matched.

### Fuse by route, not by signal

The engine sums signals, so **the route with the most signals wins**: nine entity signals drown
three text signals by count. Group signals into the routes the ground truth labels (SEMANTIC,
ENTITY, GRAPH), sum each route yourself, bound it as `W · (1 − e^(−R/τ))`, and hand each signal
to the engine scaled by its route's factor — the engine's sum then equals the sum of the route
voices and the attribution stays per signal. Label each row with the route whose signals
contributed most. Reciprocal-rank fusion across signals measured **worse** (31 % vs 42 % @20 in
the vc demo): rank-only fusion throws the rarity weights away and lets every signal's long tail
count. `UsingReciprocalRankFusion` fuses the *sources inside one signal*, not the signals.

### Type diversity, after the engine

Sibling records of the seed's type share sites, systems, categories and house style with it by
construction, and fill the list. Order greedily, each further record of a type already listed
counting `0.85×` less, and write the adjusted score back into `result.Scores` (components scaled
by the same factor) — the search area orders by `Score`, so the front end is unchanged. It was
the largest single gain in the pharma demo (+12 findings @20). A record type with many embedded
sections (a nine-section memo) matches every seed a little; score it as the record it summarises
(the memo as its deal) rather than down-weighting it blindly.

### Negative signals

For **evidence against relevance** — a superseded revision, a withdrawn record, the
investigation's own evidence — `AddNegativeSignal` plus a discounting final fusion demotes
rather than hides. **Not for the user's decisions.** A decision is a flag on the row (and a
filter when the caller asks); as a negative signal it makes the evaluation measure the
decisions — in the pharma demo it halved 40 % of the findings on completed reviews, and removing
it was worth +19 points of recall @20.

### Rules

A rule is applied after fusion and is **absolute**. Use it only for things that are genuinely
never allowed: partition isolation, scope boundaries, the seed and its own records. Anything with
exceptions is a weight, or an **admission** test (same partition, *or* a stated relation, *or* a
rare shared identifier, *or* a strong text match) rather than a hard partition rule — the hard
rule hid every cross-partition finding the corpus planted.

---

## 3. Two-hop scenarios — score, then project

"Find similar *cases*" is often really "find similar *records*, then walk to the cases that
analysed them". Do it in that order:

1. score the records with a full scenario;
2. traverse to the related objects;
3. score each by the **best** score among the records that reached it.

Scores survive **narrowing** (`Where`, `Except`, `IsRelatedTo`) but not **expansion** (`Out`),
so a two-hop signal contributes by rank rather than by score. That is the right weight for an
inference anyway — say so in a comment rather than pretending otherwise.

The same pattern gives you **aggregate scoring**: take the per-record score dictionary, resolve
each record's category, and sum the scores per category. That produces "which areas does this
change probably touch" from "which records does it probably touch", and it is one of the most
convincing things the demo does — because it surfaces an area that the structured data does
not already say is impacted.

---

## 4. Wiring it to the UI

The front-end search area consumes the scenario result directly, so scores and per-signal
attribution render themselves:

```csharp
SearchArea()
   .WithFacets()
   .WithSimilarityEngine(_ => Api.SimilarityAsync(endpointPath, seedUID))
   .OnSearch(sr => sr.WithExcludeUIDs(excluded))
   .Renderer(r => r.WithSelection().CustomizeResult(result => decisions.Customize(result, onChanged)))
```

**Always exclude the seed, and the thing the seed is about.** A node is the closest match in
the corpus to itself, so without an exclusion the seed ranks first in its own list of related
things — and the record it analyses ranks near it, which is the *change* rather than something
the change affects. Both are rows nobody can act on, in the two places the best real
candidates should be.

Compute the exclusion set **once** and hand the same task to every list on the screen, so two
lists cannot end up excluding different things.

---

## 5. Measure, then tune

Tuning by reading result lists is guesswork. Score every change against the corpus's ground
truth, one change at a time, and keep the table.

**The harness.** An evaluation endpoint (or an offline script over `data/`) is the only code
that reads the answer key: for every seed with expected findings, call the candidate endpoint,
and report recall at 10 and at 20 — **per discovery route** (SEMANTIC / ENTITY / GRAPH, the
labels the ground truth carries), per target type, for the hero cases, plus latency. The
candidate endpoint itself never reads the ground truth (`curiosity-ai-tools-and-agents` §7).

**The loop.** Baseline; change one thing; re-run the whole set; record the row — including the
changes you reverted and why. Then:

- **Look at which route is failing.** If entity-only findings are found and semantic-only ones
  are not, the text index or the field selection is wrong, and no weight will fix it.
- **Look at which types fill the top twenty.** Siblings of the seed's type crowding the list
  is a diversity problem; one many-sectioned type crowding it is a scoring-unit problem.
- **Self-query the text index** when text recall is inexplicably low: search with a record's
  own stored vector and check it comes back first. The HNSW miss above was found that way.
- **Hold some seeds out.** Constants chosen on the set they are reported on overfit by
  however much a grid can; split (the hero cases make a natural held-out set) before the next
  round, and say in the record which numbers are in-sample.
- **Write it down** — the table, what was tried and dropped, and the known gaps — in
  `docs/deployment.md` and `TODO.md` (`curiosity-delivery` §6).

**Measured on two deployed demos (26.10):**

| Change | Pharma (132 findings) recall @20 | VC (415 findings) recall @20 |
| --- | --- | --- |
| Baseline: binary `HasCommonNeighborsWith`, one concatenated text query, hard partition rule | 19.7 % | 23.9 % |
| Rarity-weighted entities; section-by-section text on stored vectors | 29.5 % | 33.0 % (with exact search and people / co-investor walks) |
| Rarity within the candidate's type; contrastive text | 38.6 % | — |
| Decisions no longer demote candidates | 57.6 % | — |
| Admission instead of the hard rule; fusion by route | 59.8 % | — |
| The seed's own file as candidates, ranked on text alone | — | 42.4 % |
| Type diversity (`0.85×` per repeat) | **68.9 %** | — |
| Shipped | **68.9 %** (top 10: 47 %) | **42.2 %** (top 10: 31.1 %) |

Latency fell at the same time — 5.6 s → 0.7 s (pharma), 4.9 s → 0.6 s median (vc) — because
every signal is precomputed from in-memory traversals and stored vectors, with no re-encoding
and no per-candidate nested queries. Tried and dropped: BM25-style length normalisation of entity
sums, query-encoded seeds, reciprocal-rank fusion (per seed and across signals), per-seed max
instead of sum. **SEMANTIC remained the weakest route in both** (12 of 34; 31.6 %): reasoning
links that section embeddings only partly see — the next step is a reranker over the top 60, not
another weight.

Also:

- **Track progress** on long scenarios and relay the message; a candidate list that appears
  after eight silent seconds reads as broken.
- **Cap candidates** (`MaxCandidates`, `MaxCandidatesPerSignal`) so a hot signal cannot flood
  the pool — after the signals are scored, so the cap keeps the best rather than arbitrary ties.

---

## 6. Checklist

- [ ] Written as a scenario, not a hand-rolled loop; returns the engine's own result
- [ ] Every signal has a name, a `Describe` and a deliberate weight, and hands over **scores**
- [ ] Text: stored vectors, field against field, exact (`uidsToKeep`), relative to the seed
- [ ] Embedding indexes have `EnableAISearch: true`; no `ChunkText` on short sections
- [ ] Entities weighted by rarity within the candidate's type; hierarchies split by level
- [ ] Routes fused so no route wins by signal count; type diversity applied after the engine
- [ ] Decisions flag rows, they do not move the ranking; negative signals only for evidence
- [ ] Only absolute constraints are rules; cross-partition admission rather than a hard rule
- [ ] The seed and its subject are excluded from every list, from one shared computation
- [ ] Recall @10 / @20 per discovery route measured against ground truth, with a held-out set,
      and recorded in `docs/deployment.md`
- [ ] The candidate endpoint never reads the ground truth

---

## Reference implementation

A complete, commented scenario in
[`examples/similarity.affected-documents.cs`](examples/similarity.affected-documents.cs): text
from stored vectors searched exactly and scored relative to the seed, rarity-weighted entity
signals, a hierarchy split by level, the NLP mention signal, a stated-reference signal, rules, and
type diversity written back into the engine's own result — which is what it returns.
