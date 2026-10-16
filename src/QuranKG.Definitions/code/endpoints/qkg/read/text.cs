[endpoint: Curiosity.Endpoints.Path("qkg/read/text")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// The text to read, for the reader of the Quran View and of the passage, theme and group pages: a
// SURAH, a PASSAGE, or a THEME or SIMILARITY GROUP (qkg/clusters/similar-build) as runs of
// consecutive verses. A surah comes in its passages (one section when the clusters have not been
// built); a passage is one section; a theme or a group is its passages in mushaf order. Each
// section carries what the builds say of it — its label, its theme, its similarity group.
//
// Each verse carries the positions of its words whose lemma is a KEY LEMMA, so the reader can
// mark the words a passage or theme is built from: for a surah, each passage's own key lemmas;
// for a passage, its own; for a theme or a group, its own. A key lemma is a content word (no particle,
// pronoun or adverb) its verses use far more than the rest of the Quran: verses using it ×
// log(verses / verses using it)², the rule of qkg/cluster/profile.
//
// IN A READING (riwaya): Qalun, the main one (Riwaya.Rank 1, read when none is asked for), or
// Hafs, the graph's own text. The passages, key lemmas and translations sit on Hafs's verses and words; in
// Qalun a section holds the Qalun verses that START in its Hafs verses, each word carries the Hafs
// word it is (RiwayaVerse.WordLinks), so the key words are marked in the Qalun text, and the
// translation of a Hafs verse goes with the Qalun verse its first word is in.
//
// Not cached: it reads the clusters, and a rebuild has to show at once. A surah of 286 verses
// answers in well under a second.

const int KEY_PER_PASSAGE = 6;
const int KEY_PER_CLUSTER = 12;

var req = Body.FromJson<QK.ReadingRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))         return BadRequest("Uid (a Surah or a Cluster) is required");
if (!Graph.TryGet(uid, out var root)) return NotFound($"'{uid}' not found.");
if (root.Type != N.Surah.Type && root.Type != N.Cluster.Type) return BadRequest("Uid must be a Surah or a Cluster.");

var totalVerses = (double)Q().StartAt(N.Verse.Type).AsUIDEnumerable().Count();

// The readings on offer, the main one first; the one asked for, or the main one.
var riwayaNodes = Q().StartAt(N.Riwaya.Type).AsEnumerable().OrderBy(r => r.GetInt(N.Riwaya.Rank)).ToList();
var asked       = (req?.Riwaya ?? "").Trim();
var riwaya      = asked.Length == 0 ? riwayaNodes.FirstOrDefault() : riwayaNodes.FirstOrDefault(r => r.GetString(N.Riwaya.RiwayaId) == asked);
if (riwaya is null || riwaya.GetBool(N.Riwaya.IsCorpusText)) riwaya = null;   // null: Hafs, the Verse nodes
var riwayaId    = riwaya?.GetString(N.Riwaya.RiwayaId) ?? riwayaNodes.FirstOrDefault(r => r.GetBool(N.Riwaya.IsCorpusText))?.GetString(N.Riwaya.RiwayaId) ?? "hafs";
var dfCache     = new Dictionary<UID128, int>();
var keepCache   = new Dictionary<UID128, bool>();

int Df(UID128 lemma)
{
    if (!dfCache.TryGetValue(lemma, out var df)) dfCache[lemma] = df = Q().StartAt(lemma).Out(N.Verse.Type, E.LemmaInVerse).AsUIDEnumerable().Count();
    return df;
}

bool IsContent(UID128 lemma)
{
    if (!keepCache.TryGetValue(lemma, out var keep))
        keepCache[lemma] = keep = Graph.TryGet(lemma, out var n)
                               && n.GetString(N.Lemma.PosTag) != "P" && QK.IsContentNoun(n.GetString(N.Lemma.PartOfSpeech));
    return keep;
}

