[endpoint: Curiosity.Endpoints.Path("qkg/verse/hadith")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// The hadith of Bukhari and Muslim on one verse:
//   Quoting  the hadith whose Arabic quotes it or shares a phrase with it (the corpus's detector),
//            then those whose English translator cites it without the Arabic quoting it;
//   Closest  the hadith whose text (Hadith.Matn) is closest in meaning to the verse's Arabic, by the
//            same embedding model — a nearest-neighbour search, quoting hadith and variants (a chain
//            pointing to the hadith before it) left out.
// Candidates for a reader to judge: closeness in meaning is not agreement. Not cached, so a
// re-ingest of the hadith shows at once.

const int MAX_QUOTING = 40;

var req   = Body.FromJson<QK.SimilarRequest>();
var uid   = req?.Uid ?? default(UID128);
var count = req is null || req.Count <= 0 ? 8 : Math.Min(req.Count, 40);
if (uid == default(UID128))                                       return BadRequest("Uid (a Verse) is required");
if (!Graph.TryGet(uid, out var verse) || verse.Type != N.Verse.Type) return NotFound($"Verse '{uid}' not found.");

string Cut(string text, int length) => string.IsNullOrEmpty(text) || text.Length <= length ? text ?? "" : text.Substring(0, length).TrimEnd() + " …";

QK.HadithRow Row(ReadOnlyNode h, string kind, double score) => new QK.HadithRow
{
    Uid          = h.UID,
    Reference    = h.GetString(N.Hadith.Reference),
    CollectionId = h.GetString(N.Hadith.CollectionId),
    BookName     = h.GetString(N.Hadith.BookName),
    Matn         = Cut(h.GetString(N.Hadith.Matn), 420),
    English      = Cut(h.GetString(N.Hadith.TextEnglish), 420),
    Kind         = kind,
    VerseKeys    = Q().StartAt(h.UID).Out(N.QuranQuote.Type, E.HasQuranQuote).AsEnumerable()
                      .Where(q => q.GetString(N.QuranQuote.Kind) != "translator")
                      .Select(q => q.GetString(N.QuranQuote.VerseKey)).ToArray(),
    Score        = Math.Round(score, 3),
};

// ---- quoting -------------------------------------------------------------------------------
var quoting = new List<(ReadOnlyNode Hadith, string Kind, int Words)>();
foreach (var q in Q().StartAt(uid).Out(N.QuranQuote.Type, E.VerseHasQuote).AsEnumerable())
{
    var h = Q().StartAt(q.UID).Out(N.Hadith.Type, E.QuoteInHadith).AsEnumerable().FirstOrDefault();
    if (h is object) quoting.Add((h, q.GetString(N.QuranQuote.Kind), q.GetInt(N.QuranQuote.Words)));
}
int Rank(string kind) => kind == "quotation" ? 0 : kind == "phrase" ? 1 : 2;
var quotingRows = quoting.OrderBy(x => Rank(x.Kind)).ThenByDescending(x => x.Words).ThenBy(x => x.Hadith.GetString(N.Hadith.CollectionId)).ThenBy(x => x.Hadith.GetInt(N.Hadith.Sequence))
                         .Take(MAX_QUOTING).Select(x => Row(x.Hadith, x.Kind, 0)).ToArray();
var quotingUids = new HashSet<UID128>(quoting.Select(x => x.Hadith.UID));

// ---- closest in meaning ------------------------------------------------------------------------
var closest     = new List<QK.HadithRow>();
var verseIndex  = Graph.Embeddings.GetIndex(N.Verse.Type, N.Verse.TextSimple);
var hadithIndex = Graph.Embeddings.GetIndex(N.Hadith.Type, N.Hadith.Matn);
var ready       = false;
if (verseIndex is not null && hadithIndex is not null && verseIndex.TryGetVector(uid, out var vector))
{
    foreach (var hit in hadithIndex.FindSimilar(default(UID128), vector, count * 4 + quotingUids.Count, null, null))
    {
        ready = true;
        UID128 hu = hit.UID;
        if (quotingUids.Contains(hu) || !Graph.TryGet(hu, out var h) || h.Type != N.Hadith.Type || h.GetBool(N.Hadith.IsVariant)) continue;
        closest.Add(Row(h, "", hit.Score));
        if (closest.Count >= count) break;
    }
}

return new QK.VerseHadithDto
{
    Uid           = uid,
    VerseKey      = verse.GetString(N.Verse.VerseKey),
    Quoting       = quotingRows,
    Closest       = closest.ToArray(),
    SemanticReady = ready,
};
