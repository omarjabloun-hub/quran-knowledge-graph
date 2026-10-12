[endpoint: Curiosity.Endpoints.Path("qkg/cluster/profile")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// One cluster: a THEME with its passages in mushaf order, or a PASSAGE with its verses, the theme
// it belongs to and its neighbours in the surah. Both with the lemmas and roots their verses use far
// more than the rest of the Quran: (verses of the cluster using it) × log(verses / verses using it)²,
// a term in more than half the verses left out (see Key below).
//
// Not cached: a rebuild of the clusters has to show at once, and this answers in under half a second.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                           return BadRequest("Uid (a Cluster) is required");
if (!Graph.TryGet(uid, out var cluster) || cluster.Type != N.Cluster.Type) return NotFound($"Cluster '{uid}' not found.");

var isTheme  = cluster.GetString(N.Cluster.Kind) == QK.THEME;
var passages = (isTheme ? Q().StartAt(uid).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().ToList() : new List<ReadOnlyNode> { cluster })
                  .OrderBy(p => p.GetInt(N.Cluster.SurahNumber)).ThenBy(p => p.GetInt(N.Cluster.Ordinal)).ToList();

List<ReadOnlyNode> VersesOf(UID128 passage)
    => Q().StartAt(passage).Out(N.Verse.Type, E.HasMember).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.AyahNumber)).ToList();

QK.PassageRow Row(ReadOnlyNode p, List<ReadOnlyNode> verses)
{
    var surah = Q().StartAt(p.UID).Out(N.Surah.Type, E.ClusterOfSurah).AsEnumerable().FirstOrDefault();
    var first = verses.FirstOrDefault();
    var theme = Q().StartAt(p.UID).Out(N.Cluster.Type, E.InTheme).AsEnumerable().FirstOrDefault();
    return new QK.PassageRow
    {
        Uid                = p.UID,
        SurahUid           = surah?.UID ?? default(UID128),
        SurahNumber        = p.GetInt(N.Cluster.SurahNumber),
        SurahName          = surah?.GetString(N.Surah.NameScholarly) ?? "",
        Ordinal            = p.GetInt(N.Cluster.Ordinal),
        FirstVerseKey      = p.GetString(N.Cluster.FirstVerseKey),
        LastVerseKey       = p.GetString(N.Cluster.LastVerseKey),
        FirstVerseUid      = first?.UID ?? default(UID128),
        Verses             = verses.Count,
        Words              = verses.Sum(v => Q().StartAt(v.UID).Out(N.Word.Type, E.HasWord).AsUIDEnumerable().Count()),
        Label              = p.GetString(N.Cluster.Label),
        LabelArabic        = p.GetString(N.Cluster.LabelArabic),
        OpeningText        = first?.GetString(N.Verse.TextUthmani) ?? "",
        OpeningTranslation = first?.GetString(N.Verse.EnglishSaheeh) ?? "",
        ThemeUid           = theme?.UID ?? default(UID128),
        ThemeLabel         = theme?.GetString(N.Cluster.Label) ?? "",
        ThemeLabelArabic   = theme?.GetString(N.Cluster.LabelArabic) ?? "",
        ThemeSurahs        = theme is null ? 0 : Q().StartAt(theme.UID).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().Select(x => x.GetInt(N.Cluster.SurahNumber)).Distinct().Count(),
    };
}

var versesOf = passages.ToDictionary(p => p.UID, p => VersesOf(p.UID));
var allVerses = versesOf.Values.SelectMany(v => v).ToList();
var totalVerses = (double)Q().StartAt(N.Verse.Type).AsUIDEnumerable().Count();

// Scored count × idf², so a word the whole Quran uses (قال, جاء) gives way to the ones that name
// the subject (موسى, ساحر); particles and function nouns are left out, and in a cluster of ten
// verses or more a term must occur in two of them.
QK.Counted[] Key(string type, string forward, string reverse, Func<ReadOnlyNode, string> label, Func<ReadOnlyNode, bool> keep)
{
    var counts = new Dictionary<UID128, int>();
    foreach (var v in allVerses)
        foreach (var t in Q().StartAt(v.UID).Out(type, forward).AsUIDEnumerable())
            counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;

    var minimum = allVerses.Count >= 10 ? 2 : 1;
    return counts.Where(kv => kv.Value >= minimum && Graph.TryGet(kv.Key, out var n) && keep(n))
                 .Select(kv => (Uid: kv.Key, Count: kv.Value, Df: Q().StartAt(kv.Key).Out(N.Verse.Type, reverse).AsUIDEnumerable().Count()))
                 .Where(x => x.Df > 0 && x.Df <= totalVerses / 2)
                 .Select(x => (x.Uid, x.Count, x.Df, Score: x.Count * Math.Pow(Math.Log(totalVerses / x.Df), 2)))
                 .OrderByDescending(x => x.Score).ThenBy(x => x.Uid.ToString(), StringComparer.Ordinal).Take(12)
                 .Select(x => new QK.Counted
                 {
                     Uid    = x.Uid,
                     Label  = Graph.TryGet(x.Uid, out var n) ? label(n) : "",
                     Detail = $"in {x.Count} of these {allVerses.Count} verses, {x.Df} of {totalVerses:N0} in the Quran",
                     Count  = x.Count,
                     Score  = Math.Round(x.Score, 2),
                 }).ToArray();
}

UID128 Neighbour(int offset)
{
    if (isTheme) return default(UID128);
    var surah   = Q().StartAt(uid).Out(N.Surah.Type, E.ClusterOfSurah).AsUIDEnumerable().FirstOrDefault();
    var ordinal = cluster.GetInt(N.Cluster.Ordinal) + offset;
    return Q().StartAt(surah).Out(N.Cluster.Type, E.HasCluster).AsEnumerable()
              .Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE && c.GetInt(N.Cluster.Ordinal) == ordinal)
              .Select(c => c.UID).FirstOrDefault();
}

return new QK.ClusterProfileDto
{
    Uid         = uid,
    Kind        = cluster.GetString(N.Cluster.Kind),
    Label       = cluster.GetString(N.Cluster.Label),
    LabelArabic = cluster.GetString(N.Cluster.LabelArabic),
    Method      = cluster.GetString(N.Cluster.Method),
    RunId       = cluster.GetString(N.Cluster.RunId),
    KeyLemmas   = Key(N.Lemma.Type, E.VerseHasLemma, E.LemmaInVerse, n => n.GetString(N.Lemma.LemmaText),
                      n => n.GetString(N.Lemma.PosTag) != "P" && QK.IsContentNoun(n.GetString(N.Lemma.PartOfSpeech))),
    KeyRoots    = Key(N.Root.Type,  E.VerseHasRoot,  E.RootInVerse,  n => QK.Spaced(n.GetString(N.Root.RootLetters)), n => true),
    Passages    = passages.Select(p => Row(p, versesOf[p.UID])).ToArray(),
    Verses      = isTheme ? new QK.VerseLine[0]
                          : versesOf[uid].Select(v => new QK.VerseLine
                            {
                                Uid         = v.UID,
                                VerseKey    = v.GetString(N.Verse.VerseKey),
                                Text        = v.GetString(N.Verse.TextUthmani),
                                Translation = v.GetString(N.Verse.EnglishSaheeh),
                            }).ToArray(),
    Previous    = Neighbour(-1),
    Next        = Neighbour(+1),
};