QK.Counted[] KeyLemmas(List<ReadOnlyNode> verses, int take)
{
    var counts = new Dictionary<UID128, int>();
    foreach (var v in verses)
        foreach (var t in Q().StartAt(v.UID).Out(N.Lemma.Type, E.VerseHasLemma).AsUIDEnumerable())
            counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;

    var minimum = verses.Count >= 10 ? 2 : 1;
    return counts.Where(kv => kv.Value >= minimum && IsContent(kv.Key))
                 .Select(kv => (Uid: kv.Key, Count: kv.Value, Df: Df(kv.Key)))
                 .Where(x => x.Df > 0 && x.Df <= totalVerses / 2)
                 .Select(x => (x.Uid, x.Count, x.Df, Score: x.Count * Math.Pow(Math.Log(totalVerses / x.Df), 2)))
                 .OrderByDescending(x => x.Score).ThenBy(x => x.Uid.ToString(), StringComparer.Ordinal).Take(take)
                 .Select(x => new QK.Counted
                 {
                     Uid    = x.Uid,
                     Label  = Graph.TryGet(x.Uid, out var n) ? n.GetString(N.Lemma.LemmaText) : "",
                     Detail = $"in {x.Count} of these {verses.Count} verses, {x.Df} of {totalVerses:N0} in the Quran",
                     Count  = x.Count,
                     Score  = Math.Round(x.Score, 2),
                 }).ToArray();
}

// The words of these verses whose lemma is one of the key lemmas, by verse key → positions.
Dictionary<string, List<int>> Highlights(List<ReadOnlyNode> verses, QK.Counted[] key)
{
    var keys = new HashSet<string>(verses.Select(v => v.GetString(N.Verse.VerseKey)), StringComparer.Ordinal);
    var hits = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    foreach (var lemma in key)
        foreach (var w in Q().StartAt(lemma.Uid).Out(N.Word.Type, E.LemmaOfWord).AsEnumerable())
        {
            var vk = w.GetString(N.Word.VerseKey);
            if (!keys.Contains(vk)) continue;
            if (!hits.TryGetValue(vk, out var list)) hits[vk] = list = new List<int>();
            list.Add(w.GetInt(N.Word.Position));
        }
    return hits;
}

List<ReadOnlyNode> VersesOf(UID128 passage)
    => Q().StartAt(passage).Out(N.Verse.Type, E.HasMember).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.AyahNumber)).ToList();

QK.ReadingVerse[] Lines(List<ReadOnlyNode> verses, Dictionary<string, List<int>> hits)
    => verses.Select(v =>
       {
           var key = v.GetString(N.Verse.VerseKey);
           return new QK.ReadingVerse
           {
               Uid         = v.UID,
               VerseKey    = key,
               Ayah        = v.GetInt(N.Verse.AyahNumber),
               Text        = v.GetString(N.Verse.TextUthmani),
               Translation = v.GetString(N.Verse.EnglishSaheeh),
               Words       = Q().StartAt(v.UID).Out(N.Word.Type, E.HasWord).AsUIDEnumerable().Count(),
               Highlights  = hits.TryGetValue(key, out var p) ? p.Distinct().OrderBy(x => x).ToArray() : new int[0],
               Sajda       = v.GetString(N.Verse.SajdaType) ?? "",
               Page        = v.GetInt(N.Verse.Page),
               Juz         = v.GetInt(N.Verse.Juz),
               Hizb        = v.GetInt(N.Verse.Hizb),
               HafsVerses  = new[] { v.UID },
               HafsKeys    = new[] { key },
           };
       }).ToArray();

