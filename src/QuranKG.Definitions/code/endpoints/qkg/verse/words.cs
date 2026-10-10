[endpoint: Curiosity.Endpoints.Path("qkg/verse/words")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// What the verse page needs beside the verse itself: its words in reading order (each renders
// with its own Word renderer — root, lemma, part of speech), its roots and lemmas, and the verses
// Ibn Kathir's tafsir relates to it (QurSim).
//
// QurSim is the ANSWER KEY for the similarity work. People may read it here; no similarity
// signal and no AI tool may call this endpoint.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                       return BadRequest("Uid (a Verse) is required");
if (!Graph.TryGet(uid, out var verse) || verse.Type != N.Verse.Type) return NotFound($"Verse '{uid}' not found.");

var related = new List<QK.Counted>();
foreach (var pair in Q().StartAt(uid).Out(N.QurSimPair.Type, E.InQurSimPair).AsEnumerable())
{
    var degree = pair.GetInt(N.QurSimPair.Degree);
    foreach (var other in Q().StartAt(pair.UID).Out(N.Verse.Type, E.QurSimPairVerse).AsEnumerable())
    {
        if (other.UID == uid) continue;
        related.Add(new QK.Counted
        {
            Uid    = other.UID,
            Label  = other.GetString(N.Verse.VerseKey),
            Detail = degree == 2 ? "strongly related" : degree == 1 ? "related" : "relation not obvious",
            Count  = degree,
        });
    }
}

return new QK.VerseWordsDto
{
    Uid           = uid,
    SurahUID      = Q().StartAt(uid).Out(N.Surah.Type, E.InSurah).AsUIDEnumerable().FirstOrDefault(),
    WordUIDs      = Q().StartAt(uid).Out(N.Word.Type, E.HasWord).AsEnumerable().OrderBy(w => w.GetInt(N.Word.Position)).Select(w => w.UID).ToArray(),
    RootUIDs      = Q().StartAt(uid).Out(N.Root.Type, E.VerseHasRoot).AsUIDEnumerable().ToArray(),
    LemmaUIDs     = Q().StartAt(uid).Out(N.Lemma.Type, E.VerseHasLemma).AsUIDEnumerable().ToArray(),
    TafsirRelated = related.OrderByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.Ordinal).ToArray(),
};
