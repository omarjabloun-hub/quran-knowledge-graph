[endpoint: Curiosity.Endpoints.Path("qkg/clusters/build")]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

//ImportEndpoint("qkg/_lib/dto")

// Builds the clusters inside the workspace and writes them as Cluster nodes:
//
//   PASSAGES  every surah cut into runs of consecutive verses — its sections;
//   THEMES    passages of several surahs that tell the same story or treat the same subject.
//
// Admin only, not read-only, not cached; `just clusters` runs it. A rebuild that finds the same
// groups keeps the same nodes (the key is the passage's verse range, or a hash of the theme's
// passages) and deletes the ones it no longer finds. Sudo can re-run it, or change a constant
// below and re-run it.
//
// Everything is read from the Arabic morphology — the lemmas and roots of each verse — so it
// runs as soon as the connector has: no embeddings and no model. Adding the verse embeddings to
// the cut gave no reliable gain (Harrier at 0.25: Pk 0.341 → 0.346 on one half of the surahs,
// 0.334 → 0.325 on the other; AraModernBert-STS: 0.338 on both), and without them the build does
// not depend on how far the embeddings index has got.
//
// PASSAGES. At every gap between two verses, the ~WINDOW_WORDS words before it are compared with
// the ~WINDOW_WORDS words after it: the cosine of their rarity-weighted lemmas, plus the cosine of
// their roots at half weight, each standardised within the surah (TextTiling, Hearst 1997). A
// dynamic programme then chooses the cuts that minimise the cohesion across them plus LAMBDA times
// the squared relative distance of each passage's length from the target (160 words), less a
// bonus where a verse opens the way a new section does in the Quran (يا أيها, وإذ, واذكر, ولقد,
// ألم تر, هل أتاك, واتل, كذبت, وإلى).
//
// THEMES. Each passage is a rarity-weighted vector of its lemmas (and of its roots at half weight).
// Average-linkage clustering merges groups while their average cosine is at least tau (0.10); a
// group becomes a theme when it spans at least two surahs.
//
// LABELS. A cluster is named by its most distinctive lemmas (up to two proper nouns first) and, as
// a reading aid only, by the most distinctive words of Saheeh International's English.
//
// NOT READ, because they are the references the clusters are scored against: the ruku' (the
// traditional sections) and the QUL topics for passages and themes, QurSim for themes. MEASURED
// (tools/cluster-benchmark; the constants chosen on half the surahs, reported on the other half):
// passages Pk 0.334 against the ruku' where the same number of equal-length passages scores 0.399
// (lower is better), boundaries within a verse of a ruku' 0.480 F1 against 0.367; themes keep the
// verse pairs Ibn Kathir relates across surahs together 6.8 times as often as chance.
//
// The body may override the two main settings — { "TargetWords": 160, "Tau": 0.10 } — to try a
// coarser or finer cut; {} (or nothing) is the measured default. Method records what was used.

const int    WINDOW_WORDS = 50;
const double LAMBDA       = 8;
const double W_LEMMAS     = 1.0;
const double W_ROOTS      = 0.5;
const double OPENER_BONUS = 2.0;
const string PROPER_NOUN  = "Proper noun";

class BuildRequest
{
    /// <summary>The length a passage is drawn towards, in words (default 160).</summary>
    public double? TargetWords { get; set; }
    /// <summary>The average cosine at which two groups of passages still merge (default 0.10).</summary>
    public double? Tau         { get; set; }
}

var request      = Body.FromJson<BuildRequest>() ?? new BuildRequest();
var targetWords = request.TargetWords is > 0 ? request.TargetWords.Value : 160;
var tau         = request.Tau is > 0 and < 1 ? request.Tau.Value : 0.10;

var openers = new[] { "يا أيها", "وإذ ", "واذكر", "ولقد", "ألم تر", "هل أتاك", "واتل", "كذبت", "وإلى " };
var stopWords = new HashSet<string>((
      "a about above after again against all also am an and any are as at be because been before being below between both but by can could did do does doing down "
    + "during each few for from further had has have having he her here hers herself him himself his how i if in into is it its itself just let me more most my "
    + "myself no nor not now of off on once only or other our ours ourselves out over own said same say says she should so some such than that the their theirs "
    + "them themselves then there these they this those through to too under until up upon very was we were what when where which while who whom why will with "
    + "would you your yours yourself yourselves indeed allah lord god o ye thee thou thy thine unto shall may might must us one ones whatever whoever every thing "
    + "things surely verily thus however therein thereof whereas among truly certainly lo behold whose came come comes get got take took taken make made give "
    + "gave given know knew known see saw seen went go goes gone tell told put bring brought yet even unless except rather toward towards within without never "
    + "ever much many like mention mentioned near side two time times feel used especially therefore whether besides").Split(' '), StringComparer.Ordinal);