// The same verses in another reading: its verses that START in them (by their first word's Hafs
// verse), each word mapped to its Hafs word.
QK.ReadingVerse[] RiwayaLines(List<ReadOnlyNode> verses, Dictionary<string, List<int>> hits)
{
    var inSection = new HashSet<string>(verses.Select(v => v.GetString(N.Verse.VerseKey)), StringComparer.Ordinal);
    var mine      = verses.SelectMany(v => Q().StartAt(v.UID).Out(N.RiwayaVerse.Type, E.VerseReadIn).AsEnumerable())
                          .Where(r => r.GetString(N.RiwayaVerse.RiwayaId) == riwayaId)
                          .GroupBy(r => r.UID).Select(g => g.First())
                          .Where(r => inSection.Contains((r.GetString(N.RiwayaVerse.HafsVerseKeys) ?? "").Split(' ')[0]))
                          .OrderBy(r => r.GetInt(N.RiwayaVerse.SurahNumber)).ThenBy(r => r.GetInt(N.RiwayaVerse.AyahNumber)).ToList();

    return mine.Select(r =>
    {
        var key    = r.GetString(N.RiwayaVerse.VerseKey);
        var hafs   = Q().StartAt(r.UID).Out(N.Verse.Type, E.ReadsVerse).AsEnumerable().ToDictionary(v => v.GetString(N.Verse.VerseKey), v => v, StringComparer.Ordinal);
        var keys   = (r.GetString(N.RiwayaVerse.HafsVerseKeys) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(hafs.ContainsKey).ToArray();
        var links  = (r.GetString(N.RiwayaVerse.WordLinks) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var verse  = new int[links.Length];
        var pos    = new int[links.Length];
        var marked = new List<int>();
        var starts = new List<string>();   // the Hafs verses whose first word is here
        for (var i = 0; i < links.Length; i++)
        {
            var cut = links[i].LastIndexOf(':');
            if (links[i] == "-" || cut < 0) { verse[i] = -1; continue; }
            var hk = links[i].Substring(0, cut);
            var p  = int.Parse(links[i].Substring(cut + 1));
            verse[i] = Array.IndexOf(keys, hk);
            pos[i]   = p;
            if (p == 1 && !starts.Contains(hk)) starts.Add(hk);
            if (hits.TryGetValue(hk, out var h) && h.Contains(p)) marked.Add(i + 1);
        }

        return new QK.ReadingVerse
        {
            Uid          = r.UID,
            VerseKey     = key,
            Ayah         = r.GetInt(N.RiwayaVerse.AyahNumber),
            Text         = r.GetString(N.RiwayaVerse.Text),
            Translation  = string.Join(" ", starts.Where(hafs.ContainsKey).Select(hk => (starts.Count > 1 || hk != key ? "(" + hk + ") " : "") + hafs[hk].GetString(N.Verse.EnglishSaheeh))),
            Words        = links.Length,
            Highlights   = marked.ToArray(),
            Sajda        = keys.Select(hk => hafs[hk].GetString(N.Verse.SajdaType) ?? "").FirstOrDefault(x => x.Length > 0) ?? "",
            Page         = r.GetInt(N.RiwayaVerse.Page),
            Juz          = r.GetInt(N.RiwayaVerse.Juz),
            Hizb         = r.GetInt(N.RiwayaVerse.Hizb),
            HafsVerses   = keys.Select(hk => hafs[hk].UID).ToArray(),
            HafsKeys     = keys,
            WordVerse    = verse,
            WordPosition = pos,
        };
    }).ToArray();
}

// A reading's verses counted surah by surah, surah 1 first.
int[] SurahCounts(ReadOnlyNode r)
{
    var counts = new int[114];
    if (r.GetBool(N.Riwaya.IsCorpusText))
        foreach (var v in Q().StartAt(N.Verse.Type).AsEnumerable()) { var n = v.GetInt(N.Verse.SurahNumber); if (n >= 1 && n <= 114) counts[n - 1]++; }
    else
        foreach (var v in Q().StartAt(r.UID).Out(N.RiwayaVerse.Type, E.RiwayaHasVerse).AsEnumerable()) { var n = v.GetInt(N.RiwayaVerse.SurahNumber); if (n >= 1 && n <= 114) counts[n - 1]++; }
    return counts;
}

// The similarity group of each passage: the groups name their passages, the passages do not name them.
Dictionary<UID128, ReadOnlyNode> groupOf = null;
ReadOnlyNode GroupOf(UID128 passage)
{
    if (groupOf is null)
    {
        groupOf = new Dictionary<UID128, ReadOnlyNode>();
        foreach (var g in Q().StartAt(N.Cluster.Type).AsEnumerable().Where(c => c.GetString(N.Cluster.Kind) == QK.SIMILAR_GROUP))
            foreach (var p in Q().StartAt(g.UID).Out(N.Cluster.Type, E.HasPassage).AsUIDEnumerable()) groupOf[p] = g;
    }
    return groupOf.TryGetValue(passage, out var group) ? group : null;
}

ReadOnlyNode SurahOf(int number) => Q().StartAt(N.Surah.Type).AsEnumerable().FirstOrDefault(s => s.GetInt(N.Surah.Number) == number);

QK.ReadingSection Section(ReadOnlyNode passage, ReadOnlyNode surah, List<ReadOnlyNode> verses, Dictionary<string, List<int>> hits)
{
    var theme = passage is null ? null : Q().StartAt(passage.UID).Out(N.Cluster.Type, E.InTheme).AsEnumerable().FirstOrDefault(c => c.GetString(N.Cluster.Kind) == QK.THEME);
    var group = passage is null ? null : GroupOf(passage.UID);
    return new QK.ReadingSection
    {
        PassageUid       = passage?.UID ?? default(UID128),
        Ordinal          = passage?.GetInt(N.Cluster.Ordinal) ?? 1,
        Label            = passage?.GetString(N.Cluster.Label) ?? "",
        LabelArabic      = passage?.GetString(N.Cluster.LabelArabic) ?? "",
        SurahUid         = surah?.UID ?? default(UID128),
        SurahNumber      = surah?.GetInt(N.Surah.Number) ?? 0,
        SurahName        = surah?.GetString(N.Surah.NameScholarly) ?? "",
        SurahNameArabic  = surah?.GetString(N.Surah.NameArabic) ?? "",
        ThemeUid         = theme?.UID ?? default(UID128),
        ThemeLabel       = theme?.GetString(N.Cluster.Label) ?? "",
        ThemeLabelArabic = theme?.GetString(N.Cluster.LabelArabic) ?? "",
        ThemeSurahs      = theme is null ? 0 : Q().StartAt(theme.UID).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().Select(x => x.GetInt(N.Cluster.SurahNumber)).Distinct().Count(),
        ThemeOrdinal     = theme?.GetInt(N.Cluster.Ordinal) ?? 0,
        GroupUid         = group?.UID ?? default(UID128),
        GroupLabelArabic = group?.GetString(N.Cluster.LabelArabic) ?? "",
        GroupOrdinal     = group?.GetInt(N.Cluster.Ordinal) ?? 0,
        GroupSurahs      = group is null ? 0 : Q().StartAt(group.UID).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().Select(x => x.GetInt(N.Cluster.SurahNumber)).Distinct().Count(),
        Verses           = riwaya is null ? Lines(verses, hits) : RiwayaLines(verses, hits),
    };
}

var anyCluster = Q().StartAt(N.Cluster.Type).AsEnumerable().FirstOrDefault(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE);
var result     = new QK.ReadingDto
{
    Uid           = uid,
    PassagesBuilt = anyCluster is object,
    Method        = anyCluster?.GetString(N.Cluster.Method) ?? "",
    RunId         = anyCluster?.GetString(N.Cluster.RunId) ?? "",
    KeyLemmas     = new QK.Counted[0],
    Riwaya        = riwayaId,
    Riwayat       = riwayaNodes.Select((r, i) => new QK.RiwayaOption
    {
        Uid          = r.UID,
        Id           = r.GetString(N.Riwaya.RiwayaId),
        Name         = r.GetString(N.Riwaya.RiwayaName),
        NameArabic   = r.GetString(N.Riwaya.RiwayaNameArabic),
        Description  = r.GetString(N.Riwaya.Description),
        Attribution  = r.GetString(N.Riwaya.Attribution),
        Verses       = r.GetBool(N.Riwaya.IsCorpusText) ? (int)totalVerses : Q().StartAt(r.UID).Out(N.RiwayaVerse.Type, E.RiwayaHasVerse).AsUIDEnumerable().Count(),
        SurahVerses  = SurahCounts(r),
        IsMain       = i == 0,
        IsCorpusText = r.GetBool(N.Riwaya.IsCorpusText),
    }).ToArray(),
};
var sections = new List<QK.ReadingSection>();

if (root.Type == N.Surah.Type)
{
    var number = root.GetInt(N.Surah.Number);
    result.Kind        = N.Surah.Type;
    result.Label       = number + ". " + root.GetString(N.Surah.NameScholarly);
    result.LabelArabic = root.GetString(N.Surah.NameArabic);
    result.Previous    = SurahOf(number - 1)?.UID ?? default(UID128);
    result.Next        = SurahOf(number + 1)?.UID ?? default(UID128);

    var passages = Q().StartAt(uid).Out(N.Cluster.Type, E.HasCluster).AsEnumerable()
                      .Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE)
                      .OrderBy(c => c.GetInt(N.Cluster.Ordinal)).ToList();

    if (passages.Count == 0)
    {
        var verses = Q().StartAt(uid).Out(N.Verse.Type, E.HasVerse).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.AyahNumber)).ToList();
        sections.Add(Section(null, root, verses, new Dictionary<string, List<int>>()));
    }
    else
    {
        foreach (var p in passages)
        {
            var verses = VersesOf(p.UID);
            sections.Add(Section(p, root, verses, Highlights(verses, KeyLemmas(verses, KEY_PER_PASSAGE))));
        }
    }
}
else
{
    // A theme and a similarity group are both passages of several surahs.
    var isTheme  = root.GetString(N.Cluster.Kind) != QK.PASSAGE;
    var passages = (isTheme ? Q().StartAt(uid).Out(N.Cluster.Type, E.HasPassage).AsEnumerable().ToList() : new List<ReadOnlyNode> { root })
                      .OrderBy(p => p.GetInt(N.Cluster.SurahNumber)).ThenBy(p => p.GetInt(N.Cluster.Ordinal)).ToList();
    var versesOf = passages.ToDictionary(p => p.UID, p => VersesOf(p.UID));
    var all      = passages.SelectMany(p => versesOf[p.UID]).ToList();
    var key      = KeyLemmas(all, KEY_PER_CLUSTER);
    var hits     = Highlights(all, key);

    result.Kind        = root.GetString(N.Cluster.Kind);
    result.Label       = root.GetString(N.Cluster.Label);
    result.LabelArabic = root.GetString(N.Cluster.LabelArabic);
    result.KeyLemmas   = key;

    foreach (var p in passages)
        sections.Add(Section(p, Q().StartAt(p.UID).Out(N.Surah.Type, E.ClusterOfSurah).AsEnumerable().FirstOrDefault(), versesOf[p.UID], hits));

    if (isTheme)
    {
        result.Method = root.GetString(N.Cluster.Method);
        result.RunId  = root.GetString(N.Cluster.RunId);
    }
    else
    {
        var surah = Q().StartAt(uid).Out(N.Surah.Type, E.ClusterOfSurah).AsUIDEnumerable().FirstOrDefault();
        UID128 Neighbour(int ordinal)
            => Q().StartAt(surah).Out(N.Cluster.Type, E.HasCluster).AsEnumerable()
                  .Where(c => c.GetString(N.Cluster.Kind) == QK.PASSAGE && c.GetInt(N.Cluster.Ordinal) == ordinal)
                  .Select(c => c.UID).FirstOrDefault();
        result.Previous = Neighbour(root.GetInt(N.Cluster.Ordinal) - 1);
        result.Next     = Neighbour(root.GetInt(N.Cluster.Ordinal) + 1);
    }
}

result.Sections = sections.ToArray();
return result;
