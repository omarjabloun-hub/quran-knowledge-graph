using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// The tafsirs, one directory per edition (a work in one language) under <c>data/raw/tafsir/</c>,
/// one file per surah: <c>[{ "surah": "2", "ayah": "255", "text": "…" }, …]</c> (some editions wrap
/// the list as <c>{ "ayahs": [ … ] }</c>). They come from QUL through spa5k/tafsir_api.
///
/// A commentary that explains several verses together is repeated on each of them: consecutive
/// verses of a surah with the same text become ONE entry with a verse range (Ibn Kathir explains
/// up to 20 verses at a time). A verse an edition does not comment on has no entry — al-Jalalayn
/// passes over verses it holds need no explanation.
///
/// Cleaning: the editor's manuscript notes in Ibn Kathir's Arabic (<c>[[في م، أ: …]]</c>) are
/// removed, and so are the printed edition's page numbers in Ibn Ashur's (a line «صفحة ١٥٢»
/// wherever a page turned, often mid-sentence: the two halves of a sentence it cut are joined);
/// runs of blank lines collapse to one paragraph break.
/// </summary>
public static class TafsirSource
{
    /// <summary>
    /// The editions read, in the order a reader is offered them: Ibn Ashur's first — the main
    /// tafsir of this workspace — then the concise ones, then Ibn Kathir.
    /// </summary>
    public static readonly IReadOnlyList<TafsirEdition> Editions =
    [
        new() { Id = "ibn-ashur-ar", Work = "ibn-ashur", Language = "ar", Directory = "ibn-ashur-ar",
                Name = "Ibn Ashur", NameArabic = "التحرير والتنوير",
                Author = "Muhammad al-Tahir ibn Ashur (Tunis, d. 1973)",
                Description = "Al-Tahrir wa al-Tanwir, the tafsir of the Zaytuna's great scholar: the language, rhetoric and order of each passage, the purposes of the law (maqasid), and the views of the commentators before him weighed.",
                Attribution = "Muhammad al-Tahir ibn Ashur, al-Tahrir wa al-Tanwir (al-Dar al-Tunisiya lil-Nashr, 1984); QUL resource 25 (qul.tarteel.ai)" },
        new() { Id = "mukhtasar-ar", Work = "mukhtasar", Language = "ar", Directory = "mukhtasar-ar",
                Name = "Al-Mukhtasar", NameArabic = "المختصر في تفسير القرآن الكريم",
                Author = "Tafsir Center for Quranic Studies",
                Description = "A concise modern tafsir, one short explanation per verse, written by a committee of scholars in Riyadh.",
                Attribution = "Tafsir Center for Quranic Studies; QUL resource 251 (qul.tarteel.ai)" },
        new() { Id = "mukhtasar-en", Work = "mukhtasar", Language = "en", Directory = "mukhtasar-en",
                Name = "Al-Mukhtasar", NameArabic = "المختصر في تفسير القرآن الكريم",
                Author = "Tafsir Center for Quranic Studies",
                Description = "The English translation of al-Mukhtasar, published by its authors.",
                Attribution = "Tafsir Center for Quranic Studies, English translation; QUL resource 266 (qul.tarteel.ai)" },
        new() { Id = "jalalayn-ar", Work = "jalalayn", Language = "ar", Directory = "jalalayn-ar",
                Name = "Al-Jalalayn", NameArabic = "تفسير الجلالين",
                Author = "Jalal al-Din al-Mahalli and Jalal al-Din al-Suyuti",
                Description = "A classical tafsir of the 15th century: the verse's words glossed in place, with grammar and readings.",
                Attribution = "Tafsir al-Jalalayn; QUL resource 523 (qul.tarteel.ai)" },
        new() { Id = "jalalayn-en", Work = "jalalayn", Language = "en", Directory = "jalalayn-en",
                Name = "Al-Jalalayn", NameArabic = "تفسير الجلالين",
                Author = "Jalal al-Din al-Mahalli and Jalal al-Din al-Suyuti, translated by Feras Hamza",
                Description = "Feras Hamza's English translation of al-Jalalayn.",
                Attribution = "Translation by Feras Hamza © Royal Aal al-Bayt Institute for Islamic Thought (altafsir.com); QUL resource 573" },
        new() { Id = "ibn-kathir-ar", Work = "ibn-kathir", Language = "ar", Directory = "ibn-kathir-ar",
                Name = "Ibn Kathir", NameArabic = "تفسير القرآن العظيم لابن كثير",
                Author = "Ismail ibn Kathir (d. 1373)",
                Description = "The classical tafsir by narration: each passage explained through other verses, hadith and the early commentators.",
                Attribution = "Tafsir Ibn Kathir; QUL resource 22 (qul.tarteel.ai)" },
        new() { Id = "ibn-kathir-en", Work = "ibn-kathir", Language = "en", Directory = "ibn-kathir-en",
                Name = "Ibn Kathir", NameArabic = "تفسير القرآن العظيم لابن كثير",
                Author = "Ismail ibn Kathir, abridged in English by Darussalam",
                Description = "The abridged English edition of Ibn Kathir (Darussalam).",
                Attribution = "Tafsir Ibn Kathir (abridged) © Darussalam; QUL resource 35 (qul.tarteel.ai)" },
    ];

