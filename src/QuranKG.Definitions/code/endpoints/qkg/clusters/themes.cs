[endpoint: Curiosity.Endpoints.Path("qkg/clusters/themes")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// The themes qkg/clusters/build found — passages of several surahs that tell the same story or
// treat the same subject — widest first, each with the surahs it spans and its size, counted live
// from its edges. Built is false, and the list empty, until the build has run.
//
// Not cached: a rebuild of the clusters has to show at once, and this answers in a fifth of a second.

var clusters = Q().StartAt(N.Cluster.Type).AsEnumerable().ToList();
var themes   = clusters.Where(c => c.GetString(N.Cluster.Kind) == QK.THEME).OrderBy(c => c.GetInt(N.Cluster.Ordinal)).ToList();
var surahs   = Q().StartAt(N.Surah.Type).AsEnumerable().ToDictionary(s => s.GetInt(N.Surah.Number));

var rows = new List<QK.ThemeRow>();
var versesInThemes = 0;
foreach (var t in themes)
{
    var passages = Q().StartAt(t.UID).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().ToList();
    var numbers  = passages.Select(p => p.GetInt(N.Cluster.SurahNumber)).Where(surahs.ContainsKey).Distinct().OrderBy(x => x).ToArray();
    var verses   = passages.Sum(p => Q().StartAt(p.UID).Out(N.Verse.Type, E.HasMember).AsUIDEnumerable().Count());
    versesInThemes += verses;

    rows.Add(new QK.ThemeRow
    {
        Uid          = t.UID,
        Ordinal      = t.GetInt(N.Cluster.Ordinal),
        Label        = t.GetString(N.Cluster.Label),
        LabelArabic  = t.GetString(N.Cluster.LabelArabic),
        Passages     = passages.Count,
        Verses       = verses,
        SurahUIDs    = numbers.Select(n => surahs[n].UID).ToArray(),
        SurahNumbers = numbers,
        SurahNames   = numbers.Select(n => surahs[n].GetString(N.Surah.NameScholarly)).ToArray(),
    });
}

// Built, Method and RunId from this build's own clusters: qkg/clusters/similar-build writes Clusters too.
var own = clusters.Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE || c.GetString(N.Cluster.Kind) == QK.THEME).ToList();

return new QK.ThemesDto
{
    Built          = own.Count > 0,
    Method         = own.Select(c => c.GetString(N.Cluster.Method)).FirstOrDefault() ?? "",
    RunId          = own.Select(c => c.GetString(N.Cluster.RunId)).FirstOrDefault() ?? "",
    Passages       = clusters.Count(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE),
    Verses         = Q().StartAt(N.Verse.Type).AsUIDEnumerable().Count(),
    VersesInThemes = versesInThemes,
    Themes         = rows.ToArray(),
};
