[endpoint: Curiosity.Endpoints.Path("qkg/surah/similar")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// Surahs similar to one surah — ARABIC FIRST. Each surah is a rarity-weighted vector of its
// lemmas, tf-idf over the 114 surahs: (1 + ln n) × ln(114 / surahs using the lemma), n = the
// surah's verses using it, and a lemma in more than half the surahs left out. The score is the
// cosine of the two vectors plus the same over roots at half weight, each standardised over the
// 113 candidates. The reasons shown: the distinctive lemmas both lean on most, and the themes both
// have a passage in (qkg/clusters/build).
//
// MEASURED against QurSim aggregated to pairs of surahs (tools/cluster-benchmark): Spearman 0.41
// with the reference, and a quarter of each surah's five most related surahs in its top five
// (random: 4 %). Counting shared themes in the score did not improve on that, so they are shown,
// not counted. QurSim and the QUL topics are not read here.
//
// Not cached: the shared themes have to follow a rebuild of the clusters, and this answers in half a
// second.

const double W_LEMMAS = 1.0;
const double W_ROOTS  = 0.5;

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
        Score        = Math.Round(W_LEMMAS * zLemmas[s] + W_ROOTS * zRoots[s], 3),
        Lemmas       = Math.Round(W_LEMMAS * zLemmas[s], 3),
        Roots        = Math.Round(W_ROOTS  * zRoots[s], 3),
        SharedLemmas = shared,
        SharedThemes = themes,
    };
}

var top = Enumerable.Range(0, ns).Where(s => s != self)
                    .OrderByDescending(s => W_LEMMAS * zLemmas[s] + W_ROOTS * zRoots[s]).ThenBy(s => s)
                    .Take(count).ToList();

return new QK.SimilarSurahsDto
{
    Uid    = seed,
    Method = "Shared distinctive lemmas and roots (tf-idf over the 114 surahs), read from the Arabic morphology",
    Surahs = top.Select(Row).ToArray(),
};
