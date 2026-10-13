[endpoint: Curiosity.Endpoints.Path("qkg/surah/similar")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// Surahs similar to one surah — ARABIC FIRST. Three signals, each standardised over the 113
// candidates and summed with a weight:
//
//   lemmas   W_LEMMAS  each surah a rarity-weighted vector of its lemmas, tf-idf over the 114
//                      surahs: (1 + ln n) × ln(114 / surahs using the lemma), n = the surah's verses
//                      using it, a lemma in more than half the surahs left out; the cosine;
//   roots    W_ROOTS   the same over roots;
//   meaning  W_MEANING for each verse of the seed, its NEIGHBOURS nearest verses in the other
//                      surahs by the Arabic embedding (Verse.TextSimple — AraModernBert-STS); a
//                      candidate scores how many of those it holds. Verse-level meaning, counted up:
//                      a surah's averaged vector washes it out (scored below the lexical signals).
//
// The reasons shown: the distinctive lemmas both lean on most, the closest verse pairs the meaning
// signal counted, and the themes both have a passage in (qkg/clusters/build).
//
// MEASURED against QurSim aggregated to pairs of surahs (tools/cluster-benchmark; NEIGHBOURS and
// W_MEANING chosen on half the surahs, reported on the other half): lemmas + roots alone, Spearman
// 0.400 with the reference and 32 % of each surah's five most related surahs in its top ten; with
// the meaning signal, 0.410 and 36 % (+4 points, 95 % CI +1 to +7) — the top of the list improves.
// Counting shared themes in the score did not improve it, so they are shown, not counted. QurSim
// and the QUL topics are not read here. While the embeddings index is empty the meaning signal is
// neutral (0); after switching the model, choose NEIGHBOURS and W_MEANING again.
//
// Not cached: the shared themes have to follow a rebuild of the clusters. The meaning signal scores
// the seed's verses against every other verse: Al-Baqarah, the longest (286 × 5,950), in 0.5 s.

const double W_LEMMAS   = 1.0;
const double W_ROOTS    = 0.5;
const double W_MEANING  = 1.5;
const int    NEIGHBOURS = 10;

var req   = Body.FromJson<QK.SimilarRequest>();
var seed  = req?.Uid ?? default(UID128);
var count = Math.Clamp(req?.Count is > 0 ? req.Count : 10, 1, 113);

if (seed == default(UID128))                                         return BadRequest("Uid (a Surah) is required");
if (!Graph.TryGet(seed, out var seedNode) || seedNode.Type != N.Surah.Type) return NotFound($"Surah '{seed}' not found.");

var surahs = Q().StartAt(N.Surah.Type).AsEnumerable().OrderBy(s => s.GetInt(N.Surah.Number)).ToList();
var ns     = surahs.Count;
var self   = surahs.FindIndex(s => s.UID == seed);

// The surah's verses using each lemma (or root), then tf-idf over the surahs, normalised.
Dictionary<UID128, double>[] Vectors(string type, string edge)
{
    var tf = new Dictionary<UID128, int>[ns];
    var df = new Dictionary<UID128, int>();
    for (var s = 0; s < ns; s++)
    {
        tf[s] = new Dictionary<UID128, int>();
        foreach (var verse in Q().StartAt(surahs[s].UID).Out(N.Verse.Type, E.HasVerse).AsUIDEnumerable())
            foreach (var t in Q().StartAt(verse).Out(type, edge).AsUIDEnumerable())
                tf[s][t] = tf[s].TryGetValue(t, out var c) ? c + 1 : 1;
        foreach (var t in tf[s].Keys) df[t] = df.TryGetValue(t, out var c) ? c + 1 : 1;
    }
    return tf.Select(counts =>
    {
        var v    = counts.Where(kv => df[kv.Key] <= ns / 2.0).ToDictionary(kv => kv.Key, kv => (1 + Math.Log(kv.Value)) * Math.Log((double)ns / df[kv.Key]));
        var norm = Math.Sqrt(v.Values.Sum(x => x * x));
        return norm > 0 ? v.ToDictionary(kv => kv.Key, kv => kv.Value / norm) : v;
    }).ToArray();
}

double Cosine(Dictionary<UID128, double> a, Dictionary<UID128, double> b)
    => a.Sum(kv => b.TryGetValue(kv.Key, out var w) ? kv.Value * w : 0);

double[] Standardised(double[] x)
{
    var others = x.Where((v, i) => i != self).ToArray();
    var mean   = others.Average();
    var sd     = Math.Sqrt(others.Sum(v => (v - mean) * (v - mean)) / others.Length);
    return x.Select(v => sd > 0 ? (v - mean) / sd : 0).ToArray();
}

var lemmaVectors = Vectors(N.Lemma.Type, E.VerseHasLemma);
var rootVectors  = Vectors(N.Root.Type,  E.VerseHasRoot);
var zLemmas      = Standardised(Enumerable.Range(0, ns).Select(s => Cosine(lemmaVectors[self], lemmaVectors[s])).ToArray());
var zRoots       = Standardised(Enumerable.Range(0, ns).Select(s => Cosine(rootVectors[self], rootVectors[s])).ToArray());

// --- meaning: each seed verse's nearest verses in the other surahs, by the Arabic embedding ----------
var embeddings = Graph.Embeddings.GetIndex(N.Verse.Type, N.Verse.TextSimple);
var close      = Enumerable.Range(0, ns).Select(_ => new List<(UID128 Seed, UID128 Other, double Similarity)>()).ToArray();
var embedded   = 0;

float[] Unit(float[] v)
{
    var norm = (float)Math.Sqrt(Dot(v, v));
    return norm > 0 ? v.Select(x => x / norm).ToArray() : v;
}

float Dot(float[] a, float[] b)
{
    var width = System.Numerics.Vector<float>.Count;
    var sum   = System.Numerics.Vector<float>.Zero;
    var i     = 0;
    for (; i <= a.Length - width; i += width) sum += new System.Numerics.Vector<float>(a, i) * new System.Numerics.Vector<float>(b, i);
    var dot = System.Numerics.Vector.Sum(sum);
    for (; i < a.Length; i++) dot += a[i] * b[i];
    return dot;
}

if (embeddings is not null)
{
    var others = new List<(UID128 Uid, int Surah, float[] Vector)>();
    for (var s = 0; s < ns; s++)
    {
        if (s == self) continue;
        foreach (var verse in Q().StartAt(surahs[s].UID).Out(N.Verse.Type, E.HasVerse).AsUIDEnumerable())
            if (embeddings.TryGetVector(verse, out var v)) others.Add((verse, s, Unit(v)));
    }

    foreach (var seedVerse in Q().StartAt(seed).Out(N.Verse.Type, E.HasVerse).AsUIDEnumerable())
    {
        if (!embeddings.TryGetVector(seedVerse, out var raw)) continue;
        embedded++;
        var q   = Unit(raw);
        var top = new List<(float Similarity, int Index)>(NEIGHBOURS + 1);     // the nearest so far, closest first
        for (var i = 0; i < others.Count; i++)
        {
            var sim = Dot(q, others[i].Vector);
            if (top.Count == NEIGHBOURS && sim <= top[NEIGHBOURS - 1].Similarity) continue;
            var at = top.Count;
            while (at > 0 && top[at - 1].Similarity < sim) at--;
            top.Insert(at, (sim, i));
            if (top.Count > NEIGHBOURS) top.RemoveAt(NEIGHBOURS);
        }
        foreach (var (sim, i) in top) close[others[i].Surah].Add((seedVerse, others[i].Uid, sim));
    }
}

var zMeaning = Standardised(close.Select(c => (double)c.Count).ToArray());
double Total(int s) => W_LEMMAS * zLemmas[s] + W_ROOTS * zRoots[s] + W_MEANING * zMeaning[s];
string KeyOf(UID128 verse) => Graph.TryGet(verse, out var v) ? v.GetString(N.Verse.VerseKey) : "";

// The themes each surah has a passage in.
HashSet<UID128> ThemesOf(UID128 surah)
    => new HashSet<UID128>(Q().StartAt(surah).Out(N.Cluster.Type, E.HasCluster).Out(N.Cluster.Type, E.InTheme).AsUIDEnumerable());

var seedThemes = ThemesOf(seed);

QK.SimilarSurah Row(int s)
{
    var node   = surahs[s];
    var shared = lemmaVectors[self].Where(kv => lemmaVectors[s].ContainsKey(kv.Key))
                                   .OrderByDescending(kv => kv.Value * lemmaVectors[s][kv.Key]).Take(6)
                                   .Select(kv => new QK.Counted { Uid = kv.Key, Label = Graph.TryGet(kv.Key, out var l) ? l.GetString(N.Lemma.LemmaText) : "" })
                                   .ToArray();
    var themes = ThemesOf(node.UID).Where(seedThemes.Contains)
                                   .Select(t => Graph.TryGet(t, out var c) ? new QK.Counted { Uid = t, Label = c.GetString(N.Cluster.Label), Detail = c.GetString(N.Cluster.LabelArabic) } : null)
                                   .Where(c => c is object).ToArray();
    return new QK.SimilarSurah
    {
        Uid          = node.UID,
        Number       = node.GetInt(N.Surah.Number),
        NameArabic   = node.GetString(N.Surah.NameArabic),
        Name         = node.GetString(N.Surah.NameScholarly),
        NameEnglish  = node.GetString(N.Surah.NameEnglish),
        Place        = node.GetString(N.Surah.RevelationPlace),
        Score        = Math.Round(Total(s), 3),
        Lemmas       = Math.Round(W_LEMMAS  * zLemmas[s], 3),
        Roots        = Math.Round(W_ROOTS   * zRoots[s], 3),
        Meaning      = Math.Round(W_MEANING * zMeaning[s], 3),
        SharedLemmas = shared,
        CloseVerses  = close[s].OrderByDescending(p => p.Similarity)
                               .Select(p => new QK.VersePair { SeedUid = p.Seed, SeedVerseKey = KeyOf(p.Seed), Uid = p.Other, VerseKey = KeyOf(p.Other), Similarity = Math.Round(p.Similarity, 3) })
                               .ToArray(),
        SharedThemes = themes,
    };
}

var top = Enumerable.Range(0, ns).Where(s => s != self)
                    .OrderByDescending(Total).ThenBy(s => s)
                    .Take(count).ToList();

return new QK.SimilarSurahsDto
{
    Uid    = seed,
    Method = embedded == 0 ? "Shared distinctive lemmas and roots (tf-idf over the 114 surahs), read from the Arabic morphology — the Arabic embeddings index has no vector for this surah's verses yet"
                           : "Shared distinctive lemmas and roots (tf-idf over the 114 surahs), and verses close in meaning (each verse's 10 nearest in the other surahs, by the Arabic embedding)",
    Surahs = top.Select(Row).ToArray(),
};
