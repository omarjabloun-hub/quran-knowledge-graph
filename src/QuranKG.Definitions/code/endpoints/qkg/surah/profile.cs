[endpoint: Curiosity.Endpoints.Path("qkg/surah/profile")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// One surah's lexical fingerprint: verse lengths in order, parts of speech, its most frequent
// roots and lemmas, and its DISTINCTIVE roots — the ones it uses far more than the rest of the
// Quran (tf-idf over the 114 surahs: tf = share of the surah's words, idf = log(114 / surahs
// using the root)). Only this surah's words are read, plus each of its roots' verse lists.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                       return BadRequest("Uid (a Surah) is required");
if (!Graph.TryGet(uid, out var surah) || surah.Type != N.Surah.Type) return NotFound($"Surah '{uid}' not found.");

var verses = Q().StartAt(uid).Out(N.Verse.Type, E.HasVerse).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.AyahNumber)).ToList();
var words  = Q().StartAt(uid).Out(N.Word.Type, E.SurahHasWord).AsEnumerable().ToList();

var lengthOf   = new Dictionary<string, int>(StringComparer.Ordinal);
var rootCount  = new Dictionary<string, int>(StringComparer.Ordinal);
var lemmaCount = new Dictionary<string, int>(StringComparer.Ordinal);
int nouns = 0, verbs = 0, particles = 0;

foreach (var w in words)
{
    var tag  = w.GetString(N.Word.PosTag);
    var root = w.GetString(N.Word.RootLetters);
    QK.Bump(lengthOf, w.GetString(N.Word.VerseKey));
    QK.Bump(rootCount, root);
    var lemma = w.GetString(N.Word.LemmaText);
    var content = tag == "V" || (tag == "N" && QK.IsContentNoun(w.GetString(N.Word.PartOfSpeech)));
    if (!string.IsNullOrEmpty(lemma) && content) QK.Bump(lemmaCount, QK.LemmaKey(lemma, root, tag));
    if (tag == "N") nouns++; else if (tag == "V") verbs++; else particles++;
}

var rootNodes  = Q().StartAt(uid).Out(N.Verse.Type, E.HasVerse).Out(N.Root.Type, E.VerseHasRoot).AsEnumerable()
                    .ToDictionary(n => n.GetString(N.Root.RootLetters), n => n.UID);
var lemmaNodes = Q().StartAt(uid).Out(N.Verse.Type, E.HasVerse).Out(N.Lemma.Type, E.VerseHasLemma).AsEnumerable()
                    .ToDictionary(n => n.GetString(N.Lemma.LemmaKey), n => n.UID);

// In how many surahs each of this surah's roots occurs: Root -> Verse, then Verse -> its surah.
var surahOfVerse = new Dictionary<UID128, int>();
foreach (var s in Q().StartAt(N.Surah.Type).AsEnumerable())
{
    var number = s.GetInt(N.Surah.Number);
    foreach (var v in Q().StartAt(s.UID).Out(N.Verse.Type, E.HasVerse).AsUIDEnumerable()) surahOfVerse[v] = number;
}

var distinctive = rootCount.Where(kv => rootNodes.ContainsKey(kv.Key)).Select(kv =>
{
    var df  = Q().StartAt(rootNodes[kv.Key]).Out(N.Verse.Type, E.RootInVerse).AsUIDEnumerable()
                 .Select(v => surahOfVerse.TryGetValue(v, out var s) ? s : 0).Distinct().Count();
    var tf  = (double)kv.Value / Math.Max(1, words.Count);
    var idf = Math.Log(114.0 / Math.Max(1, df));
    return new QK.Counted { Uid = rootNodes[kv.Key], Label = QK.Spaced(kv.Key), Detail = $"in {df} of 114 surahs", Count = kv.Value, Score = Math.Round(1000 * tf * idf, 2) };
}).OrderByDescending(x => x.Score).Take(20).ToArray();

return new QK.SurahProfileDto
{
    Uid              = uid,
    Verses           = verses.Count,
    Words            = words.Count,
    DistinctRoots    = rootCount.Count,
    DistinctLemmas   = lemmaCount.Count,
    Nouns            = nouns,
    Verbs            = verbs,
    Particles        = particles,
    VerseLengths     = verses.Select(v => lengthOf.TryGetValue(v.GetString(N.Verse.VerseKey), out var c) ? c : 0).ToArray(),
    VerseUIDs        = verses.Select(v => v.UID).ToArray(),
    TopRoots         = QK.Top(rootCount, rootNodes, 20, QK.Spaced),
    DistinctiveRoots = distinctive,
    TopLemmas        = QK.Top(lemmaCount, lemmaNodes, 20, k => k.Split('|')[0]),
};
