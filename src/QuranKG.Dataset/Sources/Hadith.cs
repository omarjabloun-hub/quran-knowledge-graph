using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// Sahih al-Bukhari and Sahih Muslim, Arabic and English, from fawazahmed0/hadith-api
/// (<c>data/raw/hadith/{ara,eng}-{bukhari,muslim}.json</c>): <c>{ metadata: { sections }, hadiths:
/// [{ hadithnumber, arabicnumber, text, reference: { book, hadith } }] }</c>. The books' Arabic names
/// come from AhmedBaset/hadith-json, whose book ids are the same.
///
/// Cleaning and derived fields:
///   - an entry with no text in either language is skipped (Muslim's introduction, 203 entries);
///   - an entry without its book (306 of Bukhari's) is put in the book its number falls in;
///   - <see cref="Hadith.Matn"/>: the text without its chain of narrators — the Arabic after the
///     last chain marker (حدثنا, أخبرنا, عن …) that comes before the first quotation mark or mention
///     of the Prophet, from the narrator's "said" on. A heuristic, right for most hadith;
///   - <see cref="Hadith.IsVariant"/>: a short text that only refers back ("بهذا الإسناد مثله");
///   - <see cref="Hadith.TranslatorCites"/>: the English translators' bracketed references,
///     "(15.90)" in Bukhari and "(xv. 90)" in Muslim.
/// </summary>
public static class HadithSource
{
    public static readonly IReadOnlyList<HadithCollection> Collections =
    [
        new() { Id = "bukhari", Name = "Sahih al-Bukhari", NameArabic = "صحيح البخاري",
                Compiler = "Muhammad ibn Ismail al-Bukhari (d. 870)", Translator = "Muhammad Muhsin Khan" },
        new() { Id = "muslim",  Name = "Sahih Muslim",     NameArabic = "صحيح مسلم",
                Compiler = "Muslim ibn al-Hajjaj (d. 875)",           Translator = "Abdul Hamid Siddiqui" },
    ];

    public sealed record Result(IReadOnlyList<HadithBook> Books, IReadOnlyList<Hadith> Hadiths, int Skipped);

