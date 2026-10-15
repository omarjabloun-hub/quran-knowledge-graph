[endpoint: Curiosity.Endpoints.Path("qkg/search/quick")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("qkg/_lib/dto")

// The home page's search, answered as the reader types. What the query names comes first — a
// verse ("2:255", "2 255"), a surah (its number, its name in any of its spellings, in Arabic), a
// page of the mushaf ("page 50", "صفحة 50"), a juz ("juz 30", "جزء 30"), a hadith ("Bukhari
// 6018") — then the verses whose words match: in the Arabic, every word of the query (diacritics
// and spelling ignored, the last word as a prefix, since it is still being typed), else in the
// English gloss. Verses where the query's words stand together come first, then mushaf order.
//
// Not cached: every keystroke is a different query.

const int DEFAULT_TAKE = 8, MAX_TAKE = 50;

var req  = Body.FromJson<QK.QuickSearchRequest>();
var text = (req?.Text ?? "").Trim();
var take = req is object && req.Take > 0 ? Math.Min(req.Take, MAX_TAKE) : DEFAULT_TAKE;

if (text.Length == 0) return new QK.QuickSearchDto { Targets = new QK.QuickTarget[0], Verses = new QK.QuickVerse[0] };

var targets = new List<QK.QuickTarget>();
var surahs  = Q().StartAt(N.Surah.Type).AsEnumerable().OrderBy(s => s.GetInt(N.Surah.Number)).ToList();
var byNumber = surahs.ToDictionary(s => s.GetInt(N.Surah.Number), s => s);

QK.QuickTarget VerseTarget(ReadOnlyNode v, string kind, string label)
{
    var surah = v.GetInt(N.Verse.SurahNumber);
    return new QK.QuickTarget
    {
        Kind        = kind,
        Uid         = v.UID,
        Label       = label,
        LabelArabic = byNumber.TryGetValue(surah, out var s) ? s.GetString(N.Surah.NameArabic) : "",
        Detail      = v.GetString(N.Verse.VerseKey) + " · " + (byNumber.TryGetValue(surah, out var s2) ? s2.GetString(N.Surah.NameScholarly) : "") + " · " + string.Format("page {0}, juz {1}", v.GetInt(N.Verse.Page), v.GetInt(N.Verse.Juz)),
        SurahNumber = surah,
        Ayah        = v.GetInt(N.Verse.AyahNumber),
    };
}

// ---- what the query names -------------------------------------------------------------------

var lower = text.ToLowerInvariant();
var key   = System.Text.RegularExpressions.Regex.Match(lower, @"^(\d{1,3})\s*[:.\s]\s*(\d{1,3})$");
if (key.Success && Graph.TryGet(N.Verse.Type, key.Groups[1].Value + ":" + key.Groups[2].Value, out var verse))
    targets.Add(VerseTarget(verse, "verse", "Verse " + verse.GetString(N.Verse.VerseKey)));

var place = System.Text.RegularExpressions.Regex.Match(lower, @"^(page|p\.?|صفحة|صفحه|juz|juz'|jozz|para|جزء|الجزء)\s*(\d{1,3})$");
if (place.Success && int.TryParse(place.Groups[2].Value, out var n))
{
    var isPage = place.Groups[1].Value.StartsWith("p") || place.Groups[1].Value.StartsWith("ص");
    if (isPage ? n >= 1 && n <= 604 : n >= 1 && n <= 30)
    {
        var first = Q().StartAt(N.Verse.Type).AsEnumerable()
                       .Where(v => (isPage ? v.GetInt(N.Verse.Page) : v.GetInt(N.Verse.Juz)) == n)
                       .OrderBy(v => v.GetInt(N.Verse.MushafIndex)).FirstOrDefault();
        if (first is object) targets.Add(VerseTarget(first, isPage ? "page" : "juz", (isPage ? "Page " : "Juz ") + n));
    }
}

// A surah by its number, or by its name in any spelling: "Al-Baqarah", "baqara", "The Cow", "البقرة".
string Plain(string s) => new string((s ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
                                       .Where(c => char.IsLetterOrDigit(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
string Bare(string s) { var p = Plain(s); foreach (var prefix in new[] { "surat", "surah", "sura", "al", "an", "ar", "as", "at", "ad", "adh", "az", "ash", "the" }) if (p.StartsWith(prefix) && p.Length > prefix.Length + 2) { p = p.Substring(prefix.Length); break; } return p; }
var arabicQuery = string.Join(" ", QK.MatchTokens(text));
var asSurah     = Bare(text);

foreach (var s in surahs)
{
    var number = s.GetInt(N.Surah.Number);
    var names  = new[] { s.GetString(N.Surah.NameScholarly), s.GetString(N.Surah.NameLatin), s.GetString(N.Surah.NameEnglish) };
    var arabicName = string.Join(" ", QK.MatchTokens(s.GetString(N.Surah.NameArabic)));
    var match = lower == number.ToString()
             || (asSurah.Length >= 3 && names.Any(x => Bare(x).StartsWith(asSurah) || (asSurah.Length >= 4 && Bare(x).Contains(asSurah))))
             || (arabicQuery.Length >= 2 && (arabicName == arabicQuery || arabicName.Replace("ال", "").StartsWith(arabicQuery.Replace("سوره ", "").Replace("ال", ""))));
    if (!match) continue;
    targets.Add(new QK.QuickTarget
    {
        Kind        = "surah",
        Uid         = s.UID,
        Label       = number + ". " + s.GetString(N.Surah.NameScholarly),
        LabelArabic = s.GetString(N.Surah.NameArabic),
        Detail      = s.GetString(N.Surah.NameEnglish) + " · " + s.GetInt(N.Surah.AyahCount) + " verses · " + s.GetString(N.Surah.RevelationPlace),
        SurahNumber = number,
    });
    if (targets.Count(t => t.Kind == "surah") >= 5) break;
}

var reference = QK.ParseReference(text);
if (reference.Collection is object)
{
    var hadith = Q().StartAt(N.Hadith.Type).AsEnumerable()
                    .FirstOrDefault(h => h.GetString(N.Hadith.CollectionId) == reference.Collection
                                      && (h.GetString(N.Hadith.Reference) ?? "").EndsWith(" " + reference.Number, StringComparison.OrdinalIgnoreCase));
    if (hadith is object)
        targets.Add(new QK.QuickTarget
        {
            Kind   = "hadith",
            Uid    = hadith.UID,
            Label  = hadith.GetString(N.Hadith.Reference),
            Detail = hadith.GetString(N.Hadith.BookName),
            LabelArabic = (hadith.GetString(N.Hadith.Matn) ?? "").Length > 90 ? hadith.GetString(N.Hadith.Matn).Substring(0, 90) + "…" : hadith.GetString(N.Hadith.Matn),
        });
}

// ---- the verses whose words match -----------------------------------------------------------

var words  = arabicQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
var arabic = words.Length > 0;
var englishWords = lower.Split(new[] { ' ', ',', '.', ';', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).ToArray();
var lookForWords = !key.Success && !place.Success && reference.Collection is null && (arabic ? arabicQuery.Length >= 2 : englishWords.Length > 0 && lower.Length >= 3);

var found = new List<(ReadOnlyNode Verse, bool Together, string Matched)>();
if (lookForWords)
{
    foreach (var v in Q().StartAt(N.Verse.Type).AsEnumerable())
    {
        if (arabic)
        {
            var tokens = QK.MatchTokens(v.GetString(N.Verse.TextSimple));
            var ok = true;
            for (var i = 0; i < words.Length && ok; i++)
                ok = i == words.Length - 1 ? tokens.Any(t => t.StartsWith(words[i]) || (words[i].Length >= 3 && t.Contains(words[i]))) : tokens.Contains(words[i]) || tokens.Any(t => t.Length > words[i].Length && t.EndsWith(words[i]) && t.Length - words[i].Length <= 3);
            if (!ok) continue;
            found.Add((v, (" " + string.Join(" ", tokens) + " ").Contains(" " + arabicQuery), "arabic"));
        }
        else
        {
            var gloss = (v.GetString(N.Verse.EnglishSaheeh) ?? "").ToLowerInvariant();
            if (!englishWords.All(w => gloss.Contains(w))) continue;
            found.Add((v, gloss.Contains(lower), "english"));
        }
    }
}

var verses = found.OrderByDescending(f => f.Together).ThenBy(f => f.Verse.GetInt(N.Verse.MushafIndex)).Take(take).Select(f =>
{
    var surah = f.Verse.GetInt(N.Verse.SurahNumber);
    return new QK.QuickVerse
    {
        Uid         = f.Verse.UID,
        VerseKey    = f.Verse.GetString(N.Verse.VerseKey),
        SurahNumber = surah,
        Ayah        = f.Verse.GetInt(N.Verse.AyahNumber),
        SurahName   = byNumber.TryGetValue(surah, out var s) ? s.GetString(N.Surah.NameScholarly) : "",
        Text        = f.Verse.GetString(N.Verse.TextUthmani),
        Translation = f.Verse.GetString(N.Verse.EnglishSaheeh),
        Matched     = f.Matched,
    };
}).ToArray();

return new QK.QuickSearchDto { Targets = targets.ToArray(), Verses = verses, Total = found.Count };
