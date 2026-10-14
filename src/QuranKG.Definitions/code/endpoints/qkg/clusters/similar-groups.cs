[endpoint: Curiosity.Endpoints.Path("qkg/clusters/similar-groups")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

// The SimilarGroup clusters qkg/clusters/similar-build found: passages of several surahs that talk
// about the same thing, largest first, each with its passages (surah, verse range, label).
// Not cached: a rebuild shows at once.

var groups = Q().StartAt(N.Cluster.Type).AsEnumerable().Where(c => c.GetString(N.Cluster.Kind) == "SimilarGroup").OrderBy(c => c.GetInt(N.Cluster.Ordinal)).ToList();
var surahs = Q().StartAt(N.Surah.Type).AsEnumerable().ToDictionary(s => s.GetInt(N.Surah.Number), s => s.GetString(N.Surah.NameScholarly));

var rows = groups.Select(g =>
{
    var passages = Q().StartAt(g.UID).Out(N.Cluster.Type, E.HasPassage).AsEnumerable()
        .OrderBy(p => p.GetInt(N.Cluster.SurahNumber)).ThenBy(p => p.GetInt(N.Cluster.Ordinal)).ToList();
    return new
    {
        Uid = g.UID,
        Ordinal = g.GetInt(N.Cluster.Ordinal),
        LabelArabic = g.GetString(N.Cluster.LabelArabic),
        Passages = passages.Count,
        Surahs = passages.Select(p => p.GetInt(N.Cluster.SurahNumber)).Distinct().Count(),
        Members = passages.Select(p => new
        {
            Uid = p.UID,
            SurahNumber = p.GetInt(N.Cluster.SurahNumber),
            SurahName = surahs.TryGetValue(p.GetInt(N.Cluster.SurahNumber), out var n) ? n : "",
            From = p.GetString(N.Cluster.FirstVerseKey),
            To = p.GetString(N.Cluster.LastVerseKey),
            Label = p.GetString(N.Cluster.Label),
        }).ToArray(),
    };
}).ToArray();

return new
{
    Built = rows.Length > 0,
    RunId = groups.Select(g => g.GetString(N.Cluster.RunId)).FirstOrDefault() ?? "",
    Method = groups.Select(g => g.GetString(N.Cluster.Method)).FirstOrDefault() ?? "",
    Groups = rows,
};

