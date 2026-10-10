[endpoint: Curiosity.Endpoints.Path("qkg/dashboard/corpus")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// Everything the pattern dashboard draws, counted live from the graph in ONE pass over the 77k
// Word nodes (about 2 s on a laptop), then cached for an hour (the corpus only changes on
// re-ingest, and an import clears the cache):
//   - corpus totals, Meccan vs Medinan, and one statistics row per surah;
//   - the most frequent roots and content lemmas;
//   - the most frequent roots across the 30 juz and the 114 surahs (heat maps), raw and per
//     1,000 words, so a short surah is comparable with a long one;
//   - the roots most characteristic of Meccan vs Medinan surahs (log-odds with an informative
//     Dirichlet prior, Monroe et al. 2008 — a z-score, so rare roots do not dominate).
//
// A word's root, lemma and tag are its stem's: the same facts the connector links as HasRoot /
// HasLemma, so these counts and the lists behind them agree. Juz membership comes from the
// graph (Juz -> Verse -> Word), not from a copied property.

var sw = System.Diagnostics.Stopwatch.StartNew();

var surahNodes = Q().StartAt(N.Surah.Type).AsEnumerable().OrderBy(n => n.GetInt(N.Surah.Number)).ToList();
var juzNodes   = Q().StartAt(N.Juz.Type).AsEnumerable().OrderBy(n => n.GetInt(N.Juz.Number)).ToList();
var roots      = Q().StartAt(N.Root.Type).AsEnumerable().ToDictionary(n => n.GetString(N.Root.RootLetters), n => n.UID);
var lemmas     = Q().StartAt(N.Lemma.Type).AsEnumerable().ToDictionary(n => n.GetString(N.Lemma.LemmaKey), n => n.UID);
var places     = Q().StartAt(N.Revelation.Type).AsEnumerable().ToDictionary(n => n.GetString(N.Revelation.PlaceName), n => n.UID);

var placeOf    = new string[115];
foreach (var s in surahNodes) placeOf[s.GetInt(N.Surah.Number)] = s.GetString(N.Surah.RevelationPlace);

var verseCount = new int[115];
foreach (var s in surahNodes) verseCount[s.GetInt(N.Surah.Number)] = Q().StartAt(s.UID).Out(N.Verse.Type, E.HasVerse).Count();

var juzOfWord = new Dictionary<UID128, int>();
foreach (var j in juzNodes)
{
    var number = j.GetInt(N.Juz.Number);
    foreach (var w in Q().StartAt(j.UID).Out(N.Verse.Type, E.JuzHasVerse).Out(N.Word.Type, E.HasWord).AsUIDEnumerable()) juzOfWord[w] = number;
}

var words      = new int[115];
var nouns      = new int[115];
var verbs      = new int[115];
var particles  = new int[115];
var juzWords   = new int[31];
var rootSets   = Enumerable.Range(0, 115).Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray();
var lemmaSets  = Enumerable.Range(0, 115).Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToArray();
var forms      = new HashSet<string>(StringComparer.Ordinal);
var rootCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var nounCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var verbCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var rootBySurah = new Dictionary<string, int[]>(StringComparer.Ordinal);
var rootByJuz   = new Dictionary<string, int[]>(StringComparer.Ordinal);
var rootMeccan  = new Dictionary<string, int>(StringComparer.Ordinal);
var rootMedinan = new Dictionary<string, int>(StringComparer.Ordinal);

foreach (var w in Q().StartAt(N.Word.Type).AsEnumerable())
{
    var s     = w.GetInt(N.Word.SurahNumber);
    var juz   = juzOfWord.TryGetValue(w.UID, out var jn) ? jn : 0;
    var tag   = w.GetString(N.Word.PosTag);
    var root  = w.GetString(N.Word.RootLetters);
    var lemma = w.GetString(N.Word.LemmaText);

    words[s]++;
    juzWords[juz]++;
    if (tag == "N") nouns[s]++; else if (tag == "V") verbs[s]++; else particles[s]++;
    forms.Add(w.GetString(N.Word.TextPlain));

    if (!string.IsNullOrEmpty(root))
    {
        rootSets[s].Add(root);
        QK.Bump(rootCounts, root);
        if (!rootBySurah.TryGetValue(root, out var bySurah)) rootBySurah[root] = bySurah = new int[115];
        if (!rootByJuz.TryGetValue(root, out var byJuz))     rootByJuz[root]   = byJuz   = new int[31];
        bySurah[s]++;
        byJuz[juz]++;
        QK.Bump(placeOf[s] == QK.MECCAN ? rootMeccan : rootMedinan, root);
    }

    if (!string.IsNullOrEmpty(lemma))
    {
        var key = QK.LemmaKey(lemma, root, tag);
        lemmaSets[s].Add(key);
        if (tag == "V") QK.Bump(verbCounts, key);
        else if (tag == "N" && QK.IsContentNoun(w.GetString(N.Word.PartOfSpeech))) QK.Bump(nounCounts, key);
    }
}

// --- overview -----------------------------------------------------------------------------------
var rows = surahNodes.Select(n =>
{
    var s = n.GetInt(N.Surah.Number);
    return new QK.SurahStats
    {
        Uid             = n.UID,
        Number          = s,
        NameArabic      = n.GetString(N.Surah.NameArabic),
        Name            = n.GetString(N.Surah.NameScholarly) is { Length: > 0 } scholarly ? scholarly : n.GetString(N.Surah.NameLatin),
        NameEnglish     = n.GetString(N.Surah.NameEnglish),
        Place           = placeOf[s],
        RevelationOrder = n.GetInt(N.Surah.RevelationOrder),
        Verses          = verseCount[s],
        Words           = words[s],
        Nouns           = nouns[s],
        Verbs           = verbs[s],
        Particles       = particles[s],
        DistinctRoots   = rootSets[s].Count,
        DistinctLemmas  = lemmaSets[s].Count,
        WordsPerVerse   = verseCount[s] == 0 ? 0 : Math.Round((double)words[s] / verseCount[s], 2),
    };
}).ToArray();

var placeStats = rows.GroupBy(r => r.Place).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new QK.PlaceStats
{
    Uid           = places.TryGetValue(g.Key, out var u) ? u : default(UID128),
    Place         = g.Key,
    Surahs        = g.Count(),
    Verses        = g.Sum(r => r.Verses),
    Words         = g.Sum(r => r.Words),
    WordsPerVerse = Math.Round((double)g.Sum(r => r.Words) / Math.Max(1, g.Sum(r => r.Verses)), 2),
}).ToArray();

