[endpoint: Curiosity.Endpoints.Path("qkg/hadith/overview")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// The hadith and the Quran at a glance, counted live from the graph: per collection, how many
// hadith quote a verse or share a phrase with one; the detector measured against the translators'
// bracketed citations it never reads (TranslatorCited / TranslatorFound); the verses and surahs the
// hadith quote most (quotations only); every book with how many of its hadith quote the Quran; how many hadith have
// been embedded. Not cached (0.2 s): a re-ingest, or the index filling, shows at once.

var started = DateTime.UtcNow;

var surahs = Q().StartAt(N.Surah.Type).AsEnumerable().OrderBy(s => s.GetInt(N.Surah.Number)).ToList();
var verses = Q().StartAt(N.Verse.Type).AsEnumerable().ToDictionary(v => v.GetString(N.Verse.VerseKey), v => v, StringComparer.Ordinal);

var hadiths = Q().StartAt(N.Hadith.Type).AsEnumerable().Select(h => new
{
    h.UID,
    Key        = h.GetString(N.Hadith.HadithKey),
    Collection = h.GetString(N.Hadith.CollectionId),
    Book       = h.GetString(N.Hadith.CollectionId) + ":" + h.GetInt(N.Hadith.BookNumber),
    Variant    = h.GetBool(N.Hadith.IsVariant),
    Cites      = (h.GetString(N.Hadith.TranslatorCites) ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries),
}).ToList();
var hadithByKey = hadiths.ToDictionary(h => h.Key, StringComparer.Ordinal);

var quotes = Q().StartAt(N.QuranQuote.Type).AsEnumerable().Select(q => new
{
    Hadith = q.GetString(N.QuranQuote.HadithKey),
    Verse  = q.GetString(N.QuranQuote.VerseKey),
    Kind   = q.GetString(N.QuranQuote.Kind),
}).Where(q => hadithByKey.ContainsKey(q.Hadith)).ToList();

var arabic  = quotes.Where(q => q.Kind != "translator").ToList();
var quoting = new HashSet<string>(arabic.Where(q => q.Kind == "quotation").Select(q => q.Hadith), StringComparer.Ordinal);
var phrase  = new HashSet<string>(arabic.Select(q => q.Hadith).Where(h => !quoting.Contains(h)), StringComparer.Ordinal);
var quotedBy = arabic.GroupBy(q => q.Hadith).ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(q => q.Verse), StringComparer.Ordinal), StringComparer.Ordinal);

var index = Graph.Embeddings.GetIndex(N.Hadith.Type, N.Hadith.Matn);

var collections = Q().StartAt(N.HadithCollection.Type).AsEnumerable().OrderBy(c => c.GetString(N.HadithCollection.CollectionId) == "bukhari" ? 0 : 1).Select(c =>
{
    var id   = c.GetString(N.HadithCollection.CollectionId);
    var mine = hadiths.Where(h => h.Collection == id).ToList();
    var cited = mine.Where(h => h.Cites.Length > 0).ToList();
    return new QK.HadithCollectionStats
    {
        Uid             = c.UID,
        Id              = id,
        Name            = c.GetString(N.HadithCollection.CollectionName),
        NameArabic      = c.GetString(N.HadithCollection.CollectionNameArabic),
        Hadith          = mine.Count,
        Quoting         = mine.Count(h => quoting.Contains(h.Key)),
        Phrase          = mine.Count(h => phrase.Contains(h.Key)),
        Variants        = mine.Count(h => h.Variant),
        TranslatorCited = cited.Count,
        TranslatorFound = cited.Count(h => quotedBy.TryGetValue(h.Key, out var vs) && h.Cites.Any(vs.Contains)),
        Embedded        = index is null ? 0 : mine.Count(h => index.TryGetVector(h.UID, out _)),
    };
}).ToArray();

// The verses quoted by the most hadith — quotations only: a shared phrase is often a formula
// ("وإقام الصلاة وإيتاء الزكاة"), which would put 24:37 near the top.
var quotations = arabic.Where(q => q.Kind == "quotation").ToList();
var topVerses = quotations.GroupBy(q => q.Verse).Select(g => (Verse: g.Key, Hadith: g.Select(q => q.Hadith).Distinct().Count()))
                      .OrderByDescending(x => x.Hadith).ThenBy(x => x.Verse, StringComparer.Ordinal).Take(25)
                      .Where(x => verses.ContainsKey(x.Verse))
                      .Select(x =>
                      {
                          var v = verses[x.Verse];
                          var text = v.GetString(N.Verse.TextUthmani) ?? "";
                          return new QK.Counted { Uid = v.UID, Label = x.Verse, Detail = text.Length > 90 ? text.Substring(0, 90) + " …" : text, Count = x.Hadith };
                      }).ToArray();

// Every surah, with the hadith quoting one of its verses.
var bySurah = quotations.GroupBy(q => int.Parse(q.Verse.Split(':')[0])).ToDictionary(g => g.Key, g => g.Select(q => q.Hadith).Distinct().Count());
var surahRows = surahs.Select(s => new QK.Counted
{
    Uid    = s.UID,
    Label  = s.GetInt(N.Surah.Number) + ". " + s.GetString(N.Surah.NameScholarly),
    Detail = s.GetString(N.Surah.NameArabic),
    Count  = bySurah.TryGetValue(s.GetInt(N.Surah.Number), out var n) ? n : 0,
}).ToArray();

// Every book.
var byBook = hadiths.GroupBy(h => h.Book).ToDictionary(g => g.Key, g => (Hadith: g.Count(), Quoting: g.Count(h => quoting.Contains(h.Key) || phrase.Contains(h.Key))), StringComparer.Ordinal);
var books = Q().StartAt(N.HadithBook.Type).AsEnumerable().Select(b =>
{
    var key = b.GetString(N.HadithBook.BookKey);
    var n   = byBook.TryGetValue(key, out var x) ? x : (0, 0);
    return new QK.HadithBookStats
    {
        Uid          = b.UID,
        CollectionId = b.GetString(N.HadithBook.CollectionId),
        Number       = b.GetInt(N.HadithBook.BookNumber),
        Name         = b.GetString(N.HadithBook.BookName),
        NameArabic   = b.GetString(N.HadithBook.BookNameArabic),
        Hadith       = n.Item1,
        Quoting      = n.Item2,
    };
}).Where(b => b.Hadith > 0).OrderBy(b => b.CollectionId == "bukhari" ? 0 : 1).ThenBy(b => b.Number).ToArray();

return new QK.HadithOverviewDto
{
    Collections  = collections,
    TopVerses    = topVerses,
    Surahs       = surahRows,
    Books        = books,
    Milliseconds = Math.Round((DateTime.UtcNow - started).TotalMilliseconds),
};
