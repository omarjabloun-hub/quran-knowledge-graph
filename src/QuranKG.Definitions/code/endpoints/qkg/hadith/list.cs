[endpoint: Curiosity.Endpoints.Path("qkg/hadith/list")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// Hadith to read, filtered: a collection, a book, a surah they quote, how they meet the Quran
// (Kind: "quotation", "phrase", "any" of the two, or "all" — every hadith), and words to find —
// Arabic in any spelling (diacritics and hamza forms ignored), English, or a reference
// ("Bukhari 6018", "Muslim 8a"; the other filters then do not apply). In collection order; Total
// counts them all, Rows is the page. Not cached: a re-ingest shows at once.

const int MAX_TAKE = 100;

var req  = Body.FromJson<QK.HadithListRequest>() ?? new QK.HadithListRequest();
var take = req.Take <= 0 ? 25 : Math.Min(req.Take, MAX_TAKE);
var kind = string.IsNullOrWhiteSpace(req.Kind) ? "any" : req.Kind.Trim().ToLowerInvariant();

string Cut(string text, int length) => string.IsNullOrEmpty(text) || text.Length <= length ? text ?? "" : text.Substring(0, length).TrimEnd() + " …";

IEnumerable<ReadOnlyNode> pool = req.BookUid != default(UID128)
    ? Q().StartAt(req.BookUid).Out(N.Hadith.Type, E.HadithBookHasHadith).AsEnumerable()
    : !string.IsNullOrWhiteSpace(req.Collection)
        ? Q().StartAt(N.HadithCollection.Type).AsEnumerable().Where(c => c.GetString(N.HadithCollection.CollectionId) == req.Collection.Trim().ToLowerInvariant())
                                               .SelectMany(c => Q().StartAt(c.UID).Out(N.Hadith.Type, E.CollectionHasHadith).AsEnumerable())
        : Q().StartAt(N.Hadith.Type).AsEnumerable();

// How each hadith meets the Quran.
var quotes = Q().StartAt(N.QuranQuote.Type).AsEnumerable()
                .Select(q => (Hadith: q.GetString(N.QuranQuote.HadithKey), Verse: q.GetString(N.QuranQuote.VerseKey), Kind: q.GetString(N.QuranQuote.Kind)))
                .GroupBy(q => q.Hadith).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

bool KindMatches(List<(string Hadith, string Verse, string Kind)> qs)
{
    if (kind == "all") return true;
    if (qs is null) return false;
    var arabic = qs.Where(q => q.Kind != "translator").ToList();
    return kind == "quotation" ? arabic.Any(q => q.Kind == "quotation")
         : kind == "phrase"    ? arabic.Count > 0 && arabic.All(q => q.Kind == "phrase")
         : kind == "translator" ? qs.Any(q => q.Kind == "translator")
         : arabic.Count > 0;
}

bool SurahMatches(List<(string Hadith, string Verse, string Kind)> qs)
    => req.Surah <= 0 || (qs is object && qs.Any(q => q.Kind != "translator" && q.Verse.StartsWith(req.Surah + ":", StringComparison.Ordinal)));

// Words to find.
var text      = (req.Text ?? "").Trim();
var reference = QK.ParseReference(text);
var arabicQuery  = string.Join(" ", QK.MatchTokens(text));
var englishQuery = text.ToLowerInvariant();
bool TextMatches(ReadOnlyNode h)
{
    if (text.Length == 0) return true;
    if (reference.Collection is object)
        return h.GetString(N.Hadith.CollectionId) == reference.Collection
            && (h.GetString(N.Hadith.Reference) ?? "").EndsWith(" " + reference.Number, StringComparison.OrdinalIgnoreCase);
    if (arabicQuery.Length > 0)
        return (" " + string.Join(" ", QK.MatchTokens(h.GetString(N.Hadith.Matn))) + " ").Contains(" " + arabicQuery + " ")
            || string.Join(" ", QK.MatchTokens(h.GetString(N.Hadith.Matn))).Contains(arabicQuery);
    return (h.GetString(N.Hadith.TextEnglish) ?? "").ToLowerInvariant().Contains(englishQuery);
}

// A reference names one hadith: it is found whatever the other filters say.
if (reference.Collection is object) { kind = "all"; req.Surah = 0; }

var matched = pool.Where(h =>
{
    quotes.TryGetValue(h.GetString(N.Hadith.HadithKey), out var qs);
    return KindMatches(qs) && SurahMatches(qs) && TextMatches(h);
}).OrderBy(h => h.GetString(N.Hadith.CollectionId) == "bukhari" ? 0 : 1).ThenBy(h => h.GetInt(N.Hadith.Sequence)).ToList();

var rows = matched.Skip(Math.Max(0, req.Skip)).Take(take).Select(h =>
{
    quotes.TryGetValue(h.GetString(N.Hadith.HadithKey), out var qs);
    var arabic = (qs ?? new List<(string Hadith, string Verse, string Kind)>()).Where(q => q.Kind != "translator").ToList();
    return new QK.HadithRow
    {
        Uid          = h.UID,
        Reference    = h.GetString(N.Hadith.Reference),
        CollectionId = h.GetString(N.Hadith.CollectionId),
        BookName     = h.GetString(N.Hadith.BookName),
        Matn         = Cut(h.GetString(N.Hadith.Matn), 420),
        English      = Cut(h.GetString(N.Hadith.TextEnglish), 420),
        Kind         = arabic.Any(q => q.Kind == "quotation") ? "quotation" : arabic.Count > 0 ? "phrase" : (qs ?? new List<(string Hadith, string Verse, string Kind)>()).Count > 0 ? "translator" : "",
        VerseKeys    = arabic.Select(q => q.Verse).Distinct().ToArray(),
    };
}).ToArray();

return new QK.HadithListDto { Total = matched.Count, Rows = rows };
