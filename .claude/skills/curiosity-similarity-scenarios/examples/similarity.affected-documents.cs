[endpoint: Curiosity.Endpoints.Path("app/similarity/affected-documents")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

// Reference template — definitions path: code/endpoints/app/similarity/affected-documents.cs
// (the attribute header above must be the first lines of the file).
//
// The records the change under analysis may affect, ranked, with the reason each one surfaced.
//
// Built as a SIMILARITY SCENARIO rather than a hand-rolled loop: the engine fuses the signals,
// keeps the per-signal attribution in ScoreInfo.Components (name -> contribution, so the UI can
// say WHY something surfaced), manages the candidate pool, and applies the rules after fusion.
// The scenario's own SimilarityResult is what comes back — the type SearchArea.WithSimilarityEngine
// consumes — so a hand-built projection would deserialise into an EMPTY RESULT with no error.
//
// THE SIGNALS ARE SCORED, NOT BINARY. This is the shape that took two deployed demos from 20 % to
// 69 % and from 24 % to 42 % recall at 20 against their ground truth (../SKILL.md §5):
//
//   TEXT      section against section, from the vectors the embedding indexes ALREADY HOLD
//             (nothing re-encoded), searched EXACTLY rather than through the HNSW graph, and
//             scored relative to the seed's own N-th best match.
//   ENTITY    each shared neighbour weighted by how RARE it is among candidates of the same type
//             (normalised idf), instead of HasCommonNeighborsWith's flat 1.0 per hit.
//   GRAPH     what the source states (a reference written in the record), one hop.
//
// and after the engine, TYPE DIVERSITY: sibling records of one type share everything with the seed
// by construction and would otherwise fill the list.
//
// GROUND TRUTH IS NOT READ HERE: no CaseFinding, no E.FindingTargets. Only the evaluation endpoint
// (or an offline script over data/) reads the answer key, so the score it reports means something.

var req     = Body.FromJson<TD.CaseRequest>();
var caseUID = req?.CaseUID ?? default(UID128);

if (caseUID == default(UID128))                                                   return BadRequest("caseUID is required");
if (!Graph.TryGet(caseUID, out ReadOnlyNode c) || c.Type != N.Case.Type)          return NotFound($"Case '{caseUID}' not found.");

// The subject of the analysis, BY EDGE.
var seedUID = Q().StartAt(caseUID).Out(N.Change.Type, E.CaseOf).AsUIDEnumerable().FirstOrDefault();
if (seedUID == default(UID128)) return NotFound($"Case '{caseUID}' analyses nothing.");

var partitionUID = Q().StartAt(seedUID).Out(N.Partition.Type, E.InPartition).AsUIDEnumerable().FirstOrDefault();

const int   CANDIDATES_PER_SIGNAL = 150;
const int   TEXT_HITS_PER_INDEX   = 50;
const int   TEXT_REFERENCE_AT     = 100;   // a hit counts for how far it is ABOVE the seed's 100th-best
const double TYPE_DIVERSITY       = 0.85;  // each further record of a type already listed counts 15 % less

// Never a candidate: the case, the change it analyses (a node is the closest match to itself), and
// the change's own case history.
var excluded = new HashSet<UID128> { caseUID, seedUID };
foreach (var uid in Q().StartAt(seedUID).Out(N.Case.Type, E.HasCase).AsUIDEnumerable()) excluded.Add(uid);

// -------------------------------------------------------------------------------------------
// text — field against field, from stored vectors, exact
//
// ONE concatenated seed string averages the specific risk away, and re-encoding it costs model time
// on every call. Instead: each embedded field of the seed is already a vector in its index
// (TryGetVector), and each candidate field's index is searched with it. All indexes on one model
// share one vector space, so the change's EffectsText can be searched against every manual's
// ProcedureText.
//
// EXACT, NOT APPROXIMATE: passing every node of the type as uidsToKeep makes the index score all
// of them instead of walking its HNSW graph. On a hosted workspace the graph walk returned a record's
// own summary in its own top 5 for 4 queries in 40; the exact path for 40 in 40. It only applies
// while uidsToKeep is under ChatAI.HNSW_INDEX_CUTOFF_BRUTE_FORCE_SEARCH (1,000) — above that the
// index silently walks the graph again, so check the type sizes.
//
// Embedding indexes need EnableAISearch: true for StartAtSimilarTextAsync; GetIndex reads any.
// -------------------------------------------------------------------------------------------

var seedFields      = new[] { N.Change.Abstract, N.Change.ReasonText, N.Change.DescriptionText, N.Change.EffectsText };
var candidateFields = new (string Type, string Field)[]
{
    (N.Manual.Type, N.Manual.Abstract), (N.Manual.Type, N.Manual.PurposeText), (N.Manual.Type, N.Manual.ProcedureText),
    (N.Case.Type,   N.Case.Abstract),   (N.Case.Type,   N.Case.FindingsText),
};

var nodesOfType = new Dictionary<string, HashSet<UID128>>();
HashSet<UID128> AllOf(string type)
{
    if (!nodesOfType.TryGetValue(type, out var set)) nodesOfType[type] = set = new HashSet<UID128>(Q().StartAt(type).AsUIDEnumerable());
    return set;
}

Dictionary<UID128, float> TextScores()
{
    var total = new Dictionary<UID128, float>();

    foreach (var seedField in seedFields)
    {
        var seedIndex = Graph.Embeddings.GetIndex(N.Change.Type, seedField);
        if (seedIndex is null || !seedIndex.TryGetVector(seedUID, out var vector)) continue;   // false on a ChunkText index

        // Best score per candidate across its fields — search each index and take the max yourself:
        // a multi-index search keeps the FIRST score it sees per uid, not the best.
        var best = new Dictionary<UID128, float>();

        foreach (var (type, field) in candidateFields)
        {
            var index = Graph.Embeddings.GetIndex(type, field);
            if (index is null) continue;

            foreach (var hit in index.FindSimilar(default(UID128), vector, TEXT_HITS_PER_INDEX, null, AllOf(type)))
            {
                UID128 uid = hit.UID;
                if (excluded.Contains(uid)) continue;
                if (!best.TryGetValue(uid, out var b) || hit.Score > b) best[uid] = hit.Score;
            }
        }

        if (best.Count == 0) continue;

        // Raw cosine sits in a narrow band that moves per seed: count only what a candidate scores
        // above this seed field's N-th best, and sum over the seed fields.
        var ranked    = best.Values.OrderByDescending(v => v).ToList();
        var reference = ranked[Math.Min(TEXT_REFERENCE_AT, ranked.Count - 1)];

        foreach (var kv in best)
        {
            var above = kv.Value - reference;
            if (above > 0) total[kv.Key] = (total.TryGetValue(kv.Key, out var sum) ? sum : 0f) + above;
        }
    }

    return total;
}

// -------------------------------------------------------------------------------------------
// shared entities, weighted by rarity
//
// HasCommonNeighborsWith scores every hit a flat 1.0, so a category forty records share counts as
// much as a part two share, and MaxCandidatesPerSignal then trims the ties ARBITRARILY. Walk it by
// hand: the seed's neighbours, then each neighbour's holders over the reverse edge, each shared
// neighbour worth log(n / df) / log(n) — n the candidates of that type, df how many of them hold it.
// Measured per candidate type: rarity among protocols and rarity among reports are different
// questions, and one type carrying far more entities would otherwise dominate.
// -------------------------------------------------------------------------------------------

Dictionary<UID128, float> SharedRare(string entityType, string forward, string backward)
{
    var scores = new Dictionary<UID128, float>();

    foreach (var entity in Q().StartAt(seedUID).Out(entityType, forward).AsUIDEnumerable().ToList())
    {
        var holders = Q().StartAt(entity).Out(TD.ImpactableTypes, null, new[] { backward }).AsEnumerable().ToList();

        foreach (var group in holders.GroupBy(h => h.Type))
        {
            var n  = Math.Max(2, AllOf(group.Key).Count);
            var df = group.Count();
            if (df > n / 2) continue;                       // carried by half the type: says nothing

            var rarity = (float)(Math.Log((double)n / df) / Math.Log(n));

            foreach (var holder in group)
            {
                if (excluded.Contains(holder.UID)) continue;
                scores[holder.UID] = (scores.TryGetValue(holder.UID, out var sum) ? sum : 0f) + rarity;
            }
        }
    }

    return scores;
}

// Every scored signal leaves scaled to its own best candidate, so its Weight is the whole of what it
// can add — which is what makes the weights comparable. The engine reads only the UID out of the
// TypedUID128 (there is no UID128 -> TypedUID128 conversion; 0 for the type is fine).
IEnumerable<ScoredUID> Scaled(Dictionary<UID128, float> scores)
{
    if (scores.Count == 0) return Array.Empty<ScoredUID>();

    var ranked = scores.OrderByDescending(kv => kv.Value).Take(CANDIDATES_PER_SIGNAL).ToList();
    var top    = ranked[0].Value;

    return ranked.Select(kv => new ScoredUID(new TypedUID128(0, kv.Key), top > 0 ? kv.Value / top : 0f)).ToList();
}

var result = await Graph.Query()
    .StartAt(seedUID)
    .ToSimilarity(o => o
        .MaxCandidates(400)
        .MaxCandidatesPerSignal(CANDIDATES_PER_SIGNAL)
        // A candidate list that appears after eight silent seconds reads as broken.
        .TrackProgress(async p => await RelayStatusAsync(p.Message)))

    // TEXT — the only route that reaches a record sharing no identifier at all.
    .AddSignal("SectionText", s => s
        .Describe("Its sections read like the change's reason, description and effects")
        .Weight(1.0f)
        .From(ctx => Scaled(TextScores())))

    // ENTITY — one signal per kind, so the attribution says which kind matched.
    .AddSignal("SharedPart", s => s
        .Describe("Shares a rarely shared part number with the change")
        .Weight(0.9f)
        .From(ctx => Scaled(SharedRare(N.PartNumber.Type, E.UsesPart, E.UsedByDocument))))

    // A hierarchy is one signal PER LEVEL, narrowest first, so the weights order themselves.
    .AddSignal("SameCategoryL4", s => s
        .Describe("Same detailed category")
        .Weight(0.6f)
        .From(ctx => Scaled(SharedRare(N.Category.Type, E.InCategory, E.HasCategoryDocument))))

    .AddSignal("SameCategoryL2", s => s
        .Describe("Same top-level category")
        .Weight(0.15f)
        .From(ctx => Scaled(SharedRare(N.Category.Type, E.InCategoryL2, E.HasCategoryL2Document))))

    // The NLP layer: a record that names the change's parts in its prose, linked by the parsing
    // pipeline and nowhere else. Never filtered out — users should not have to know which
    // mechanism found a connection.
    .AddSignal("MentionsPart", s => s
        .Describe("Names one of the change's parts in its text")
        .Weight(0.7f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(seedUID)
            .Out(N.PartNumber.Type, E.UsesPart)
            .Out(TD.ImpactableTypes, null, new[] { E._AppearsIn })
            .AsUIDEnumerable()
            .Where(uid => !excluded.Contains(uid))))

    // GRAPH — what the source states.
    .AddSignal("ReferencedByChange", s => s
        .Describe("The change's own text cites it")
        .Weight(0.8f)
        .From(ctx => ctx.Graph.Query()
            .StartAt(seedUID)
            .Out(TD.ImpactableTypes, null, new[] { E.ReferencesDoc })
            .AsUIDEnumerable()))

    // NOT a negative signal for "already decided": a user's decision must not move the ranking, or
    // the evaluation measures the decisions (it halved 40 % of the findings on completed reviews in
    // one demo). The front end badges decided rows from the decisions endpoint instead.

    .Fuse(Fusion.Sum)

    // Partition isolation is ABSOLUTE in this corpus, so it is a rule, not a weight.
    .AddRule("SamePartition", r => r
        .Filter((ctx, candidates) => ctx.Graph.Query().StartAt(candidates).IsRelatedTo(partitionUID).AsUIDEnumerable()))

    .AddRule("NotTheSubject", r => r
        .Filter((ctx, candidates) => candidates.Where(uid => !excluded.Contains(uid))))

    .ExecuteAsync(CancellationToken);

// -------------------------------------------------------------------------------------------
// type diversity, after the engine
//
// Greedy: each further record of a type the list already holds counts TYPE_DIVERSITY less. The
// adjusted score is written back into the result (components scaled by the same factor, so the
// contribution bar still adds up), and the search area orders by Score — so the front end needs no
// change. 0.85 sat mid-plateau in an offline sweep (0.95 → 85, 0.9 → 91, 0.85 → 91, 0.7 → 94 of
// 132); it was the largest single gain in that demo.
// -------------------------------------------------------------------------------------------

var typeOf    = result.Scores.Keys.ToDictionary(uid => uid, uid => Graph.TryGet(uid, out ReadOnlyNode n) ? n.Type : "");
var remaining = result.Scores.OrderByDescending(kv => kv.Value.Score).ToList();
var seen      = new Dictionary<string, int>();

while (remaining.Count > 0)
{
    var bestIndex = 0;
    var bestScore = double.MinValue;

    for (var i = 0; i < remaining.Count; i++)
    {
        var adjusted = remaining[i].Value.Score * Math.Pow(TYPE_DIVERSITY, seen.TryGetValue(typeOf[remaining[i].Key], out var k) ? k : 0);
        if (adjusted > bestScore) { bestScore = adjusted; bestIndex = i; }
    }

    var pick   = remaining[bestIndex];
    var factor = pick.Value.Score > 0 ? (float)(bestScore / pick.Value.Score) : 1f;

    pick.Value.Score = (float)bestScore;
    foreach (var name in pick.Value.Components.Keys.ToList()) pick.Value.Components[name] *= factor;

    seen[typeOf[pick.Key]] = (seen.TryGetValue(typeOf[pick.Key], out var count) ? count : 0) + 1;
    remaining.RemoveAt(bestIndex);
}

return result;
