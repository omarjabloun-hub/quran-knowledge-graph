[endpoint: Curiosity.Endpoints.Path("qkg/tajweed/rules")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// The tajweed of the main reading's text (Qalun), rule by rule: how many letters each rule is on
// and in how many verses, counted live from RiwayaVerse.Tajweed (written by the connector from
// QuranKG.Dataset.Text.Tajweed), and words to learn it from — Al-Fatiha's first, then Juz 'Amma's
// (114 down to 78), the surahs a student learns first, then the rest of the book; one word per
// surah, each word once.
//
// Not cached: it reads 6,214 verses in well under a second, and a re-ingest must show at once.

var req  = Body.FromJson<QK.TajweedRequest>();
var take = Math.Max(1, Math.Min(24, req?.Examples > 0 ? req.Examples : 6));

var riwaya = Q().StartAt(N.Riwaya.Type).AsEnumerable().Where(r => !r.GetBool(N.Riwaya.IsCorpusText))
                .OrderBy(r => r.GetInt(N.Riwaya.Rank)).FirstOrDefault();
if (riwaya is null) return new QK.TajweedDto { Rules = new QK.TajweedRuleStats[0] };

var id     = riwaya.GetString(N.Riwaya.RiwayaId);
var verses = Q().StartAt(riwaya.UID).Out(N.RiwayaVerse.Type, E.RiwayaHasVerse).AsEnumerable().ToList();

// The order examples are looked for in: Al-Fatiha, Juz 'Amma from An-Nas back, then the book.
int Order(int surah) => surah == 1 ? 0 : surah >= 78 ? 1000 + (114 - surah) : 2000 + surah;

var counts  = new Dictionary<string, int>(StringComparer.Ordinal);
var inVerse = new Dictionary<string, int>(StringComparer.Ordinal);
var found   = new Dictionary<string, List<QK.TajweedExample>>(StringComparer.Ordinal);
var seen    = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

foreach (var v in verses.OrderBy(v => Order(v.GetInt(N.RiwayaVerse.SurahNumber))).ThenBy(v => v.GetInt(N.RiwayaVerse.AyahNumber)))
{
    var text  = v.GetString(N.RiwayaVerse.Text) ?? "";
    var marks = (v.GetString(N.RiwayaVerse.Tajweed) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(m =>
                   {
                       var colon = m.LastIndexOf(':');
                       var dash  = m.LastIndexOf('-');
                       return (Rule: m.Substring(0, colon), Start: int.Parse(m.Substring(colon + 1, dash - colon - 1)), End: int.Parse(m.Substring(dash + 1)));
                   }).ToList();

    foreach (var rule in marks.Select(m => m.Rule).Distinct())
        inVerse[rule] = inVerse.TryGetValue(rule, out var n) ? n + 1 : 1;

    var surah = v.GetInt(N.RiwayaVerse.SurahNumber);
    foreach (var m in marks)
    {
        counts[m.Rule] = counts.TryGetValue(m.Rule, out var c) ? c + 1 : 1;

        if (!found.TryGetValue(m.Rule, out var list)) { found[m.Rule] = list = new List<QK.TajweedExample>(); seen[m.Rule] = new HashSet<string>(StringComparer.Ordinal); }
        if (list.Count >= take || list.Any(x => x.SurahNumber == surah && list.Count < take - 1 && Order(surah) > 0)) continue;

        // The word the letters are in.
        var from = text.LastIndexOf(' ', Math.Max(0, m.Start - 1)) + 1;
        var to   = text.IndexOf(' ', m.End);
        if (to < 0) to = text.Length;
        var word = text.Substring(from, to - from);
        if (!seen[m.Rule].Add(word)) continue;

        list.Add(new QK.TajweedExample
        {
            Uid         = v.UID,
            VerseKey    = v.GetString(N.RiwayaVerse.VerseKey),
            HafsKey     = (v.GetString(N.RiwayaVerse.HafsVerseKeys) ?? "").Split(' ')[0],
            SurahNumber = surah,
            Word        = word,
            MarkStart   = m.Start - from,
            MarkEnd     = m.End - from,
            Tajweed     = string.Join(" ", marks.Where(x => x.Start >= from && x.End <= to).Select(x => x.Rule + ":" + (x.Start - from) + "-" + (x.End - from))),
        });
    }
}

return new QK.TajweedDto
{
    Riwaya           = id,
    RiwayaName       = riwaya.GetString(N.Riwaya.RiwayaName),
    RiwayaNameArabic = riwaya.GetString(N.Riwaya.RiwayaNameArabic),
    Verses           = verses.Count,
    Rules            = counts.Keys.Select(rule => new QK.TajweedRuleStats
    {
        Rule     = rule,
        Count    = counts[rule],
        Verses   = inVerse.TryGetValue(rule, out var n) ? n : 0,
        Examples = found.TryGetValue(rule, out var list) ? list.ToArray() : new QK.TajweedExample[0],
    }).ToArray(),
};