var clock  = System.Diagnostics.Stopwatch.StartNew();
var runId  = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
var method = $"Passages: cut where the vocabulary changes (lemmas and roots, {WINDOW_WORDS} words each side), about {targetWords:0} words each. "
           + $"Themes: passages grouped by shared distinctive lemmas and roots (average cosine ≥ {tau:0.00}), spanning two surahs or more.";

// --- the verses, in mushaf order, with their lemmas, roots and word counts ------------------------
var verseNodes = Q().StartAt(N.Verse.Type).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.MushafIndex)).ToList();
var nv         = verseNodes.Count;
var verseUid   = new UID128[nv];
var verseKey   = new string[nv];
var surahOf    = new int[nv];
var ayahOf     = new int[nv];
var plain      = new string[nv];
var english    = new string[nv];
var wordCount  = new int[nv];
var lemmasOf   = new int[nv][];
var rootsOf    = new int[nv][];
var lemmaIds   = new Dictionary<UID128, int>();
var lemmaUids  = new List<UID128>();
var rootIds    = new Dictionary<UID128, int>();
var rootUids   = new List<UID128>();

int IdOf(Dictionary<UID128, int> ids, List<UID128> uids, UID128 uid)
{
    if (!ids.TryGetValue(uid, out var id)) { id = uids.Count; ids[uid] = id; uids.Add(uid); }
    return id;
}

for (var i = 0; i < nv; i++)
{
    var v = verseNodes[i];
    verseUid[i]  = v.UID;
    verseKey[i]  = v.GetString(N.Verse.VerseKey);
    surahOf[i]   = v.GetInt(N.Verse.SurahNumber);
    ayahOf[i]    = v.GetInt(N.Verse.AyahNumber);
    plain[i]     = v.GetString(N.Verse.TextSimple) ?? "";
    english[i]   = v.GetString(N.Verse.EnglishSaheeh) ?? "";
    wordCount[i] = Q().StartAt(v.UID).Out(N.Word.Type, E.HasWord).AsUIDEnumerable().Count();
    lemmasOf[i]  = Q().StartAt(v.UID).Out(N.Lemma.Type, E.VerseHasLemma).AsUIDEnumerable().Select(u => IdOf(lemmaIds, lemmaUids, u)).Distinct().ToArray();
    rootsOf[i]   = Q().StartAt(v.UID).Out(N.Root.Type, E.VerseHasRoot).AsUIDEnumerable().Select(u => IdOf(rootIds, rootUids, u)).Distinct().ToArray();
}

double[] IdfOverVerses(int[][] sets, int vocabulary)
{
    var df = new int[vocabulary];
    foreach (var set in sets) foreach (var t in set) df[t]++;
    return df.Select(d => d > 0 ? Math.Log((double)nv / d) : 0).ToArray();
}

var idfLemma = IdfOverVerses(lemmasOf, lemmaUids.Count);
var idfRoot  = IdfOverVerses(rootsOf, rootUids.Count);

Dictionary<int, double> Block(List<int> verses, int[][] sets, double[] idf)
{
    var counts = new Dictionary<int, double>();
    foreach (var i in verses) foreach (var t in sets[i]) counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
    return counts.ToDictionary(kv => kv.Key, kv => kv.Value * idf[kv.Key]);
}

double Cosine(Dictionary<int, double> a, Dictionary<int, double> b)
{
    if (a.Count == 0 || b.Count == 0) return 0;
    if (a.Count > b.Count) (a, b) = (b, a);
    var dot = 0.0;
    foreach (var kv in a) if (b.TryGetValue(kv.Key, out var w)) dot += kv.Value * w;
    var na = Math.Sqrt(a.Values.Sum(x => x * x));
    var nb = Math.Sqrt(b.Values.Sum(x => x * x));
    return na > 0 && nb > 0 ? dot / (na * nb) : 0;
}

double[] Standardise(double[] x)
{
    if (x.Length < 2) return new double[x.Length];
    var mean = x.Average();
    var sd   = Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / x.Length);
    return x.Select(v => sd > 0 ? (v - mean) / sd : 0).ToArray();
}

bool OpensSection(string text) => openers.Any(o => text.StartsWith(o, StringComparison.Ordinal));

