using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using QuranKG.Dataset.Text;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// The Kaggle dataset: one CSV per surah. What it contributes here is the surah introduction,
/// the scholarly surah names and the verse transliteration.
///
/// Two traps, both handled here:
/// <list type="bullet">
/// <item>Rows are sorted by their <c>id</c> <b>as text</b> ("1.2", "10.2", "100.2", "101.2" …), so
/// the row number is not the ayah. The id is "ayah.surah"; its integer part is the ayah.
/// (As a float it is lossy too: "1.10" — ayah 1 of surah 10 — reads as 1.1.)</item>
/// <item>The English is Qara'i's translation with the footnote markers and texts run into the
/// verse. The clean copy of the same translation comes from Tanzil instead.</item>
/// </list>
/// </summary>
public static class KaggleDataset
{
    public sealed class SurahFile
    {
        public required int                          Number          { get; init; }
        public required string                       NameScholarly   { get; init; }
        public required string                       NameArabic      { get; init; }
        public required string                       Meaning         { get; init; }
        public required string                       Type            { get; init; }
        public required int                          TotalVerses     { get; init; }
        public required string                       Description     { get; init; }
        public required IReadOnlyDictionary<int, string> Transliteration { get; init; }
        /// <summary>The raw Kaggle English per ayah (annotated, footnotes inline) — kept for reference only.</summary>
        public required IReadOnlyDictionary<int, string> EnglishAnnotated { get; init; }
    }

    private sealed class Row
    {
        [Name("id")]            public int    Id          { get; set; }
        [Name("surah_name")]    public string SurahName   { get; set; } = "";
        [Name("surah_name_ar")] public string SurahNameAr { get; set; } = "";
        [Name("translation")]   public string Translation { get; set; } = "";
        [Name("type")]          public string Type        { get; set; } = "";
        [Name("total_verses")]  public int    TotalVerses { get; set; }
        [Name("description")]   public string Description { get; set; } = "";
        [Name("verses")]        public string Verses      { get; set; } = "";
    }

    public static IReadOnlyList<SurahFile> Read(string csvDir)
    {
        if (!Directory.Exists(csvDir)) throw new DirectoryNotFoundException($"Missing {csvDir} — run `just fetch`.");

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord   = true,
            MissingFieldFound = null,
            HeaderValidated   = null,
        };

        var result = new List<SurahFile>();
        foreach (var path in Directory.EnumerateFiles(csvDir, "*.csv"))
        {
            using var reader = new StreamReader(path);
            using var csv    = new CsvReader(reader, config);
            var rows = csv.GetRecords<Row>().ToList();
            if (rows.Count == 0) continue;

            var translit = new Dictionary<int, string>();
            var english  = new Dictionary<int, string>();
            foreach (var row in rows)
            {
                var verse = PythonLiteral.ParseDict(row.Verses);
                var ayah  = AyahFromId(verse.GetValueOrDefault("id") ?? throw new FormatException($"{Path.GetFileName(path)}: verse without id"));
                if (!translit.TryAdd(ayah, verse.GetValueOrDefault("transliteration") ?? ""))
                    throw new FormatException($"{Path.GetFileName(path)}: ayah {ayah} appears twice");
                english[ayah] = verse.GetValueOrDefault("translation_eng") ?? "";
            }

            var first = rows[0];
            result.Add(new SurahFile
            {
                Number           = first.Id,
                NameScholarly    = TitleCase(first.SurahName),
                NameArabic       = first.SurahNameAr.Trim(),
                Meaning          = TitleCase(first.Translation),
                Type             = first.Type.Trim(),
                TotalVerses      = first.TotalVerses,
                Description      = first.Description.Trim(),
                Transliteration  = translit,
                EnglishAnnotated = english,
            });
        }

        return result.OrderBy(s => s.Number).ToList();
    }

    /// <summary>"ayah.surah" → ayah. The text before the dot, never a float conversion.</summary>
    public static int AyahFromId(string id)
    {
        var dot = id.IndexOf('.');
        return int.Parse(dot < 0 ? id : id[..dot], CultureInfo.InvariantCulture);
    }

    /// <summary>"AL-FĀTIḤAH" → "Al-Fātiḥah", "ĀL ʿIMRĀN" → "Āl ʿImrān": the first cased letter of each part is upper case.</summary>
    public static string TitleCase(string value)
    {
        var chars = value.Trim().ToLowerInvariant().ToCharArray();
        var start = true;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is ' ' or '-') { start = true; continue; }
            if (start && char.IsLower(c)) { chars[i] = char.ToUpperInvariant(c); start = false; }
            else if (char.IsLetterOrDigit(c) && (char.IsUpper(c) || char.IsLower(c) || char.IsDigit(c))) start = false;
        }
        return new string(chars);
    }
}
