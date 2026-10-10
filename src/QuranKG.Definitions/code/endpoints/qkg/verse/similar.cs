[endpoint: Curiosity.Endpoints.Path("qkg/verse/similar")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// Verses similar to one verse — ARABIC FIRST. A first version of the Similarity work: three
// signals, all read from the Arabic, each standardised over every verse (a z-score, so a seed whose
// raw cosines sit in a narrow band ranks as fairly as any other) and summed:
//
//   semantic  1.0  cosine of the plain-Arabic embeddings (Verse.TextSimple index), searched
//                  EXACTLY: the index only scores every vector when it is handed fewer than 1,000
//                  candidates, so the 6,236 verses go in batches (the HNSW walk misses neighbours —
//                  see curiosity-similarity-scenarios);
//   lemmas    1.0  shared lemmas, each worth log(n/df)/log(n), saturated (1 - e^(-1.5x));
//   roots     0.5  shared roots, the same way.
//
// The translations are not a signal: the meaning is in the Arabic. QurSim (Ibn Kathir's related
// verses) is the answer key these weights are measured against, so it is NOT read here, and the
// QUL topics are not read either (the Clusters work is scored against them).
//
// Measured on QurSim with the benchmark in tools/embedding-benchmark: lemmas alone put 17.0 % of
// the related verses in the top 10. Run the benchmark on your machine to compare embedding models
// and to tune these weights before trusting them.

const int    BATCH    = 900;
const double W_SEM    = 1.0;
const double W_LEMMAS = 1.0;
const double W_ROOTS  = 0.5;

var req   = Body.FromJson<QK.SimilarRequest>();
var seed  = req?.Uid ?? default(UID128);
var count = Math.Clamp(req?.Count is > 0 ? req.Count : 20, 1, 100);

if (seed == default(UID128))                                         return BadRequest("Uid (a Verse) is required");
if (!Graph.TryGet(seed, out var seedNode) || seedNode.Type != N.Verse.Type) return NotFound($"Verse '{seed}' not found.");

var all   = Q().StartAt(N.Verse.Type).AsUIDEnumerable().ToList();
var n     = (double)all.Count;
var index = all.Select((u, i) => (u, i)).ToDictionary(x => x.u, x => x.i);

// --- lexical: shared lemmas and roots, weighted by rarity --------------------------------------------
double[] Shared(string neighbourType, string forward, string reverse, Dictionary<int, List<(string Label, double W)>> why)
{
    var raw = new double[all.Count];
    foreach (var neighbour in Q().StartAt(seed).Out(neighbourType, forward).AsEnumerable())
    {
        var holders = Q().StartAt(neighbour.UID).Out(N.Verse.Type, reverse).AsUIDEnumerable().ToList();
        if (holders.Count == 0 || holders.Count > n / 2) continue;           // ubiquitous: says nothing
        var w = Math.Log(n / holders.Count) / Math.Log(n);
        foreach (var h in holders)
        {
            if (h == seed || !index.TryGetValue(h, out var i)) continue;
            raw[i] += w;
            if (why is not null)
            {
                if (!why.TryGetValue(i, out var list)) why[i] = list = new List<(string, double)>();
                list.Add((QK.Spaced(neighbour.GetString(N.Root.RootLetters)), w));
            }
        }
    }
    for (var i = 0; i < raw.Length; i++) raw[i] = 1.0 - Math.Exp(-1.5 * raw[i]);
    return raw;
}

var whyRoots = new Dictionary<int, List<(string Label, double W)>>();
var lemmas   = Shared(N.Lemma.Type, E.VerseHasLemma, E.LemmaInVerse, null);
var rootsRaw = Shared(N.Root.Type,  E.VerseHasRoot,  E.RootInVerse,  whyRoots);

// --- semantic: the Arabic text's embedding, exact search in batches ----------------------------------
var semantic          = new double[all.Count];
var semanticAvailable = false;
var embeddings        = Graph.Embeddings.GetIndex(N.Verse.Type, N.Verse.TextSimple);

if (embeddings is not null && embeddings.TryGetVector(seed, out var vector))
{
    semanticAvailable = true;
    for (var start = 0; start < all.Count; start += BATCH)
    {
        var batch = new HashSet<UID128>(all.Skip(start).Take(BATCH));
        foreach (var hit in embeddings.FindSimilar(default(UID128), vector, batch.Count, null, batch))
        {
            UID128 uid = hit.UID;
            if (index.TryGetValue(uid, out var i)) semantic[i] = hit.Score;
        }
    }
}

// --- standardise each signal over all verses, then sum --------------------------------------------------
double[] Z(double[] x)
{
    var seedIndex = index[seed];
    var values    = x.Where((v, i) => i != seedIndex).ToArray();
    var mean      = values.Average();
    var sd        = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, values.Length));
    return x.Select(v => sd > 0 ? (v - mean) / sd : 0).ToArray();
}

var zSem   = semanticAvailable ? Z(semantic) : new double[all.Count];
var zLem   = Z(lemmas);
var zRoots = Z(rootsRaw);
var total  = Enumerable.Range(0, all.Count).Select(i => W_SEM * zSem[i] + W_LEMMAS * zLem[i] + W_ROOTS * zRoots[i]).ToArray();

var top = Enumerable.Range(0, all.Count).Where(i => all[i] != seed).OrderByDescending(i => total[i]).Take(count).ToList();

return new QK.SimilarDto
{
    Uid               = seed,
    SemanticAvailable = semanticAvailable,
    Method            = semanticAvailable ? "Arabic meaning (embedding) + shared lemmas + shared roots" : "shared lemmas + shared roots (the Arabic embeddings index has no vector for this verse yet)",
    Verses            = top.Select(i => new QK.SimilarVerse
    {
        Uid         = all[i],
        VerseKey    = Graph.TryGet(all[i], out var v) ? v.GetString(N.Verse.VerseKey) : "",
        Score       = Math.Round(total[i], 3),
        Semantic    = Math.Round(W_SEM * zSem[i], 3),
        Lemmas      = Math.Round(W_LEMMAS * zLem[i], 3),
        Roots       = Math.Round(W_ROOTS * zRoots[i], 3),
        SharedRoots = whyRoots.TryGetValue(i, out var why) ? why.OrderByDescending(x => x.W).Select(x => x.Label).Distinct().Take(6).ToArray() : new string[0],
    }).ToArray(),
};