// --- passages: cut each surah where its vocabulary changes ----------------------------------------
List<int> Segment(int[] idx)
{
    var n = idx.Length;
    if (n < 2) return new List<int> { 0 };

    var lexical = new double[n - 1];
    var roots   = new double[n - 1];
    for (var g = 0; g < n - 1; g++)
    {
        var left  = new List<int>();
        var words = 0;
        for (var j = g; j >= 0 && (words < WINDOW_WORDS || left.Count == 0); j--) { left.Add(idx[j]); words += wordCount[idx[j]]; }
        var right = new List<int>();
        words = 0;
        for (var j = g + 1; j < n && (words < WINDOW_WORDS || right.Count == 0); j++) { right.Add(idx[j]); words += wordCount[idx[j]]; }
        lexical[g] = Cosine(Block(left, lemmasOf, idfLemma), Block(right, lemmasOf, idfLemma));
        roots[g]   = Cosine(Block(left, rootsOf, idfRoot), Block(right, rootsOf, idfRoot));
    }

    var zLexical = Standardise(lexical);
    var zRoots   = Standardise(roots);
    var cutCost  = new double[n - 1];
    for (var g = 0; g < n - 1; g++)
        cutCost[g] = W_LEMMAS * zLexical[g] + W_ROOTS * zRoots[g] - (OpensSection(plain[idx[g + 1]]) ? OPENER_BONUS : 0);

    var cumulative = new double[n + 1];
    for (var i = 0; i < n; i++) cumulative[i + 1] = cumulative[i] + wordCount[idx[i]];

    var best = new double[n + 1];
    var back = new int[n + 1];
    for (var end = 1; end <= n; end++)
    {
        best[end] = double.PositiveInfinity;
        for (var start = 0; start < end; start++)
        {
            var off  = (cumulative[end] - cumulative[start] - targetWords) / targetWords;
            var cost = best[start] + LAMBDA * off * off + (start > 0 ? cutCost[start - 1] : 0);
            if (cost < best[end]) { best[end] = cost; back[end] = start; }
        }
    }

    var starts = new List<int>();
    for (var end = n; end > 0; end = back[end]) starts.Add(back[end]);
    starts.Reverse();
    return starts;
}

var passages = new List<int[]>();          // each passage: its verses' indices, in order
foreach (var surah in Enumerable.Range(0, nv).GroupBy(i => surahOf[i]).OrderBy(g => g.Key))
{
    var idx    = surah.ToArray();
    var starts = Segment(idx);
    starts.Add(idx.Length);
    for (var k = 0; k + 1 < starts.Count; k++) passages.Add(idx.Skip(starts[k]).Take(starts[k + 1] - starts[k]).ToArray());
}
var np = passages.Count;

// --- themes: average-linkage clustering of the passages -------------------------------------------
// Each passage: (1 + ln n) × ln(passages / passages using the term), n = its verses using the term;
// a term in more than half the passages says nothing and is left out. Sparse, sorted by term.
(int[] Terms, double[] Weights)[] PassageVectors(int[][] sets, int vocabulary, out int[] dfPassages)
{
    var tf = new List<Dictionary<int, int>>();
    var df = new int[vocabulary];
    foreach (var p in passages)
    {
        var counts = new Dictionary<int, int>();
        foreach (var i in p) foreach (var t in sets[i]) counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
        foreach (var t in counts.Keys) df[t]++;
        tf.Add(counts);
    }
    dfPassages = df;
    var vectors = new (int[] Terms, double[] Weights)[np];
    for (var k = 0; k < np; k++)
    {
        var entries = tf[k].Where(kv => df[kv.Key] <= np / 2.0).OrderBy(kv => kv.Key)
                           .Select(kv => (Term: kv.Key, Weight: (1 + Math.Log(kv.Value)) * Math.Log((double)np / df[kv.Key]))).ToArray();
        var norm    = Math.Sqrt(entries.Sum(e => e.Weight * e.Weight));
        vectors[k]  = (entries.Select(e => e.Term).ToArray(), entries.Select(e => norm > 0 ? e.Weight / norm : 0).ToArray());
    }
    return vectors;
}

var lemmaVectors = PassageVectors(lemmasOf, lemmaUids.Count, out var lemmaDfPassages);
var rootVectors  = PassageVectors(rootsOf, rootUids.Count, out var _);

