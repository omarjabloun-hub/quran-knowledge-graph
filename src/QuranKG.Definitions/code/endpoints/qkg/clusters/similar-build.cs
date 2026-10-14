[endpoint: Curiosity.Endpoints.Path("qkg/clusters/similar-build")]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

//ImportEndpoint("qkg/_lib/dto")

// SIMILARITY GROUPS: clusters of passages (the Passage clusters qkg/clusters/build already cut) that
// talk about the same thing, grouped with the same signals as qkg/verse/similar:
//   semantic  W_SEM    cosine of each passage's mean verse embedding (Verse.TextSimple index)
//   lemmas    W_LEMMAS cosine of rarity-weighted lemma vectors
//   roots     W_ROOTS  cosine of rarity-weighted root vectors
// each standardised (z-score) over all cross-surah passage pairs, then summed with the weights.
// Passages of the same surah get a score of -1 so a group spreads over surahs.
// Average-linkage merges while the average score is >= Tau; a group needs >= 2 surahs.
//
// Written as Cluster nodes of Kind "SimilarGroup" (key "S:<hash of its passages>"), with HasPassage
// edges to the passages. The existing Themes and Passages are not touched, and qkg/clusters/build
// leaves SimilarGroup nodes alone. QurSimPair is NOT read (it is the answer key).
// Body (optional): { "Tau": 1.5 }  - lower gives bigger groups. Admin only; rerun to rebuild.

const double W_SEM = 1.0;
const double W_LEMMAS = 1.0;
const double W_ROOTS = 0.5;
const string KIND = "SimilarGroup";

class SimBuildRequest { public double? Tau { get; set; } }
var request = ParseBody<SimBuildRequest>() ?? new SimBuildRequest();
var tau = request.Tau ?? 1.5;
var clock = System.Diagnostics.Stopwatch.StartNew();
var runId = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
var method = $"Similarity groups: passages grouped by Arabic meaning (mean verse embedding) + shared lemmas + shared roots, standardised and summed (weights {W_SEM}/{W_LEMMAS}/{W_ROOTS}), average-linkage at z >= {tau:0.00}, across two surahs or more.";

var passageNodes = Q().StartAt(N.Cluster.Type).AsEnumerable().Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE)
                      .OrderBy(c => c.GetInt(N.Cluster.SurahNumber)).ThenBy(c => c.GetInt(N.Cluster.Ordinal)).ToList();
var np = passageNodes.Count;
if (np == 0) return BadRequest("Run qkg/clusters/build first: there are no passages.");

var passageVerses = passageNodes.Select(p => Q().StartAt(p.UID).Out(N.Verse.Type, E.HasMember).AsUIDEnumerable().ToArray()).ToArray();
var surahOf = passageNodes.Select(p => p.GetInt(N.Cluster.SurahNumber)).ToArray();

// --- sparse rarity-weighted vectors over lemmas and roots ------------------------------------------
var verseLemmas = new Dictionary<UID128, UID128[]>();
var verseRoots = new Dictionary<UID128, UID128[]>();
foreach (var v in passageVerses.SelectMany(x => x).Distinct())
{
    verseLemmas[v] = Q().StartAt(v).Out(N.Lemma.Type, E.VerseHasLemma).AsUIDEnumerable().Distinct().ToArray();
    verseRoots[v] = Q().StartAt(v).Out(N.Root.Type, E.VerseHasRoot).AsUIDEnumerable().Distinct().ToArray();
}

Dictionary<UID128, double>[] Vectors(Dictionary<UID128, UID128[]> terms)
{
    var tf = passageVerses.Select(vs => { var d = new Dictionary<UID128, int>(); foreach (var v in vs) foreach (var t in terms[v]) d[t] = d.TryGetValue(t, out var c) ? c + 1 : 1; return d; }).ToArray();
    var df = new Dictionary<UID128, int>();
    foreach (var d in tf) foreach (var t in d.Keys) df[t] = df.TryGetValue(t, out var c) ? c + 1 : 1;
    return tf.Select(d =>
    {
        var w = d.Where(kv => df[kv.Key] <= np / 2.0).ToDictionary(kv => kv.Key, kv => (1 + Math.Log(kv.Value)) * Math.Log((double)np / df[kv.Key]));
        var norm = Math.Sqrt(w.Values.Sum(x => x * x));
        return norm > 0 ? w.ToDictionary(kv => kv.Key, kv => kv.Value / norm) : w;
    }).ToArray();
}

