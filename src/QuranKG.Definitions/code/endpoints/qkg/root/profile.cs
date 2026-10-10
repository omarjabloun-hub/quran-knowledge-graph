[endpoint: Curiosity.Endpoints.Path("qkg/root/profile")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]
[endpoint: Curiosity.Endpoints.CacheFor(3600)]

//ImportEndpoint("qkg/_lib/dto")

// One root, everywhere it occurs: how often, in how many verses and surahs, its spread over the
// 114 surahs and between Meccan and Medinan, the lemmas derived from it and the surface forms it
// takes. Only the root's own words are read (Root -> Word), never the whole corpus.

var req = Body.FromJson<QK.UidRequest>();
var uid = req?.Uid ?? default(UID128);

if (uid == default(UID128))                                    return BadRequest("Uid (a Root) is required");
if (!Graph.TryGet(uid, out var root) || root.Type != N.Root.Type) return NotFound($"Root '{uid}' not found.");

var surahs  = Q().StartAt(N.Surah.Type).AsEnumerable().ToDictionary(n => n.GetInt(N.Surah.Number), n => n);
var lemmas  = Q().StartAt(uid).Out(N.Lemma.Type, E.RootOfLemma).AsEnumerable().ToDictionary(n => n.GetString(N.Lemma.LemmaKey), n => n);

var perSurah   = new int[114];
var meccan     = 0;
var medinan    = 0;
var verseKeys  = new HashSet<string>(StringComparer.Ordinal);
var lemmaCount = new Dictionary<string, int>(StringComparer.Ordinal);
var formCount  = new Dictionary<string, int>(StringComparer.Ordinal);

var words = Q().StartAt(uid).Out(N.Word.Type, E.RootOfWord).AsEnumerable().ToList();
foreach (var w in words)
{
    var s = w.GetInt(N.Word.SurahNumber);
    perSurah[s - 1]++;
    if (surahs[s].GetString(N.Surah.RevelationPlace) == QK.MECCAN) meccan++; else medinan++;
    verseKeys.Add(w.GetString(N.Word.VerseKey));
    QK.Bump(lemmaCount, QK.LemmaKey(w.GetString(N.Word.LemmaText), w.GetString(N.Word.RootLetters), w.GetString(N.Word.PosTag)));
    QK.Bump(formCount, w.GetString(N.Word.TextPlain));
}

return new QK.RootProfileDto
{
    Uid         = uid,
    Letters     = root.GetString(N.Root.RootLetters),
    Occurrences = words.Count,
    Verses      = verseKeys.Count,
    Surahs      = perSurah.Count(c => c > 0),
    Meccan      = meccan,
    Medinan     = medinan,
    PerSurah    = perSurah,
    SurahUIDs   = Enumerable.Range(1, 114).Select(s => surahs[s].UID).ToArray(),
    Lemmas      = lemmaCount.OrderByDescending(kv => kv.Value).Select(kv => new QK.Counted
                  {
                      Uid    = lemmas.TryGetValue(kv.Key, out var l) ? l.UID : default(UID128),
                      Label  = kv.Key.Split('|')[0],
                      Detail = lemmas.TryGetValue(kv.Key, out var l2) ? l2.GetString(N.Lemma.PartOfSpeech) : "",
                      Count  = kv.Value,
                  }).ToArray(),
    Forms       = formCount.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(40)
                           .Select(kv => new QK.Counted { Label = kv.Key, Count = kv.Value }).ToArray(),
    VerseUIDs   = Q().StartAt(uid).Out(N.Verse.Type, E.RootInVerse).AsEnumerable()
                     .OrderBy(v => v.GetInt(N.Verse.MushafIndex)).Select(v => v.UID).ToArray(),
};
