[endpoint: Curiosity.Endpoints.Path("qkg/surah/recitation")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// A surah's recitations: for each reciter, the recording to stream and where each verse starts
// and ends in it, so the reader can follow the verse being recited. The connector times every
// verse of the corpus (Recitation.VerseStartsMs); a verse listed in EstimatedAyahs starts inside a
// verse of the recording — Qalun counts the verses the Madani way — and its time is estimated.
// The recording's OWN verses (Recitation.RecordedStartsMs) are timed exactly: when its reading is
// in the graph (Qalun's RiwayaVerse), they are what the Qalun text follows.
//
// Not cached: it answers in milliseconds, and a re-ingest must show at once.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                         return BadRequest("Uid (a Surah) is required");
if (!Graph.TryGet(uid, out var surah) || surah.Type != N.Surah.Type) return NotFound($"Surah '{uid}' not found.");

var verses = Q().StartAt(uid).Out(N.Verse.Type, E.HasVerse).AsEnumerable()
                .ToDictionary(v => v.GetInt(N.Verse.AyahNumber), v => v.UID);

var riwayaNodes = Q().StartAt(N.Riwaya.Type).AsEnumerable().ToList();

// The ayah of the first Hafs verse a reading's verse is in ("2:255 2:256" → 255).
int HafsAyah(ReadOnlyNode v)
{
    var key   = (v.GetString(N.RiwayaVerse.HafsVerseKeys) ?? "").Split(' ')[0];
    var colon = key.IndexOf(':');
    return colon > 0 && int.TryParse(key.Substring(colon + 1), out var a) ? a : 0;
}

var recitations = Q().StartAt(uid).Out(N.Recitation.Type, E.SurahRecitedIn).AsEnumerable().Select(r =>
{
    var reciter   = Q().StartAt(r.UID).Out(N.Reciter.Type, E.RecitedBy).AsEnumerable().FirstOrDefault();
    var duration  = r.GetInt(N.Recitation.DurationMs) / 1000.0;
    var starts    = (r.GetString(N.Recitation.VerseStartsMs) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture) / 1000.0).ToArray();
    var estimated = new HashSet<int>((r.GetString(N.Recitation.EstimatedAyahs) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture)));

    // The recording's reading in the graph (a Reciter names it as a Riwaya does), and its verses of this surah.
    var riwaya   = riwayaNodes.FirstOrDefault(n => n.GetString(N.Riwaya.RiwayaName) == (reciter?.GetString(N.Reciter.Riwaya) ?? ""));
    var own      = riwaya is null || riwaya.GetBool(N.Riwaya.IsCorpusText) ? new Dictionary<int, ReadOnlyNode>()
                 : Q().StartAt(uid).Out(N.RiwayaVerse.Type, E.SurahHasRiwayaVerse).AsEnumerable()
                      .Where(v => v.GetString(N.RiwayaVerse.RiwayaId) == riwaya.GetString(N.Riwaya.RiwayaId))
                      .ToDictionary(v => v.GetInt(N.RiwayaVerse.AyahNumber), v => v);
    var recorded = (r.GetString(N.Recitation.RecordedStartsMs) ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture) / 1000.0).ToArray();

    return new QK.RecitationDto
    {
        Uid               = r.UID,
        ReciterUid        = reciter?.UID ?? default(UID128),
        ReciterName       = reciter?.GetString(N.Reciter.ReciterName) ?? r.GetString(N.Recitation.ReciterId),
        ReciterNameArabic = reciter?.GetString(N.Reciter.ReciterNameArabic) ?? "",
        Riwaya            = reciter?.GetString(N.Reciter.Riwaya) ?? "",
        RiwayaArabic      = reciter?.GetString(N.Reciter.RiwayaArabic) ?? "",
        Country           = reciter?.GetString(N.Reciter.Country) ?? "",
        Style             = reciter?.GetString(N.Reciter.Style) ?? "",
        Attribution       = reciter?.GetString(N.Reciter.Attribution) ?? "",
        SourceUrl         = reciter?.GetString(N.Reciter.SourceUrl) ?? "",
        AudioUrl          = r.GetString(N.Recitation.AudioUrl),
        Duration          = duration,
        RecordedVerses    = r.GetInt(N.Recitation.RecordedVerses),
        Verses            = starts.Select((start, i) => new QK.RecitedVerseRow
        {
            Uid      = verses.TryGetValue(i + 1, out var v) ? v : default(UID128),
            Ayah     = i + 1,
            HafsAyah = i + 1,
            Start = start,
            End   = i + 1 < starts.Length ? starts[i + 1] : duration,
            Exact = !estimated.Contains(i + 1),
        }).ToArray(),
        RiwayaId          = riwaya?.GetString(N.Riwaya.RiwayaId) ?? "",
        RecordedVerseRows = recorded.Select((start, i) => new QK.RecitedVerseRow
        {
            Uid      = own.TryGetValue(i + 1, out var v) ? v.UID : default(UID128),
            Ayah     = i + 1,
            HafsAyah = own.TryGetValue(i + 1, out var w) ? HafsAyah(w) : 0,
            Start = start,
            End   = i + 1 < recorded.Length ? recorded[i + 1] : duration,
            Exact = true,
        }).ToArray(),
    };
}).OrderBy(r => r.ReciterName, StringComparer.Ordinal).ToArray();

return new QK.SurahRecitationDto
{
    SurahUid    = uid,
    SurahNumber = surah.GetInt(N.Surah.Number),
    Recitations = recitations,
};
