[endpoint: Curiosity.Endpoints.Path("qkg/verse/tafsir")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// What the tafsirs say on one verse: every work (Ibn Ashur, al-Mukhtasar, al-Jalalayn, Ibn Kathir) with the
// range of verses each of its editions explains together with this one, and the requested work's
// text in Arabic and in English (Ibn Ashur's is in Arabic only). Only one work's text: Ibn Kathir's
// and Ibn Ashur's run to 60,000 characters and more, and a reader asks for one when they choose it.
// Ibn Ashur's al-Tahrir wa al-Tanwir is the main tafsir: the one read when none is asked for.
//
// FOR PEOPLE ONLY. No AI tool may call this endpoint, and Tafsir / TafsirEntry have no consult
// reader: Ibn Kathir's tafsir is the text QurSim — the similarity answer key — was drawn from.

const string DEFAULT_WORK = "ibn-ashur";

var req  = Body.FromJson<QK.VerseTafsirRequest>();
var uid  = req?.Uid ?? default(UID128);
var work = string.IsNullOrWhiteSpace(req?.Work) ? DEFAULT_WORK : req.Work.Trim();

if (uid == default(UID128))                                       return BadRequest("Uid (a Verse) is required");
if (!Graph.TryGet(uid, out var verse) || verse.Type != N.Verse.Type) return NotFound($"Verse '{uid}' not found.");

var editions = Q().StartAt(N.Tafsir.Type).AsEnumerable().OrderBy(t => t.GetInt(N.Tafsir.Rank)).ToList();
var entries  = Q().StartAt(uid).Out(N.TafsirEntry.Type, E.HasTafsirEntry).AsEnumerable()
                  .ToDictionary(e => e.GetString(N.TafsirEntry.TafsirId), e => e);

QK.TafsirText Text(ReadOnlyNode edition)
{
    if (edition is null || !entries.TryGetValue(edition.GetString(N.Tafsir.TafsirId), out var e)) return null;
    return new QK.TafsirText
    {
        Uid           = e.UID,
        TafsirUid     = edition.UID,
        Language      = e.GetString(N.TafsirEntry.Language),
        FirstVerseKey = e.GetString(N.TafsirEntry.FirstVerseKey),
        LastVerseKey  = e.GetString(N.TafsirEntry.LastVerseKey),
        FirstAyah     = e.GetInt(N.TafsirEntry.FirstAyah),
        LastAyah      = e.GetInt(N.TafsirEntry.LastAyah),
        Text          = edition.GetString(N.Tafsir.Work) == work ? e.GetString(N.TafsirEntry.Text) : "",
        Attribution   = edition.GetString(N.Tafsir.Attribution),
    };
}

var works = editions.GroupBy(t => t.GetString(N.Tafsir.Work)).Select(g =>
{
    var first = g.First();
    return new QK.TafsirWork
    {
        Work        = g.Key,
        Name        = first.GetString(N.Tafsir.TafsirName),
        NameArabic  = first.GetString(N.Tafsir.TafsirNameArabic),
        Author      = (g.FirstOrDefault(t => t.GetString(N.Tafsir.Language) == "ar") ?? first).GetString(N.Tafsir.Author),
        Description = (g.FirstOrDefault(t => t.GetString(N.Tafsir.Language) == "ar") ?? first).GetString(N.Tafsir.Description),
        Arabic      = Text(g.FirstOrDefault(t => t.GetString(N.Tafsir.Language) == "ar")),
        English     = Text(g.FirstOrDefault(t => t.GetString(N.Tafsir.Language) == "en")),
    };
}).ToArray();

return new QK.VerseTafsirDto
{
    Uid      = uid,
    VerseKey = verse.GetString(N.Verse.VerseKey),
    Works    = works,
    Work     = works.Any(w => w.Work == work) ? work : "",
};
