using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// QurSim (Sharaf &amp; Atwell, LREC 2012): verse pairs Ibn Kathir's tafsir relates, graded
/// 0 (not obvious), 1 (related) or 2 (strongly related). Columns:
/// <c>ID, Source_Sura, Source_Verse, target_sura, Target_verse, common_roots, relevance_degree</c>.
///
/// Cleaning: malformed rows, self pairs and references to verses that do not exist are dropped;
/// a pair listed in both directions (or twice) becomes one undirected pair with its highest grade.
/// </summary>
public static class QurSim
{
    public sealed record Result(IReadOnlyList<QurSimPair> Pairs, int RowsRead, int RowsDropped);

    public static Result Read(string path, Func<VerseRef, bool> verseExists)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing source file {path} — run `just fetch`.", path);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord   = true,
            MissingFieldFound = null,
            BadDataFound      = null,
            TrimOptions       = TrimOptions.Trim,
        };

        using var reader = new StreamReader(path);
        using var csv    = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        var best    = new Dictionary<(VerseRef A, VerseRef B), int>();
        int read    = 0, dropped = 0;

        while (csv.Read())
        {
            read++;
            if (!TryInt(csv, "Source_Sura", out var ss) || !TryInt(csv, "Source_Verse", out var sa) ||
                !TryInt(csv, "target_sura", out var ts) || !TryInt(csv, "Target_verse", out var ta) ||
                !TryInt(csv, "relevance_degree", out var degree) || degree is < 0 or > 2)
            {
                dropped++;
                continue;
            }

            var a = new VerseRef(ss, sa);
            var b = new VerseRef(ts, ta);
            if (a == b || !verseExists(a) || !verseExists(b)) { dropped++; continue; }

            var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
            best[key] = best.TryGetValue(key, out var seen) ? Math.Max(seen, degree) : degree;
        }

        var pairs = best
            .OrderBy(kv => kv.Key.A).ThenBy(kv => kv.Key.B)
            .Select(kv => new QurSimPair { A = kv.Key.A, B = kv.Key.B, Degree = kv.Value })
            .ToList();

        return new Result(pairs, read, dropped);
    }

    private static bool TryInt(CsvReader csv, string column, out int value)
    {
        value = 0;
        return csv.TryGetField<string>(column, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
