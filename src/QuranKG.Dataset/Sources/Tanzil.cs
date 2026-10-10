using System.Globalization;
using System.Xml.Linq;

namespace QuranKG.Dataset.Sources;

/// <summary>Tanzil's <c>surah|ayah|text</c> files: the Quran text variants and the translations.</summary>
public static class TanzilText
{
    public static Dictionary<VerseRef, string> Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing source file {path} — run `just fetch`.", path);

        var verses = new Dictionary<VerseRef, string>();
        foreach (var raw in File.ReadLines(path))
        {
            // The files end with a commented licence block.
            if (raw.Length == 0 || raw[0] == '#') continue;

            var line  = raw.TrimStart('﻿');
            var parts = line.Split('|', 3);
            if (parts.Length != 3) throw new FormatException($"{Path.GetFileName(path)}: not a 'surah|ayah|text' line: {line}");

            var key = new VerseRef(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
            if (!verses.TryAdd(key, parts[2].Trim())) throw new FormatException($"{Path.GetFileName(path)}: verse {key} appears twice");
        }

        return verses;
    }
}

/// <summary>
/// Tanzil's <c>quran-data.xml</c>: the 114 surahs and the start of every juz, hizb quarter,
/// manzil, ruku and page, plus the 15 sajdas.
/// </summary>
public sealed class TanzilMetadata
{
    public sealed record SurahRow(int Index, int Ayas, int Start, string Name, string TName, string EName, string Type, int Order, int Rukus);

    public required IReadOnlyList<SurahRow>                   Surahs       { get; init; }
    public required IReadOnlyList<VerseRef>                   JuzStarts    { get; init; }
    public required IReadOnlyList<VerseRef>                   QuarterStarts{ get; init; }
    public required IReadOnlyList<VerseRef>                   ManzilStarts { get; init; }
    public required IReadOnlyList<VerseRef>                   RukuStarts   { get; init; }
    public required IReadOnlyList<VerseRef>                   PageStarts   { get; init; }
    public required IReadOnlyDictionary<VerseRef, string>     Sajdas       { get; init; }

    public static TanzilMetadata Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing source file {path} — run `just fetch`.", path);

        var doc = XDocument.Load(path);

        static int I(XElement e, string a) => int.Parse((string?)e.Attribute(a) ?? throw new FormatException($"<{e.Name}> without '{a}'"), CultureInfo.InvariantCulture);
        static string S(XElement e, string a) => (string?)e.Attribute(a) ?? "";

        List<VerseRef> Starts(string element) => doc.Descendants(element)
            .OrderBy(e => I(e, "index"))
            .Select(e => new VerseRef(I(e, "sura"), I(e, "aya")))
            .ToList();

        return new TanzilMetadata
        {
            Surahs = doc.Descendants("sura")
                .Select(e => new SurahRow(I(e, "index"), I(e, "ayas"), I(e, "start"), S(e, "name"), S(e, "tname"), S(e, "ename"), S(e, "type"), I(e, "order"), I(e, "rukus")))
                .OrderBy(s => s.Index)
                .ToList(),
            JuzStarts     = Starts("juz"),
            QuarterStarts = Starts("quarter"),
            ManzilStarts  = Starts("manzil"),
            RukuStarts    = Starts("ruku"),
            PageStarts    = Starts("page"),
            Sajdas        = doc.Descendants("sajda").ToDictionary(e => new VerseRef(I(e, "sura"), I(e, "aya")), e => S(e, "type")),
        };
    }

    /// <summary>
    /// For each verse in mushaf order, the 1-based number of the division it falls in, given the
    /// divisions' start verses: the last division whose start is not after the verse.
    /// </summary>
    public static int[] Assign(IReadOnlyList<VerseRef> versesInOrder, IReadOnlyList<VerseRef> starts)
    {
        var result = new int[versesInOrder.Count];
        var next   = 0;
        for (var i = 0; i < versesInOrder.Count; i++)
        {
            while (next < starts.Count && starts[next].CompareTo(versesInOrder[i]) <= 0) next++;
            result[i] = next; // number of starts at or before this verse = the division number
        }
        return result;
    }
}