    public static Result Read(string hadithDir, HadithCollection collection, Func<VerseRef, bool> verseExists)
    {
        var arabic  = Load(Path.Combine(hadithDir, $"ara-{collection.Id}.json"));
        var english = Load(Path.Combine(hadithDir, $"eng-{collection.Id}.json"));
        var arabicNames = ArabicBookNames(Path.Combine(hadithDir, $"hadith-json-{collection.Id}.json"));

        var books = arabic.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Value) || arabicNames.ContainsKey(s.Key))
            .OrderBy(s => s.Key)
            .Select(s => new HadithBook
            {
                Collection = collection.Id,
                Number     = s.Key,
                Name       = (s.Value ?? "").Trim(),
                NameArabic = arabicNames.GetValueOrDefault(s.Key, ""),
            }).ToList();

        var englishByNumber = english.Rows.ToDictionary(r => r.Number, r => r.Text);
        var hadiths = new List<Hadith>();
        var skipped = 0;

        foreach (var row in arabic.Rows.OrderBy(r => r.Number))
        {
            var en = englishByNumber.GetValueOrDefault(row.Number, "");
            if (string.IsNullOrWhiteSpace(row.Text) && string.IsNullOrWhiteSpace(en)) { skipped++; continue; }

            var (matn, variant) = Matn(row.Text);
            var number = row.Number.ToString(CultureInfo.InvariantCulture);
            hadiths.Add(new Hadith
            {
                Collection      = collection.Id,
                Number          = number,
                Sequence        = hadiths.Count + 1,
                Reference       = (collection.Id == "bukhari" ? "Bukhari " : "Muslim ") + Cited(row.ArabicNumber ?? row.Number, collection.Id),
                Book            = row.Book,
                NumberInBook    = row.InBook,
                TextArabic      = row.Text.Trim(),
                Matn            = matn,
                IsVariant       = variant,
                TextEnglish     = en.Trim(),
                TranslatorCites = TranslatorCites(en).Where(verseExists).Distinct().ToList(),
            });
        }

        return new Result(books, hadiths, skipped);
    }

    /// <summary>How a hadith is cited: Bukhari's number as is; Muslim's Abdul Baqi number with its letter, 8.01 → "8a".</summary>
    private static string Cited(double number, string collection)
    {
        var whole = Math.Floor(number);
        var part  = (int)Math.Round((number - whole) * 100);
        if (part == 0) return ((int)whole).ToString(CultureInfo.InvariantCulture);
        if (collection == "bukhari") return number.ToString(CultureInfo.InvariantCulture);
        return ((int)whole).ToString(CultureInfo.InvariantCulture) + (part <= 26 ? ((char)('a' + part - 1)).ToString() : "." + part);
    }

    // ---- the text without its chain ----------------------------------------------------------

    private static readonly Regex Noise   = new("[\\u0610-\\u061A\\u064B-\\u065F\\u0670\\u06D6-\\u06ED\\u0640\\u200C-\\u200F]", RegexOptions.Compiled);
    private static readonly Regex Spaces  = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Honour  = new(@"\s*(رضى|رضي) الله (عنه|عنهما|عنها|عنهم)\s*", RegexOptions.Compiled);
    private static readonly Regex StopAt  = new("\"|رسول الله|النبي", RegexOptions.Compiled);
    private static readonly Regex Chain   = new(@"(?:^|\s)و?(?:حدثنا|حدثني|حدثناه|حدثنيه|أخبرنا|أخبرني|أخبرناه|أنبأنا|سمعت|عن)\s", RegexOptions.Compiled);
    private static readonly Regex Speech  = new(@"\s(قال|قالت|قالا|أن|أنه|أنها|أنهم|يقول|تقول|يحدث|كان|كانت)\s", RegexOptions.Compiled);
    private static readonly Regex Refers  = new(@"(?:^|\s)(بهذا الإسناد|بإسناد|بمثله|مثله|بنحوه|نحوه|بمعناه|بنحو حديث|بمثل حديث|نحو حديث|في هذا الإسناد)(?:\s|\.|،|$)", RegexOptions.Compiled);

    internal static (string Matn, bool Variant) Matn(string arabic)
    {
        var plain = Honour.Replace(Spaces.Replace(Noise.Replace(arabic ?? "", ""), " ").Trim(), " ");
        var stop  = StopAt.Match(plain);
        var limit = stop.Success ? stop.Index : plain.Length;

        var last = Chain.Matches(plain).Where(m => m.Index < limit).LastOrDefault();
        if (last is null) return (plain, false);

        var rest   = plain.Substring(last.Index + last.Length);
        var speech = Speech.Match(" " + rest);
        var body   = (speech.Success && speech.Index < 60 ? rest[Math.Max(0, speech.Index + speech.Length - 1)..] : rest).Trim();

        return (body, body.Length < 160 && Refers.IsMatch(body));
    }

    // ---- the translators' citations ----------------------------------------------------------

    private static readonly Regex ArabicNumeralCite = new(@"\((\d{1,3})\.(\d{1,3})(?:\s*-\s*(\d{1,3}))?\)", RegexOptions.Compiled);
    private static readonly Regex RomanCite         = new(@"\(([ivxlc]+)\.\s*(\d{1,3})(?:\s*-\s*(\d{1,3}))?", RegexOptions.Compiled);

    private static IEnumerable<VerseRef> TranslatorCites(string english)
    {
        foreach (Match m in ArabicNumeralCite.Matches(english ?? ""))
            foreach (var v in Range(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value, m.Groups[3].Value)) yield return v;
        foreach (Match m in RomanCite.Matches(english ?? ""))
            foreach (var v in Range(Roman(m.Groups[1].Value), m.Groups[2].Value, m.Groups[3].Value)) yield return v;
    }

    private static IEnumerable<VerseRef> Range(int surah, string from, string to)
    {
        var a = int.Parse(from, CultureInfo.InvariantCulture);
        var b = string.IsNullOrEmpty(to) ? a : int.Parse(to, CultureInfo.InvariantCulture);
        if (surah < 1 || surah > 114 || b < a) yield break;
        for (var x = a; x <= Math.Min(b, a + 30); x++) yield return new VerseRef(surah, x);
    }

    private static int Roman(string s)
    {
        int Value(char c) => c switch { 'i' => 1, 'v' => 5, 'x' => 10, 'l' => 50, 'c' => 100, _ => 0 };
        var total = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var v = Value(s[i]);
            total += i + 1 < s.Length && Value(s[i + 1]) > v ? -v : v;
        }
        return total;
    }

    // ---- reading the files ------------------------------------------------------------------

    private sealed record Row(double Number, double? ArabicNumber, string Text, int Book, int InBook);
    private sealed record Edition(IReadOnlyList<Row> Rows, IReadOnlyDictionary<int, string> Sections, IReadOnlyList<(int Book, double First)> Starts);

    private static Edition Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing hadith file {path} — run `just fetch`.", path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var sections = new Dictionary<int, string>();
        foreach (var s in doc.RootElement.GetProperty("metadata").GetProperty("sections").EnumerateObject())
            if (int.TryParse(s.Name, out var n)) sections[n] = s.Value.GetString() ?? "";

        // Each book's first hadith number: 306 of Bukhari's hadith come without their book, and are
        // put in the book whose range they fall in (or the last one to start before them).
        var starts = new List<(int Book, double First)>();
        if (doc.RootElement.GetProperty("metadata").TryGetProperty("section_details", out var details))
            foreach (var d in details.EnumerateObject())
                if (int.TryParse(d.Name, out var n) && n > 0 && d.Value.TryGetProperty("hadithnumber_first", out var f) && f.ValueKind == JsonValueKind.Number)
                    starts.Add((n, f.GetDouble()));
        starts.Sort((a, b) => a.First.CompareTo(b.First));

        var rows = new List<Row>();
        foreach (var h in doc.RootElement.GetProperty("hadiths").EnumerateArray())
        {
            var reference = h.GetProperty("reference");
            rows.Add(new Row(
                h.GetProperty("hadithnumber").GetDouble(),
                ArabicNumber(h),
                h.GetProperty("text").GetString() ?? "",
                reference.ValueKind == JsonValueKind.Object && reference.TryGetProperty("book", out var b) ? b.GetInt32() : 0,
                reference.ValueKind == JsonValueKind.Object && reference.TryGetProperty("hadith", out var i) ? i.GetInt32() : 0));
        }

        rows = rows.Select(r => r.Book != 0 || !string.IsNullOrWhiteSpace(sections.GetValueOrDefault(0)) || starts.Count == 0
                                    ? r
                                    : r with { Book = starts.LastOrDefault(st => st.First <= r.Number).Book }).ToList();

        return new Edition(rows, sections, starts);
    }

    /// <summary>Muslim's Abdul Baqi number, a string ("8.01") in the source, a number in Bukhari's.</summary>
    private static double? ArabicNumber(JsonElement h)
    {
        if (!h.TryGetProperty("arabicnumber", out var an)) return null;
        if (an.ValueKind == JsonValueKind.Number) return an.GetDouble();
        return an.ValueKind == JsonValueKind.String && double.TryParse(an.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static Dictionary<int, string> ArabicBookNames(string path)
    {
        var names = new Dictionary<int, string>();
        if (!File.Exists(path)) return names;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var c in doc.RootElement.GetProperty("chapters").EnumerateArray())
            names[c.GetProperty("id").GetInt32()] = (c.GetProperty("arabic").GetString() ?? "").Trim();
        return names;
    }
}
