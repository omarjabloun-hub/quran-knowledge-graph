[tools: Curiosity.ChatAITools.UID("SEARCHRecords111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Search Records")]
[tools: Curiosity.ChatAITools.Description("Searches the Quran graph by text - a verse's Arabic or a phrase of its translation, a surah's name, a root, a lemma, a passage or theme - and returns each match's id, type and label. Use it to find a verse or a record you do not have the id of, then consult the id to read it in full. It ranks by text match: to COUNT occurrences use the Quran tools (CountTerm), not this.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-search")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// The workspace search, run AS THE USER (access control and the configured indexes apply exactly as
// in the search box), returning ids consult can read. The built-in Search Files finds files only,
// and this corpus has none.
//
// FENCED TO THE TYPES BELOW. Topic is indexed (the QUL topics, the clusters' future reference) and
// QurSimPair exists in the graph: both are answer keys (CLAUDE.md, non-negotiable 2), so a nodeType
// outside the list is refused and every hit is filtered by type, whatever the indexes hold.

public class RecordSearchTools
{
    private const int DEFAULT_LIMIT = 15;

    private static readonly string[] Types = { N.Verse.Type, N.Surah.Type, N.Root.Type, N.Lemma.Type, N.Cluster.Type, N.Juz.Type };

    [Tool("Searches the Quran graph by text and returns up to `limit` matches, best first, each with its id, type and label. Arabic search ignores diacritics. Pass `nodeType` (Verse, Surah, Root, Lemma, Cluster, Juz) to search one type. Then consult a returned id to read the record in full. Do not count with it: a search ranks, it does not enumerate - CountTerm and FindVerses count.")]
    public static async Task<string> SearchRecords(ToolScope scope,
          [Parameter("What to search for: Arabic words, an English phrase, a surah name, a root.", required: true)] string query,
          [Parameter("Restrict to one type: Verse, Surah, Root, Lemma, Cluster or Juz. Empty for all of them.", required: false)] string nodeType = null,
          [Parameter("Maximum results to return (default 15, at most 50).", required: false)] int limit = DEFAULT_LIMIT)
    {
        if (string.IsNullOrWhiteSpace(query)) return "{\"error\":\"query is required\"}";
        if (limit <= 0 || limit > 50) limit = DEFAULT_LIMIT;

        scope.SetToolCallDisplayName("Searching for '" + query + "'");

        using var request = SearchRequest.For(query);
        string note = null;

        var type = Types.FirstOrDefault(t => string.Equals(t, (nodeType ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (type != null) request.BeforeTypesFacet.Add(type);
        else
        {
            if (!string.IsNullOrWhiteSpace(nodeType)) note = "'" + nodeType + "' is not a type you can search; searched " + string.Join(", ", Types) + " instead.";
            foreach (var t in Types) request.BeforeTypesFacet.Add(t);
        }

        var search = await scope.Graph.CreateSearchAsUserAsync(request, scope.CurrentUser, scope.CancellationToken);

        var hits = new List<object>();
        foreach (var node in search.Take(limit * 2).AsEnumerable())
        {
            if (!Types.Contains(node.Type)) continue;
            hits.Add(new { id = node.UID.ToString(), type = node.Type, label = LabelOf(node) });
            if (hits.Count == limit) break;
        }

        return System.Text.Json.JsonSerializer.Serialize(new { query, nodeType = type, note, count = hits.Count, results = hits },
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string LabelOf(ReadOnlyNode node)
    {
        string Cut(string s) => string.IsNullOrEmpty(s) || s.Length <= 160 ? s : s.Substring(0, 160) + "…";

        if (node.Type == N.Verse.Type)   return node.GetString(N.Verse.VerseKey) + " " + Cut(node.GetString(N.Verse.TextSimple));
        if (node.Type == N.Surah.Type)   return node.GetInt(N.Surah.Number) + " " + node.GetString(N.Surah.NameArabic) + " (" + node.GetString(N.Surah.NameScholarly) + ")";
        if (node.Type == N.Root.Type)    return node.GetString(N.Root.Spaced);
        if (node.Type == N.Lemma.Type)   return node.GetString(N.Lemma.LemmaText) + " (" + node.GetString(N.Lemma.PartOfSpeech) + ", root " + node.GetString(N.Lemma.RootLetters) + ")";
        if (node.Type == N.Cluster.Type) return node.GetString(N.Cluster.Kind) + ": " + node.GetString(N.Cluster.LabelArabic) + " / " + node.GetString(N.Cluster.Label);
        if (node.Type == N.Juz.Type)     return "Juz " + node.GetInt(N.Juz.Number);
        return "";
    }
}

return new RecordSearchTools();
