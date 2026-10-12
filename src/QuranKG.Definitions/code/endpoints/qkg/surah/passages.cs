[endpoint: Curiosity.Endpoints.Path("qkg/surah/passages")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// One surah's passages — the runs of consecutive verses qkg/clusters/build cut it into — in order,
// each with its opening verse, its size (counted live from its edges) and the theme it shares with
// other surahs. Built is false, and the list empty, until the build has run.
//
// Not cached: a rebuild of the clusters has to show at once, and this answers in a third of a second.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                       return BadRequest("Uid (a Surah) is required");
if (!Graph.TryGet(uid, out var surah) || surah.Type != N.Surah.Type) return NotFound($"Surah '{uid}' not found.");

var passages = Q().StartAt(uid).Out(N.Cluster.Type, E.HasCluster).AsEnumerable()
                  .Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE)
                  .OrderBy(c => c.GetInt(N.Cluster.Ordinal)).ToList();

var rows = new List<QK.PassageRow>();
foreach (var p in passages)
{
    var verses = Q().StartAt(p.UID).Out(N.Verse.Type, E.HasMember).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.AyahNumber)).ToList();
    var first  = verses.FirstOrDefault();
    var theme  = Q().StartAt(p.UID).Out(N.Cluster.Type, E.InTheme).AsEnumerable().FirstOrDefault();
    var themeSurahs = theme is null ? 0
                    : Q().StartAt(theme.UID).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().Select(x => x.GetInt(N.Cluster.SurahNumber)).Distinct().Count();

    rows.Add(new QK.PassageRow
    {
        Uid                = p.UID,
        SurahUid           = uid,
        SurahNumber        = surah.GetInt(N.Surah.Number),
        SurahName          = surah.GetString(N.Surah.NameScholarly),
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
        ThemeSurahs        = themeSurahs,
    });
}

return new QK.SurahPassagesDto
{
    Uid      = uid,
    Built    = rows.Count > 0,
    Method   = passages.Select(p => p.GetString(N.Cluster.Method)).FirstOrDefault() ?? "",
    RunId    = passages.Select(p => p.GetString(N.Cluster.RunId)).FirstOrDefault() ?? "",
    Passages = rows.ToArray(),
};