double Dot((int[] Terms, double[] Weights) a, (int[] Terms, double[] Weights) b)
{
    var s = 0.0;
    for (int x = 0, y = 0; x < a.Terms.Length && y < b.Terms.Length;)
    {
        if      (a.Terms[x] < b.Terms[y]) x++;
        else if (a.Terms[x] > b.Terms[y]) y++;
        else    s += a.Weights[x++] * b.Weights[y++];
    }
    return s;
}

var similarity = new double[np, np];
for (var a = 0; a < np; a++)
    for (var b = a + 1; b < np; b++)
        similarity[a, b] = similarity[b, a] = (Dot(lemmaVectors[a], lemmaVectors[b]) + 0.5 * Dot(rootVectors[a], rootVectors[b])) / 1.5;

var members = Enumerable.Range(0, np).Select(k => new List<int> { k }).ToArray();
var alive   = Enumerable.Repeat(true, np).ToArray();
while (true)
{
    var (bestA, bestB, bestS) = (-1, -1, double.NegativeInfinity);
    for (var a = 0; a < np; a++)
    {
        if (!alive[a]) continue;
        for (var b = a + 1; b < np; b++)
            if (alive[b] && similarity[a, b] > bestS) (bestA, bestB, bestS) = (a, b, similarity[a, b]);
    }
    if (bestA < 0 || bestS < tau) break;

    var (sa, sb) = (members[bestA].Count, members[bestB].Count);
    for (var k = 0; k < np; k++)
    {
        if (!alive[k] || k == bestA || k == bestB) continue;
        similarity[bestA, k] = similarity[k, bestA] = (sa * similarity[bestA, k] + sb * similarity[bestB, k]) / (sa + sb);
    }
    members[bestA].AddRange(members[bestB]);
    alive[bestB] = false;
}

var themes = Enumerable.Range(0, np).Where(k => alive[k])
                       .Select(k => members[k].OrderBy(p => p).ToArray())
                       .Where(g => g.Select(p => surahOf[passages[p][0]]).Distinct().Count() >= 2)
                       .OrderByDescending(g => g.Select(p => surahOf[passages[p][0]]).Distinct().Count())
                       .ThenByDescending(g => g.Length)
                       .ThenBy(g => g[0])
                       .ToList();

// --- labels ---------------------------------------------------------------------------------------
var lemmaPlain  = new Dictionary<int, string>();
var lemmaProper = new Dictionary<int, bool>();
string PlainOf(int lemma)
{
    if (!lemmaPlain.TryGetValue(lemma, out var text))
    {
        var found = Graph.TryGet(lemmaUids[lemma], out var node);
        lemmaPlain[lemma]  = text = found ? node.GetString(N.Lemma.LemmaPlain) : "";
        lemmaProper[lemma] = found && node.GetString(N.Lemma.PartOfSpeech) == PROPER_NOUN;
    }
    return text;
}

string ArabicLabel(IEnumerable<int> verses)
{
    var counts = new Dictionary<int, int>();
    foreach (var i in verses) foreach (var t in lemmasOf[i]) counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
    var top = counts.Where(kv => lemmaDfPassages[kv.Key] <= np / 2.0)
                    .Select(kv => (Lemma: kv.Key, Score: kv.Value * Math.Log((double)np / lemmaDfPassages[kv.Key])))
                    .OrderByDescending(x => x.Score).ThenBy(x => PlainOf(x.Lemma), StringComparer.Ordinal)
                    .Take(6).Select(x => x.Lemma).ToList();
    foreach (var t in top) PlainOf(t);
    var proper = top.Where(t => lemmaProper[t]).Take(2).ToList();
    return string.Join(" · ", proper.Concat(top.Where(t => !proper.Contains(t))).Take(3).Select(PlainOf).Where(s => !string.IsNullOrEmpty(s)));
}

var tokenPattern = new System.Text.RegularExpressions.Regex(@"[A-Za-z][A-Za-z'\-]+");
var bracketed    = new System.Text.RegularExpressions.Regex(@"\[[^\]]*\]");
var casing       = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
var englishOf    = new HashSet<string>[nv];
for (var i = 0; i < nv; i++)
{
    englishOf[i] = new HashSet<string>(StringComparer.Ordinal);
    foreach (System.Text.RegularExpressions.Match m in tokenPattern.Matches(bracketed.Replace(english[i], " ")))
    {
        var word = m.Value.Trim('\'', '-');
        if (word.EndsWith("'s", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 2);
        var lower = word.ToLowerInvariant();
        if (lower.Length < 3 || stopWords.Contains(lower)) continue;
        englishOf[i].Add(lower);
        if (!casing.TryGetValue(lower, out var forms)) casing[lower] = forms = new Dictionary<string, int>(StringComparer.Ordinal);
        forms[word] = forms.TryGetValue(word, out var c) ? c + 1 : 1;
    }
}
var englishDf = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var p in passages) foreach (var w in p.SelectMany(i => englishOf[i]).Distinct()) englishDf[w] = englishDf.TryGetValue(w, out var c) ? c + 1 : 1;