double Dot(Dictionary<UID128, double> a, Dictionary<UID128, double> b)
{
    if (a.Count > b.Count) (a, b) = (b, a);
    var s = 0.0;
    foreach (var kv in a) if (b.TryGetValue(kv.Key, out var w)) s += kv.Value * w;
    return s;
}

var lemmaVec = Vectors(verseLemmas);
var rootVec = Vectors(verseRoots);

// --- semantic: mean verse embedding per passage ------------------------------------------------------
var embeddings = Graph.Embeddings.GetIndex(N.Verse.Type, N.Verse.TextSimple);
var passageVec = new float[np][];
var semanticAvailable = false;
if (embeddings is not null)
{
    for (var k = 0; k < np; k++)
    {
        float[] sum = null;
        foreach (var v in passageVerses[k])
        {
            if (!embeddings.TryGetVector(v, out var vec)) continue;
            var arr = vec.ToArray();
            if (sum is null) sum = new float[arr.Length];
            for (var d = 0; d < arr.Length; d++) sum[d] += arr[d];
        }
        if (sum is null) continue;
        var norm = (float)Math.Sqrt(sum.Sum(x => x * x));
        if (norm > 0) for (var d = 0; d < sum.Length; d++) sum[d] /= norm;
        passageVec[k] = sum;
        semanticAvailable = true;
    }
}

// --- pairwise scores -----------------------------------------------------------------------------------
var sem = new double[np, np];
var lem = new double[np, np];
var rts = new double[np, np];
var cross = new List<(double, double, double)>();
for (var a = 0; a < np; a++)
    for (var b = a + 1; b < np; b++)
    {
        lem[a, b] = Dot(lemmaVec[a], lemmaVec[b]);
        rts[a, b] = Dot(rootVec[a], rootVec[b]);
        if (passageVec[a] is not null && passageVec[b] is not null)
        {
            var s = 0.0;
            for (var d = 0; d < passageVec[a].Length; d++) s += passageVec[a][d] * passageVec[b][d];
            sem[a, b] = s;
        }
        if (surahOf[a] != surahOf[b]) cross.Add((sem[a, b], lem[a, b], rts[a, b]));
    }

(double Mean, double Sd) Stats(IEnumerable<double> xs)
{
    var l = xs.ToList();
    var m = l.Average();
    return (m, Math.Max(1e-9, Math.Sqrt(l.Sum(x => (x - m) * (x - m)) / l.Count)));
}
var stSem = Stats(cross.Select(c => c.Item1));
var stLem = Stats(cross.Select(c => c.Item2));
var stRts = Stats(cross.Select(c => c.Item3));
var wSem = semanticAvailable ? W_SEM : 0;
var wTotal = wSem + W_LEMMAS + W_ROOTS;

var score = new double[np, np];
for (var a = 0; a < np; a++)
    for (var b = a + 1; b < np; b++)
    {
        var z = surahOf[a] == surahOf[b] ? -1.0
            : (wSem * (sem[a, b] - stSem.Mean) / stSem.Sd + W_LEMMAS * (lem[a, b] - stLem.Mean) / stLem.Sd + W_ROOTS * (rts[a, b] - stRts.Mean) / stRts.Sd) / wTotal;
        score[a, b] = score[b, a] = z;
    }

// --- average-linkage -------------------------------------------------------------------------------------
var members = Enumerable.Range(0, np).Select(k => new List<int> { k }).ToArray();
var alive = Enumerable.Repeat(true, np).ToArray();
while (true)
{
    CancellationToken.ThrowIfCancellationRequested();
    var (bestA, bestB, bestS) = (-1, -1, double.NegativeInfinity);
    for (var a = 0; a < np; a++)
    {
        if (!alive[a]) continue;
        for (var b = a + 1; b < np; b++)
            if (alive[b] && score[a, b] > bestS) (bestA, bestB, bestS) = (a, b, score[a, b]);
    }
    if (bestA < 0 || bestS < tau) break;
    var (sa, sb) = (members[bestA].Count, members[bestB].Count);
    for (var k = 0; k < np; k++)
    {
        if (!alive[k] || k == bestA || k == bestB) continue;
        score[bestA, k] = score[k, bestA] = (sa * score[bestA, k] + sb * score[bestB, k]) / (sa + sb);
    }
    members[bestA].AddRange(members[bestB]);
    alive[bestB] = false;
}

