[endpoint: Curiosity.Endpoints.Path("qkg/hadith/profile")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// One hadith and the Quran: its texts, where it is, the verses its Arabic quotes (the corpus's
// detector: quotations, shared phrases, and the verses only its translator cites), and the verses
// closest to it — ARABIC FIRST, two signals, each standardised over the 6,236 verses and summed:
//
//   semantic  W_SEM  cosine of the hadith's text (Hadith.Matn, the chain left out) and the verse's
//                    plain Arabic, both embedded by the same model (the Verse.TextSimple and
//                    Hadith.Matn indexes); exact, in batches of 900 (see qkg/verse/similar);
//   lexical   W_LEX  the hadith's distinctive words (Hadith.Keywords: chosen against the other hadith,
//                    so "whoever believes in Allah and the Last Day" does not outweigh "his
//                    neighbour") that the verse uses, each worth log(N/df) over the verses, saturated.
//
// THE WEIGHTS ARE MEASURED (tools/hadith-benchmark): on the 85 hadith whose English translator cites
// a verse their Arabic does not quote, that verse is in the top 10 for 28% of them with both signals
// at 1 (MRR@10 0.151, the best of the grid), 19% by meaning alone, 22% by words alone; chance is 0.2%.
//
// Until the hadith is embedded, Closest is by shared words alone and SemanticReady is false. This
// is a ranking of candidates for a reader, or the assistant, to judge: not a judgement.
//
// Not cached (0.1 s): a re-ingest of the hadith must show at once, not an hour later.

const int    BATCH   = 900;
const int    CLOSEST = 10;
var req   = Body.FromJson<QK.HadithProfileRequest>();
var W_SEM = req?.Semantic ?? 1.0;
var W_LEX = req?.Lexical  ?? 1.0;

var uid = req?.Uid ?? default(UID128);
if (uid == default(UID128))                                         return BadRequest("Uid (a Hadith) is required");
if (!Graph.TryGet(uid, out var hadith) || hadith.Type != N.Hadith.Type) return NotFound($"Hadith '{uid}' not found.");

var collection = Q().StartAt(uid).Out(N.HadithCollection.Type, E.InCollection).AsEnumerable().FirstOrDefault();
var book       = Q().StartAt(uid).Out(N.HadithBook.Type, E.InHadithBook).AsEnumerable().FirstOrDefault();

// The hadith before and after it in its book.
UID128 previous = default(UID128), next = default(UID128);
if (book is object)
{
    var inBook = Q().StartAt(book.UID).Out(N.Hadith.Type, E.HadithBookHasHadith).AsEnumerable().OrderBy(h => h.GetInt(N.Hadith.Sequence)).Select(h => h.UID).ToList();
    var at = inBook.IndexOf(uid);
    if (at > 0)                 previous = inBook[at - 1];
    if (at >= 0 && at + 1 < inBook.Count) next = inBook[at + 1];
}

// ---- the verses it quotes ------------------------------------------------------------------
var quotes = Q().StartAt(uid).Out(N.QuranQuote.Type, E.HasQuranQuote).AsEnumerable().Select(q =>
{
    var verse = Q().StartAt(q.UID).Out(N.Verse.Type, E.QuoteOfVerse).AsEnumerable().FirstOrDefault();
    return new QK.HadithQuoteRow
    {
        Uid         = q.UID,
        VerseUid    = verse?.UID ?? default(UID128),
        VerseKey    = q.GetString(N.QuranQuote.VerseKey),
        Kind        = q.GetString(N.QuranQuote.Kind),
        Words       = q.GetInt(N.QuranQuote.Words),
        VerseWords  = q.GetInt(N.QuranQuote.VerseWords),
        Quoted      = q.GetString(N.QuranQuote.Quoted),
        VerseText   = verse?.GetString(N.Verse.TextUthmani) ?? "",
        Translation = verse?.GetString(N.Verse.EnglishSaheeh) ?? "",
    };
}).OrderBy(q => q.Kind == "quotation" ? 0 : q.Kind == "phrase" ? 1 : 2).ThenByDescending(q => q.Words).ToArray();
var quotedKeys = new HashSet<string>(quotes.Where(q => q.Kind != "translator").Select(q => q.VerseKey), StringComparer.Ordinal);

// ---- the verses closest to it --------------------------------------------------------------
var verses = Q().StartAt(N.Verse.Type).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.MushafIndex)).ToList();
var index  = new Dictionary<UID128, int>();
for (int i = 0; i < verses.Count; i++) index[verses[i].UID] = i;