string EnglishLabel(IEnumerable<int> verses)
{
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var i in verses) foreach (var w in englishOf[i]) counts[w] = counts.TryGetValue(w, out var c) ? c + 1 : 1;
    return string.Join(" · ", counts.Where(kv => kv.Value >= 2 && englishDf[kv.Key] <= np / 2.0)
                                    .OrderByDescending(kv => kv.Value * Math.Log((double)np / englishDf[kv.Key])).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                                    .Take(3)
                                    .Select(kv => casing[kv.Key].OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).First().Key));
}

// --- what to write --------------------------------------------------------------------------------
string PassageKey(int[] p) => $"P:{surahOf[p[0]]}:{ayahOf[p[0]]}-{ayahOf[p[p.Length - 1]]}";

string ThemeKey(int[] group)
{
    using var sha = System.Security.Cryptography.SHA1.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join(",", group.Select(p => PassageKey(passages[p])))));
    return "T:" + string.Concat(bytes.Take(6).Select(x => x.ToString("x2")));
}

var passageKeys = passages.Select(PassageKey).ToArray();
var themeKeys   = themes.Select(ThemeKey).ToArray();
var themeOf     = new int[np];
Array.Fill(themeOf, -1);
for (var t = 0; t < themes.Count; t++) foreach (var p in themes[t]) themeOf[p] = t;

UID128 ClusterUid(string key) => Mosaik.GraphDB.Node.GetUID(N.Cluster.Type, key);

var clusterType = Graph.GetNodeTypeUID(N.Cluster.Type);
var verseType   = Graph.GetNodeTypeUID(N.Verse.Type);
var surahType   = Graph.GetNodeTypeUID(N.Surah.Type);
var surahUid    = Q().StartAt(N.Surah.Type).AsEnumerable().ToDictionary(s => s.GetInt(N.Surah.Number), s => s.UID);

// --- 1. delete the clusters this build no longer finds, and the edges that named them -------------
var wanted   = new HashSet<string>(passageKeys.Concat(themeKeys), StringComparer.Ordinal);
var existing = Q().StartAt(N.Cluster.Type).AsEnumerable().ToList();
var stale    = existing.Where(c => !wanted.Contains(c.GetString(N.Cluster.ClusterKey))).Select(c => c.UID).ToList();
var touched  = new HashSet<UID128>();
foreach (var uid in stale)
{
    foreach (var n in Q().StartAt(uid).Out(N.Verse.Type, E.HasMember).AsUIDEnumerable())       touched.Add(n);
    foreach (var n in Q().StartAt(uid).Out(N.Surah.Type, E.ClusterOfSurah).AsUIDEnumerable())  touched.Add(n);
    foreach (var n in Q().StartAt(uid).Out(N.Cluster.Type, E.InTheme).AsUIDEnumerable())       touched.Add(n);
    foreach (var n in Q().StartAt(uid).Out(N.Cluster.Type, E.HasPassage).AsUIDEnumerable())    touched.Add(n);
}
foreach (var uid in stale) await Graph.TryDeleteAsync(uid);
touched.ExceptWith(stale);
foreach (var uid in touched) await Graph.RemoveInvalidEdges(uid);

// --- 2. the passages, with their verses and surah --------------------------------------------------
var ordinal = 0;
var previousSurah = 0;
for (var k = 0; k < np; k++)
{
    CancellationToken.ThrowIfCancellationRequested();
    var p     = passages[k];
    var surah = surahOf[p[0]];
    ordinal   = surah == previousSurah ? ordinal + 1 : 1;
    previousSurah = surah;

    var gloss = EnglishLabel(p);
    var node  = await Graph.GetOrAddLockedAsync(N.Cluster.Type, passageKeys[k]);
    node[N.Cluster.Kind]          = QK.PASSAGE;
    node[N.Cluster.Scope]         = $"Surah {surah}";
    node[N.Cluster.Label]         = $"{verseKey[p[0]]}–{ayahOf[p[p.Length - 1]]}" + (gloss.Length > 0 ? " · " + gloss : "");
    node[N.Cluster.LabelArabic]   = ArabicLabel(p);
    node[N.Cluster.Description]   = "";
    node[N.Cluster.Method]        = method;
    node[N.Cluster.RunId]         = runId;
    node[N.Cluster.SurahNumber]   = surah;
    node[N.Cluster.Ordinal]       = ordinal;
    node[N.Cluster.FirstVerseKey] = verseKey[p[0]];
    node[N.Cluster.LastVerseKey]  = verseKey[p[p.Length - 1]];
    foreach (var i in p) node.AddUniqueEdge(E.HasMember, verseUid[i], verseType);
    node.AddUniqueEdge(E.ClusterOfSurah, surahUid[surah], surahType);
    if (themeOf[k] >= 0) node.AddUniqueEdge(E.InTheme, ClusterUid(themeKeys[themeOf[k]]), clusterType);
    await Graph.CommitAsync(node);
}

