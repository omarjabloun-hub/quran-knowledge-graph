using Microsoft.Data.Sqlite;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// QUL ayah topics (Tarteel), distributed as a SQLite file behind a free login. Optional: the
/// corpus loads without it, with no Topic nodes.
///
/// QUL documents the content (topic id, English and Arabic name, family, parent, verse key) but
/// not its table and column names, so this reader finds them: the topic table is the one with an
/// id, a name and a parent column; the mapping table is the one with a topic id and a verse
/// reference (a "2:255" key, or surah + ayah numbers). If the file does not fit, it fails with the
/// schema it found, so the column names can be added below.
/// </summary>
public static class QulTopics
{
    private static readonly string[] IdColumns        = ["id", "topic_id"];
    private static readonly string[] NameColumns      = ["name", "name_en", "english_name", "title"];
    private static readonly string[] ArabicColumns    = ["arabic_name", "name_ar", "ar_name", "name_arabic"];
    private static readonly string[] ParentColumns    = ["parent_id", "parent_topic_id", "parent"];
    private static readonly string[] FamilyColumns    = ["category", "type", "topic_type", "kind", "family"];
    private static readonly string[] DescriptionCols  = ["description", "desc", "summary"];
    private static readonly string[] TopicRefColumns  = ["topic_id"];
    private static readonly string[] VerseKeyColumns  = ["ayah_key", "verse_key", "key"];
    private static readonly string[] SurahColumns     = ["surah_id", "surah", "chapter_id", "surah_number"];
    private static readonly string[] AyahColumns      = ["ayah_number", "ayah", "verse_number", "ayah_id"];

    public static IReadOnlyList<Topic> Read(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();

        var schema = Tables(connection);

        var topicTable = schema.FirstOrDefault(t => Has(t.Columns, IdColumns) && Has(t.Columns, NameColumns) && Has(t.Columns, ParentColumns))
            ?? throw new FormatException("QUL topics: no table with id, name and parent columns. Found: " + Describe(schema));

        var mapTable = schema.FirstOrDefault(t => t != topicTable && Has(t.Columns, TopicRefColumns)
                                                  && (Has(t.Columns, VerseKeyColumns) || (Has(t.Columns, SurahColumns) && Has(t.Columns, AyahColumns))))
            ?? throw new FormatException("QUL topics: no table mapping topic_id to a verse. Found: " + Describe(schema));

        var verses = new Dictionary<string, List<VerseRef>>(StringComparer.Ordinal);
        {
            var topicCol = Pick(mapTable.Columns, TopicRefColumns)!;
            var keyCol   = Pick(mapTable.Columns, VerseKeyColumns);
            var surahCol = Pick(mapTable.Columns, SurahColumns);
            var ayahCol  = Pick(mapTable.Columns, AyahColumns);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = keyCol is not null
                ? $"SELECT \"{topicCol}\", \"{keyCol}\" FROM \"{mapTable.Name}\""
                : $"SELECT \"{topicCol}\", \"{surahCol}\", \"{ayahCol}\" FROM \"{mapTable.Name}\"";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var topicId = Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                VerseRef verse;
                if (keyCol is not null)
                {
                    if (!TryParseVerse(r.GetValue(1)?.ToString(), out verse)) continue;
                }
                else
                {
                    verse = new VerseRef(Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)));
                }

                if (!verses.TryGetValue(topicId, out var list)) verses[topicId] = list = [];
                list.Add(verse);
            }
        }

        var topics = new List<Topic>();
        {
            var idCol     = Pick(topicTable.Columns, IdColumns)!;
            var nameCol   = Pick(topicTable.Columns, NameColumns)!;
            var arCol     = Pick(topicTable.Columns, ArabicColumns);
            var parentCol = Pick(topicTable.Columns, ParentColumns)!;
            var famCol    = Pick(topicTable.Columns, FamilyColumns);
            var descCol   = Pick(topicTable.Columns, DescriptionCols);

            var cols = new[] { idCol, nameCol, arCol, parentCol, famCol, descCol };
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {string.Join(", ", cols.Select(c => c is null ? "NULL" : $"\"{c}\""))} FROM \"{topicTable.Name}\"";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string S(int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? "";

                var id     = S(0);
                var parent = S(3);
                topics.Add(new Topic
                {
                    Id          = id,
                    Name        = S(1),
                    NameArabic  = S(2),
                    ParentId    = parent is "" or "0" ? null : parent,
                    Family      = S(4),
                    Description = S(5),
                    Verses      = verses.TryGetValue(id, out var v) ? v.Distinct().OrderBy(x => x).ToList() : [],
                });
            }
        }

        return topics;
    }

    private static bool TryParseVerse(string? text, out VerseRef verse)
    {
        verse = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var s) || !int.TryParse(parts[1], out var a)) return false;
        verse = new VerseRef(s, a);
        return true;
    }

    private sealed record Table(string Name, IReadOnlyList<string> Columns);

    private static List<Table> Tables(SqliteConnection connection)
    {
        var names = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view')";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
        }

        var tables = new List<Table>();
        foreach (var name in names)
        {
            var columns = new List<string>();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{name}\")";
            using var r = cmd.ExecuteReader();
            while (r.Read()) columns.Add(r.GetString(1));
            tables.Add(new Table(name, columns));
        }

        return tables;
    }

    private static bool Has(IReadOnlyList<string> columns, string[] candidates) => Pick(columns, candidates) is not null;

    private static string? Pick(IReadOnlyList<string> columns, string[] candidates)
        => candidates.Select(c => columns.FirstOrDefault(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(x => x is not null);

    private static string Describe(IEnumerable<Table> tables) => string.Join("; ", tables.Select(t => $"{t.Name}({string.Join(", ", t.Columns)})"));
}
