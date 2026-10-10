[endpoint: Curiosity.Endpoints.Path("qkg/verse/words")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// What the verse page needs beside the verse itself: its words in reading order with their
// morphology (segments, root, lemma, part of speech — each root and lemma with its UID, so the
// word-by-word view opens them), its roots and lemmas, and the verses Ibn Kathir's tafsir relates
// to it (QurSim).
//
// QurSim is the ANSWER KEY for the similarity work. People may read it here; no similarity
// signal and no AI tool may call this endpoint.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                       return BadRequest("Uid (a Verse) is required");
if (!Graph.TryGet(uid, out var verse) || verse.Type != N.Verse.Type) return NotFound($"Verse '{uid}' not found.");

UID128 FirstNeighbour(UID128 from, string type, string edge) => Q().StartAt(from).Out(type, edge).AsUIDEnumerable().FirstOrDefault();

var words = Q().StartAt(uid).Out(N.Word.Type, E.HasWord).AsEnumerable()
               .OrderBy(w => w.GetInt(N.Word.Position))
               .Select(w => new QK.WordRow
               {
                   Uid          = w.UID,
                   Position     = w.GetInt(N.Word.Position),
                   Text         = w.GetString(N.Word.TextArabic),
                   Segments     = w.GetString(N.Word.Segments),
                   Root         = QK.Spaced(w.GetString(N.Word.RootLetters)),
                   RootUid      = FirstNeighbour(w.UID, N.Root.Type,  E.HasRoot),
                   Lemma        = w.GetString(N.Word.LemmaText),
                   LemmaUid     = FirstNeighbour(w.UID, N.Lemma.Type, E.HasLemma),
                   PartOfSpeech = w.GetString(N.Word.PartOfSpeech),
                   Features     = w.GetString(N.Word.Features),
               })
               .ToArray();

var related = new List<(QK.RelatedVerse Verse, int MushafIndex)>();
foreach (var pair in Q().StartAt(uid).Out(N.QurSimPair.Type, E.InQurSimPair).AsEnumerable())
{
    var degree = pair.GetInt(N.QurSimPair.Degree);
    foreach (var other in Q().StartAt(pair.UID).Out(N.Verse.Type, E.QurSimPairVerse).AsEnumerable())
    {
        if (other.UID == uid) continue;
        related.Add((new QK.RelatedVerse
        {
            Uid         = other.UID,
            VerseKey    = other.GetString(N.Verse.VerseKey),
            Degree      = degree,
            Grade       = degree == 2 ? "strongly related" : degree == 1 ? "related" : "relation not obvious",
            Text        = other.GetString(N.Verse.TextUthmani),
            Translation = other.GetString(N.Verse.EnglishSaheeh),
        }, other.GetInt(N.Verse.MushafIndex)));
    }
}

return new QK.VerseWordsDto
{
    Uid           = uid,
    SurahUID      = FirstNeighbour(uid, N.Surah.Type, E.InSurah),
    Words         = words,
    RootUIDs      = Q().StartAt(uid).Out(N.Root.Type, E.VerseHasRoot).AsUIDEnumerable().ToArray(),
    LemmaUIDs     = Q().StartAt(uid).Out(N.Lemma.Type, E.VerseHasLemma).AsUIDEnumerable().ToArray(),
    TafsirRelated = related.OrderByDescending(r => r.Verse.Degree).ThenBy(r => r.MushafIndex).Select(r => r.Verse).ToArray(),
};