var groups = Enumerable.Range(0, np).Where(k => alive[k]).Select(k => members[k].OrderBy(p => p).ToArray())
                       .Where(g => g.Select(p => surahOf[p]).Distinct().Count() >= 2)
                       .OrderByDescending(g => g.Select(p => surahOf[p]).Distinct().Count()).ThenByDescending(g => g.Length).ThenBy(g => g[0]).ToList();

// --- labels: the group's most distinctive lemmas ---------------------------------------------------------
var lemmaDf = new Dictionary<UID128, int>();
foreach (var v in passageVerses.SelectMany(x => x).Distinct()) foreach (var t in verseLemmas[v]) lemmaDf[t] = lemmaDf.TryGetValue(t, out var c) ? c + 1 : 1;
var nVerses = (double)verseLemmas.Count;
string Label(int[] g)
{
    var counts = new Dictionary<UID128, int>();
    foreach (var p in g) foreach (var v in passageVerses[p]) foreach (var t in verseLemmas[v]) counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
    var top = counts.Where(kv => lemmaDf[kv.Key] <= nVerses * 0.10).OrderByDescending(kv => kv.Value * Math.Log(nVerses / lemmaDf[kv.Key])).Take(3).Select(kv => kv.Key);
    return string.Join(" · ", top.Select(u => Graph.TryGet(u, out var n) ? n.GetString(N.Lemma.LemmaPlain) : "").Where(s => !string.IsNullOrEmpty(s)));
}

string GroupKey(int[] g)
{
    using var sha = System.Security.Cryptography.SHA1.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join(",", g.Select(p => passageNodes[p].GetString(N.Cluster.ClusterKey)))));
    return "S:" + string.Concat(bytes.Take(6).Select(x => x.ToString("x2")));
}

// --- write: remove the groups no longer found, then add / update -------------------------------------------
var keys = groups.Select(GroupKey).ToArray();
var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
var stale = Q().StartAt(N.Cluster.Type).AsEnumerable().Where(c => c.GetString(N.Cluster.Kind) == KIND && !wanted.Contains(c.GetString(N.Cluster.ClusterKey))).Select(c => c.UID).ToList();
foreach (var uid in stale) await Graph.TryDeleteAsync(uid);

var clusterType = Graph.GetNodeTypeUID(N.Cluster.Type);
for (var t = 0; t < groups.Count; t++)
{
    CancellationToken.ThrowIfCancellationRequested();
    var g = groups[t];
    var node = await Graph.GetOrAddLockedAsync(N.Cluster.Type, keys[t]);
    node[N.Cluster.Kind] = KIND;
    node[N.Cluster.Scope] = "Quran";
    node[N.Cluster.Label] = string.Join(" · ", g.Take(3).Select(p => passageNodes[p].GetString(N.Cluster.FirstVerseKey))) + (g.Length > 3 ? " …" : "");
    node[N.Cluster.LabelArabic] = Label(g);
    node[N.Cluster.Description] = "";
    node[N.Cluster.Method] = method;
    node[N.Cluster.RunId] = runId;
    node[N.Cluster.SurahNumber] = 0;
    node[N.Cluster.Ordinal] = t + 1;
    node[N.Cluster.FirstVerseKey] = "";
    node[N.Cluster.LastVerseKey] = "";
    foreach (var p in g) node.AddUniqueEdge(E.HasPassage, passageNodes[p].UID, clusterType);
    await Graph.CommitAsync(node);
}

return new
{
    RunId = runId,
    Method = method,
    Tau = tau,
    SemanticAvailable = semanticAvailable,
    PassagesConsidered = np,
    Groups = groups.Count,
    PassagesGrouped = groups.Sum(g => g.Length),
    LargestGroup = groups.Count > 0 ? groups.Max(g => g.Length) : 0,
    Removed = stale.Count,
    Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1),
};