// Lexical: the hadith's distinctive words (Hadith.Keywords, chosen against the other hadith) that
// a verse uses too, each worth log(N/df) over the verses, saturated as qkg/verse/similar does.
var verseWords = verses.Select(v => new HashSet<string>(QK.MatchTokens(v.GetString(N.Verse.TextSimple)).Select(QK.Stem), StringComparer.Ordinal)).ToList();
var df = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var set in verseWords) foreach (var w in set) df[w] = df.TryGetValue(w, out var c) ? c + 1 : 1;
var hadithWords = new HashSet<string>((hadith.GetString(N.Hadith.Keywords) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
double Idf(string w) => Math.Log((double)verses.Count / df[w]);
var lexical = verseWords.Select(set => 1 - Math.Exp(-1.5 * hadithWords.Where(set.Contains).Sum(Idf) / Math.Log(verses.Count))).ToArray();

// Semantic: the hadith's vector against every verse's.
var semantic  = new double[verses.Count];
var hasVector = new bool[verses.Count];
var verseIndex  = Graph.Embeddings.GetIndex(N.Verse.Type, N.Verse.TextSimple);
var hadithIndex = Graph.Embeddings.GetIndex(N.Hadith.Type, N.Hadith.Matn);
var semanticReady = false;
if (verseIndex is not null && hadithIndex is not null && hadithIndex.TryGetVector(uid, out var vector))
{
    semanticReady = true;
    for (var start = 0; start < verses.Count; start += BATCH)
    {
        var batch = new HashSet<UID128>(verses.Skip(start).Take(BATCH).Select(v => v.UID));
        foreach (var hit in verseIndex.FindSimilar(default(UID128), vector, batch.Count, null, batch))
        {
            UID128 hitUid = hit.UID;
            if (index.TryGetValue(hitUid, out var i)) { semantic[i] = hit.Score; hasVector[i] = true; }
        }
    }
}

double[] Z(double[] x, bool[] use)
{
    var vals = x.Where((_, i) => use is null || use[i]).ToArray();
    if (vals.Length < 2) return new double[x.Length];
    var mean = vals.Average();
    var sd   = Math.Sqrt(vals.Sum(v => (v - mean) * (v - mean)) / vals.Length);
    return x.Select((v, i) => sd < 1e-9 || (use is not null && !use[i]) ? 0 : (v - mean) / sd).ToArray();
}

var zSem = semanticReady ? Z(semantic, hasVector) : new double[verses.Count];
var zLex = Z(lexical, null);

var closest = Enumerable.Range(0, verses.Count)
    .Select(i => (I: i, Score: W_SEM * zSem[i] + W_LEX * zLex[i]))
    .OrderByDescending(x => x.Score).Take(CLOSEST)
    .Select(x =>
    {
        var v = verses[x.I];
        var key = v.GetString(N.Verse.VerseKey);
        return new QK.HadithVerseRow
        {
            Uid         = v.UID,
            VerseKey    = key,
            Text        = v.GetString(N.Verse.TextUthmani),
            Translation = v.GetString(N.Verse.EnglishSaheeh),
            Score       = Math.Round(x.Score, 2),
            Semantic    = Math.Round(W_SEM * zSem[x.I], 2),
            Lexical     = Math.Round(W_LEX * zLex[x.I], 2),
            Shared      = hadithWords.Where(verseWords[x.I].Contains).OrderBy(w => df[w]).Take(8).ToArray(),
            Quoted      = quotedKeys.Contains(key),
        };
    }).ToArray();

return new QK.HadithProfileDto
{
    Uid                  = uid,
    Key                  = hadith.GetString(N.Hadith.HadithKey),
    Reference            = hadith.GetString(N.Hadith.Reference),
    CollectionUid        = collection?.UID ?? default(UID128),
    CollectionId         = hadith.GetString(N.Hadith.CollectionId),
    CollectionName       = collection?.GetString(N.HadithCollection.CollectionName) ?? "",
    CollectionNameArabic = collection?.GetString(N.HadithCollection.CollectionNameArabic) ?? "",
    BookUid              = book?.UID ?? default(UID128),
    BookNumber           = hadith.GetInt(N.Hadith.BookNumber),
    BookName             = book?.GetString(N.HadithBook.BookName) ?? "",
    BookNameArabic       = book?.GetString(N.HadithBook.BookNameArabic) ?? "",
    NumberInBook         = hadith.GetInt(N.Hadith.NumberInBook),
    TextArabic           = hadith.GetString(N.Hadith.TextArabic),
    Matn                 = hadith.GetString(N.Hadith.Matn),
    TextEnglish          = hadith.GetString(N.Hadith.TextEnglish),
    IsVariant            = hadith.GetBool(N.Hadith.IsVariant),
    TranslatorCites      = hadith.GetString(N.Hadith.TranslatorCites),
    Quotes               = quotes,
    Closest              = closest,
    SemanticReady        = semanticReady,
    Previous             = previous,
    Next                 = next,
};
