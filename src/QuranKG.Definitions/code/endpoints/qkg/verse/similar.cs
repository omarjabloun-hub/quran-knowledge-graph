[endpoint: Curiosity.Endpoints.Path("qkg/verse/similar")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// Verses similar to one verse — ARABIC FIRST. Three signals, all read from the Arabic, each
// standardised (a z-score, so a seed whose raw scores sit in a narrow band ranks as fairly as any
// other) and summed with a weight:
//
//   semantic  W_SEM     cosine of the plain-Arabic embeddings (Verse.TextSimple index), searched
//                       EXACTLY: the index only scores every vector when it is handed fewer than
//                       1,000 candidates, so the 6,236 verses go in batches (the HNSW walk misses
//                       neighbours — see curiosity-similarity-scenarios). Standardised over the
//                       verses that HAVE a vector: while the index is still filling, the others
//                       are neutral (0) instead of making the embedded few look alike;
//   lemmas    W_LEMMAS  shared lemmas, each worth log(n/df)/log(n), saturated (1 - e^(-1.5x));
//   roots     W_ROOTS   shared roots, the same way.
//
// The translations are not a signal: the meaning is in the Arabic. QurSim (Ibn Kathir's related
// verses) is the answer key the weights are measured against, so it is NOT read here, and the
// QUL topics are not read either (the Clusters work is scored against them).
//
// THE WEIGHTS ARE MEASURED, per embedding model, with tools/embedding-benchmark (`just benchmark
// --weights`: W_SEM chosen on the dev half of QurSim's queries, reported on the test half; 24 models):
//                                                               W_SEM  top-10 recall / strong pairs
//   AraModernBert-STS  (just embeddings-arabic — the index)     1.0    20.2 / 34.5  the best
//   qwen3-embedding:8b (just embeddings-ollama)                 0.5    19.0 / 32.4  best through Ollama
//   Harrier 270M       (built in, just embeddings-harrier)      0.25   17.5 / 29.4  at 1.0 below lemmas alone
//   no embedding: shared lemmas + roots                                16.9 / 28.0
// W_SEM belongs to the model: after switching the index, set it from that model's own grid.

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
var hasVector         = new bool[all.Count];
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
            if (index.TryGetValue(uid, out var i)) { semantic[i] = hit.Score; hasVector[i] = true; }
        }
    }
}

var embedded = hasVector.Where((h, i) => h && all[i] != seed).Count();     // candidates with a vector

// --- standardise each signal, then sum ----------------------------------------------------------------
// Over every verse but the seed, or — for the semantic signal — over those that have a vector; a
// verse outside the mask scores 0, the mean.
double[] Z(double[] x, bool[] mask = null)
{
    var seedIndex = index[seed];
    var values    = x.Where((v, i) => i != seedIndex && (mask is null || mask[i])).ToArray();
    if (values.Length == 0) return new double[x.Length];
    var mean      = values.Average();
    var sd        = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, values.Length));
    return x.Select((v, i) => sd > 0 && (mask is null || mask[i]) ? (v - mean) / sd : 0).ToArray();
}

var zSem   = semanticAvailable ? Z(semantic, hasVector) : new double[all.Count];
var zLem   = Z(lemmas);
var zRoots = Z(rootsRaw);
var total  = Enumerable.Range(0, all.Count).Select(i => W_SEM * zSem[i] + W_LEMMAS * zLem[i] + W_ROOTS * zRoots[i]).ToArray();

var top = Enumerable.Range(0, all.Count).Where(i => all[i] != seed).OrderByDescending(i => total[i]).Take(count).ToList();

QK.SimilarVerse Row(int i)
{
    var found = Graph.TryGet(all[i], out var v);
    return new QK.SimilarVerse
    {
        Uid         = all[i],
        VerseKey    = found ? v.GetString(N.Verse.VerseKey)      : "",
        Text        = found ? v.GetString(N.Verse.TextUthmani)   : "",
        Translation = found ? v.GetString(N.Verse.EnglishSaheeh) : "",
        Score       = Math.Round(total[i], 3),
        Semantic    = Math.Round(W_SEM    * zSem[i],   3),
        Lemmas      = Math.Round(W_LEMMAS * zLem[i],   3),
        Roots       = Math.Round(W_ROOTS  * zRoots[i], 3),
        SharedRoots = whyRoots.TryGetValue(i, out var why) ? why.OrderByDescending(r => r.W).Select(r => r.Label).Distinct().Take(6).ToArray() : new string[0],
    };
}

return new QK.SimilarDto
{
    Uid               = seed,
    SemanticAvailable = semanticAvailable,
    Method            = !semanticAvailable ? "shared lemmas + shared roots (the Arabic embeddings index has no vector for this verse yet)"
                      : embedded < all.Count - 1 ? $"Arabic meaning (embedding) + shared lemmas + shared roots — the embeddings cover {embedded + 1:N0} of {all.Count:N0} verses so far"
                      : "Arabic meaning (embedding) + shared lemmas + shared roots",
    Verses            = top.Select(Row).ToArray(),
};
