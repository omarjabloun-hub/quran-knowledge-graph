[endpoint: Curiosity.Endpoints.Path("qkg/clusters/map")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// Both groupings of the passages at a glance, for the map on the Stories and themes page: the 114
// surahs, every passage in mushaf order with its verse range, its size in words (counted live from
// its verses' edges), the THEME it belongs to (qkg/clusters/build) and the SIMILARITY GROUP it
// belongs to (qkg/clusters/similar-build), and the two lists. Built is false, and the lists empty,
// until the clusters build has run; Groups is empty until the similarity build has.
//
// Not cached: a rebuild of the clusters has to show at once.

var clusters = Q().StartAt(N.Cluster.Type).AsEnumerable().ToList();
var passagesOfKind = clusters.Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE).ToList();

var wordsOfVerse = new Dictionary<UID128, int>();
int Words(UID128 verse)
{
    if (!wordsOfVerse.TryGetValue(verse, out var n)) wordsOfVerse[verse] = n = Q().StartAt(verse).Out(N.Word.Type, E.HasWord).AsUIDEnumerable().Count();
    return n;
}

int Ayah(string verseKey) => int.TryParse((verseKey ?? "").Split(':').Last(), out var a) ? a : 0;

var versesOf  = passagesOfKind.ToDictionary(p => p.UID, p => Q().StartAt(p.UID).Out(N.Verse.Type, E.HasMember).AsUIDEnumerable().ToArray());
var surahOf   = passagesOfKind.ToDictionary(p => p.UID, p => p.GetInt(N.Cluster.SurahNumber));

// A theme or a similarity group, and the passage → group index both colourings read.
QK.MapGroup[] Groups(string kind, Dictionary<UID128, ReadOnlyNode> groupOf)
    => clusters.Where(c => c.GetString(N.Cluster.Kind) == kind).OrderBy(c => c.GetInt(N.Cluster.Ordinal)).Select(g =>
       {
           var members = Q().StartAt(g.UID).Out(N.Cluster.Type, E.HasPassage).AsUIDEnumerable().Where(versesOf.ContainsKey).ToList();
           foreach (var p in members) groupOf[p] = g;
           return new QK.MapGroup
           {
               Uid          = g.UID,
               Kind         = kind,
               Ordinal      = g.GetInt(N.Cluster.Ordinal),
               Label        = g.GetString(N.Cluster.Label),
               LabelArabic  = g.GetString(N.Cluster.LabelArabic),
               Passages     = members.Count,
               Verses       = members.Sum(p => versesOf[p].Length),
               SurahNumbers = members.Select(p => surahOf[p]).Distinct().OrderBy(x => x).ToArray(),
           };
       }).ToArray();

var themeOf = new Dictionary<UID128, ReadOnlyNode>();
var groupOf = new Dictionary<UID128, ReadOnlyNode>();
var themes  = Groups(QK.THEME, themeOf);
var groups  = Groups(QK.SIMILAR_GROUP, groupOf);

var passages = passagesOfKind.OrderBy(c => c.GetInt(N.Cluster.SurahNumber)).ThenBy(c => c.GetInt(N.Cluster.Ordinal))
                             .Select(p =>
                             {
                                 var theme = themeOf.TryGetValue(p.UID, out var t) ? t : null;
                                 var group = groupOf.TryGetValue(p.UID, out var g) ? g : null;
                                 return new QK.MapPassage
                                 {
                                     Uid          = p.UID,
                                     SurahNumber  = p.GetInt(N.Cluster.SurahNumber),
                                     Ordinal      = p.GetInt(N.Cluster.Ordinal),
                                     FirstAyah    = Ayah(p.GetString(N.Cluster.FirstVerseKey)),
                                     LastAyah     = Ayah(p.GetString(N.Cluster.LastVerseKey)),
                                     Words        = versesOf[p.UID].Sum(Words),
                                     LabelArabic  = p.GetString(N.Cluster.LabelArabic),
                                     Label        = p.GetString(N.Cluster.Label),
                                     ThemeUid     = theme?.UID ?? default(UID128),
                                     ThemeOrdinal = theme?.GetInt(N.Cluster.Ordinal) ?? 0,
                                     GroupUid     = group?.UID ?? default(UID128),
                                     GroupOrdinal = group?.GetInt(N.Cluster.Ordinal) ?? 0,
                                 };
                             }).ToArray();

var wordsOfSurah = passages.GroupBy(p => p.SurahNumber).ToDictionary(g => g.Key, g => g.Sum(p => p.Words));

var surahs = Q().StartAt(N.Surah.Type).AsEnumerable().OrderBy(s => s.GetInt(N.Surah.Number))
                .Select(s => new QK.MapSurah
                {
                    Uid        = s.UID,
                    Number     = s.GetInt(N.Surah.Number),
                    Name       = s.GetString(N.Surah.NameScholarly),
                    NameArabic = s.GetString(N.Surah.NameArabic),
                    Place      = s.GetString(N.Surah.RevelationPlace),
                    Words      = wordsOfSurah.TryGetValue(s.GetInt(N.Surah.Number), out var w) ? w : 0,
                }).ToArray();

var built       = passagesOfKind.FirstOrDefault();
var groupsBuilt = clusters.FirstOrDefault(c => c.GetString(N.Cluster.Kind) == QK.SIMILAR_GROUP);

return new QK.ClusterMapDto
{
    Built        = built is object,
    Method       = built?.GetString(N.Cluster.Method) ?? "",
    RunId        = built?.GetString(N.Cluster.RunId) ?? "",
    GroupsMethod = groupsBuilt?.GetString(N.Cluster.Method) ?? "",
    GroupsRunId  = groupsBuilt?.GetString(N.Cluster.RunId) ?? "",
    Surahs       = surahs,
    Passages     = passages,
    Themes       = themes,
    Groups       = groups,
};