string LemmaLabel(string key) => key.Split('|')[0];

var overview = new QK.OverviewDto
{
    Surahs        = rows.Length,
    Verses        = rows.Sum(r => r.Verses),
    Words         = rows.Sum(r => r.Words),
    DistinctForms = forms.Count,
    Lemmas        = lemmas.Count,
    Roots         = roots.Count,
    Topics        = Q().StartAt(N.Topic.Type).Count(),
    Clusters      = Q().StartAt(N.Cluster.Type).Count(),
    Nouns         = rows.Sum(r => r.Nouns),
    Verbs         = rows.Sum(r => r.Verbs),
    Particles     = rows.Sum(r => r.Particles),
    Places        = placeStats,
    SurahRows     = rows,
    TopRoots      = QK.Top(rootCounts, roots, 25, QK.Spaced),
    TopNouns      = QK.Top(nounCounts, lemmas, 20, LemmaLabel),
    TopVerbs      = QK.Top(verbCounts, lemmas, 20, LemmaLabel),
};

// --- heat maps: the 30 most frequent roots ---------------------------------------------------------
var topRoots = QK.Top(rootCounts, roots, 30, QK.Spaced);
var topKeys  = rootCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(30).Select(kv => kv.Key).ToArray();

QK.HeatmapDto Heatmap(Dictionary<string, int[]> byColumn, int first, int last, int[] columnWords, string[] labels, UID128[] uids)
{
    var counts = topKeys.Select(k => Enumerable.Range(first, last - first + 1).Select(c => byColumn[k][c]).ToArray()).ToArray();
    return new QK.HeatmapDto
    {
        Rows        = topRoots,
        Columns     = labels,
        ColumnUIDs  = uids,
        ColumnWords = Enumerable.Range(first, last - first + 1).Select(c => columnWords[c]).ToArray(),
        Counts      = counts,
        PerThousand = counts.Select(r => r.Select((v, i) => columnWords[first + i] == 0 ? 0 : Math.Round(1000.0 * v / columnWords[first + i], 2)).ToArray()).ToArray(),
    };
}

var rootsByJuz   = Heatmap(rootByJuz, 1, 30, juzWords,
                           juzNodes.Select(j => j.GetInt(N.Juz.Number).ToString()).ToArray(), juzNodes.Select(j => j.UID).ToArray());
var rootsBySurah = Heatmap(rootBySurah, 1, 114, words,
                           rows.Select(r => r.Number.ToString()).ToArray(), rows.Select(r => r.Uid).ToArray());

// --- Meccan vs Medinan: log-odds ratio with an informative Dirichlet prior --------------------------
var nM = rows.Where(r => r.Place == QK.MECCAN).Sum(r => r.Words);
var nD = rows.Where(r => r.Place != QK.MECCAN).Sum(r => r.Words);
var n  = (double)(nM + nD);
const double A0 = 1000.0;

var scored = rootCounts.Keys.Select(k =>
{
    var yM = rootMeccan.TryGetValue(k, out var a) ? a : 0;
    var yD = rootMedinan.TryGetValue(k, out var b) ? b : 0;
    var alpha = A0 * (yM + yD) / n;
    var delta = Math.Log((yM + alpha) / (nM + A0 - yM - alpha)) - Math.Log((yD + alpha) / (nD + A0 - yD - alpha));
    var z = delta / Math.Sqrt(1.0 / (yM + alpha) + 1.0 / (yD + alpha));
    return new QK.Counted { Uid = roots.TryGetValue(k, out var u) ? u : default(UID128), Label = QK.Spaced(k), Detail = $"{yM} Meccan · {yD} Medinan", Count = yM + yD, Score = Math.Round(z, 2) };
}).ToList();

return new QK.CorpusDto
{
    Overview     = overview,
    RootsByJuz   = rootsByJuz,
    RootsBySurah = rootsBySurah,
    Distinctive  = new QK.DistinctiveDto
    {
        Meccan       = scored.OrderByDescending(x => x.Score).Take(20).ToArray(),
        Medinan      = scored.OrderBy(x => x.Score).Take(20).ToArray(),
        MeccanWords  = nM,
        MedinanWords = nD,
    },
    Milliseconds = Math.Round(sw.Elapsed.TotalMilliseconds),
};