// --- 3. the themes, with their passages -------------------------------------------------------------
for (var t = 0; t < themes.Count; t++)
{
    var verses = themes[t].SelectMany(p => passages[p]).ToArray();
    var arabic = ArabicLabel(verses);
    var label  = EnglishLabel(verses);
    var node   = await Graph.GetOrAddLockedAsync(N.Cluster.Type, themeKeys[t]);
    node[N.Cluster.Kind]          = QK.THEME;
    node[N.Cluster.Scope]         = "Quran";
    node[N.Cluster.Label]         = label.Length > 0 ? label : arabic;
    node[N.Cluster.LabelArabic]   = arabic;
    node[N.Cluster.Description]   = "";
    node[N.Cluster.Method]        = method;
    node[N.Cluster.RunId]         = runId;
    node[N.Cluster.SurahNumber]   = 0;
    node[N.Cluster.Ordinal]       = t + 1;
    node[N.Cluster.FirstVerseKey] = "";
    node[N.Cluster.LastVerseKey]  = "";
    foreach (var p in themes[t]) node.AddUniqueEdge(E.HasPassage, ClusterUid(passageKeys[p]), clusterType);
    await Graph.CommitAsync(node);
}

// --- 4. the reverse edges: each verse to its passage, each surah to its passages ---------------------
for (var k = 0; k < np; k++)
{
    var passageUid = ClusterUid(passageKeys[k]);
    foreach (var i in passages[k])
    {
        var verse = await Graph.TryGetLockedAsync(verseUid[i]);
        verse.AddUniqueEdge(E.InCluster, passageUid, clusterType);
        await Graph.CommitAsync(verse);
    }
}
foreach (var group in Enumerable.Range(0, np).GroupBy(k => surahOf[passages[k][0]]))
{
    var surah = await Graph.TryGetLockedAsync(surahUid[group.Key]);
    foreach (var k in group) surah.AddUniqueEdge(E.HasCluster, ClusterUid(passageKeys[k]), clusterType);
    await Graph.CommitAsync(surah);
}

// --- 5. check what was written: every verse in exactly one passage, every passage in at most one theme
var versesNotInOnePassage = verseUid.Count(v => Q().StartAt(v).Out(N.Cluster.Type, E.InCluster).AsUIDEnumerable().Count() != 1);
var passagesInSeveralThemes = passageKeys.Count(key => Q().StartAt(ClusterUid(key)).Out(N.Cluster.Type, E.InTheme).AsUIDEnumerable().Count() > 1);
var clustersNow = Q().StartAt(N.Cluster.Type).AsUIDEnumerable().Count();

Logger.LogInformation("qkg/clusters/build: {Passages} passages, {Themes} themes, {Stale} stale clusters removed in {Seconds:F1} s", np, themes.Count, stale.Count, clock.Elapsed.TotalSeconds);

return new
{
    RunId           = runId,
    Method          = method,
    Passages        = np,
    Themes          = themes.Count,
    VersesInThemes  = themes.Sum(g => g.Sum(p => passages[p].Length)),
    Removed         = stale.Count,
    Seconds         = Math.Round(clock.Elapsed.TotalSeconds, 1),
    // All three are 0 / np + themes when the graph is consistent.
    VersesNotInOnePassage   = versesNotInOnePassage,
    PassagesInSeveralThemes = passagesInSeveralThemes,
    ClustersInGraph         = clustersNow,
    // What the build found, by key, so tools/cluster-benchmark can score this exact run.
    PassageKeys     = passageKeys,
    ThemePassages   = themes.Select(g => g.Select(p => passageKeys[p]).ToArray()).ToArray(),
};