    public sealed record Result(IReadOnlyList<TafsirEntry> Entries, IReadOnlyList<VerseRef> Uncommented);

    /// <summary>Reads one edition; verses outside <paramref name="order"/> are a problem, not data.</summary>
    public static Result Read(string rawDir, TafsirEdition edition, IReadOnlyList<VerseRef> order, List<string> problems)
    {
        var dir = Path.Combine(rawDir, "tafsir", edition.Directory);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"Missing tafsir {dir} — run `just fetch`.");

        var known = order.ToHashSet();
        var text  = new Dictionary<VerseRef, string>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var rows = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("ayahs", out var a) ? a : doc.RootElement;

            foreach (var row in rows.EnumerateArray())
            {
                if (!int.TryParse(row.GetProperty("surah").ToString(), out var s) || !int.TryParse(row.GetProperty("ayah").ToString(), out var ayah))
                {
                    problems.Add($"tafsir {edition.Id}: a row in {Path.GetFileName(file)} without a verse number");
                    continue;
                }

                var v = new VerseRef(s, ayah);
                if (!known.Contains(v)) { problems.Add($"tafsir {edition.Id}: {v} does not exist"); continue; }

                var body = Clean(row.TryGetProperty("text", out var t) ? t.GetString() : null);
                if (body.Length > 0) text[v] = body;
            }
        }

        // Consecutive verses of a surah with the same commentary: one entry over the range.
        var entries     = new List<TafsirEntry>();
        var uncommented = new List<VerseRef>();
        TafsirEntry? open = null;

        foreach (var v in order)
        {
            if (!text.TryGetValue(v, out var body)) { uncommented.Add(v); open = null; continue; }

            if (open is not null && open.Last.Surah == v.Surah && open.Last.Ayah == v.Ayah - 1 && open.Text == body)
            {
                open.Last = v;
                continue;
            }

            open = new TafsirEntry { Edition = edition.Id, First = v, Last = v, Text = body };
            entries.Add(open);
        }

        return new Result(entries, uncommented);
    }

    private static readonly Regex EditorNote = new(@"\s*\[\[.*?\]\]", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n\s*\n\s*", RegexOptions.Compiled);
    // A page number of the printed edition, opening a line (twice the line goes on after it). After a sentence's end it is a
    // paragraph break; after anything else the page turned mid-sentence, and the two halves join.
    private static readonly Regex PageTurn = new(@"(?<=(\S))[ \t]*(\n+)[ \t]*صفحة[ \t]*[٠-٩0-9]+[ \t]*(\n|$|(?=\S))[ \t]*", RegexOptions.Compiled);
    private static readonly Regex PageLine = new(@"(?m)^[ \t]*صفحة[ \t]*[٠-٩0-9]+[ \t]*(\n|$|(?=\S))", RegexOptions.Compiled);
    private const string SentenceEnds = ".:؟?!]﴾»)”\"";

    internal static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var t = EditorNote.Replace(text.Replace("\r\n", "\n"), "");
        t = PageTurn.Replace(t, m => SentenceEnds.IndexOf(m.Groups[1].Value[0]) >= 0 ? m.Groups[2].Value : " ");
        t = PageLine.Replace(t, "");
        return BlankLines.Replace(t, "\n\n").Trim();
    }
}
