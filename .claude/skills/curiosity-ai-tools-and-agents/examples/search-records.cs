[tools: Curiosity.ChatAITools.UID("SEARCHRecords111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Search Records")]
[tools: Curiosity.ChatAITools.Description("Searches the records of this workspace's knowledge graph by text and returns each match's id, type and label. Use it to reach any record that is not attached to the conversation - by identifier, title or subject - then pass the id to consult to read it in full.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-search")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// Reference template — definitions path: code/chat-ai-tools/SEARCHRecords111111111.cs
// Deployed in eight applications on 26.10. See ../SKILL.md §2.
//
// WHY IT EXISTS. The built-in "Search Files" tool searches FILES only, and the built-in `search`
// interprets the question through an agent over the llm-search.json types — which found nothing
// for exact identifiers in every demo that tried it. Without this the assistant has no way to
// reach a graph record it was not handed.
//
// The results go through the ordinary workspace search AS THE CURRENT USER, so access control
// and the configured indexes apply exactly as in the search box — which also means it finds
// whatever is INDEXED. Do not index the ground-truth type (../SKILL.md §7).
//
// The UID is fixed and readable on purpose: every application ships this tool under the same id,
// so the chat view, the assistant and the agents pin it by that id. It round-trips through the
// UID parser; check any readable UID you mint yourself (UID128.Parse(s).ToString() == s).

public class RecordSearchTools
{
    private const int DEFAULT_LIMIT = 15;

    // Tried in order for a record's label - identifier fields first, then the prose ones. Adapt to
    // the corpus: the label is what the model reads to decide which hit to consult.
    private static readonly string[] LABEL_FIELDS = { "DocId", "DocumentId", "Id", "Code", "RecordKey", "Title", "Name", "SummaryText", "Abstract", "Description" };

    [Tool("Searches the workspace's records (documents, master data, people, reviews...) by text and returns up to `limit` matches, best first, each with its id, type and label. Prefer an identifier (a document id, a part number, a code) over prose when you have one. Pass `nodeType` to restrict the search to one record type. Then call consult with a returned id to read the record in full.")]
    public static async Task<string> SearchRecords(ToolScope scope,
          [Parameter("What to search for - an identifier, a title, or a few distinctive words.", required: true)] string query,
          [Parameter("Restrict to one record type, e.g. Case. Leave empty to search every type.", required: false)] string nodeType = null,
          [Parameter("Maximum results to return (default 15).", required: false)] int limit = DEFAULT_LIMIT)
    {
        if (string.IsNullOrWhiteSpace(query)) return "{\"error\":\"query is required\"}";
        if (limit <= 0 || limit > 50) limit = DEFAULT_LIMIT;

        scope.SetToolCallDisplayName("Searching for '" + query + "'");

        using var request = SearchRequest.For(query);

        // A type name the model invented would fail the whole search: search every type instead,
        // and say so, rather than failing the turn.
        string note = null;

        if (!string.IsNullOrWhiteSpace(nodeType))
        {
            if (scope.Graph.TryGetNodeSchemaKey(nodeType.Trim(), out _)) request.BeforeTypesFacet.Add(nodeType.Trim());
            else note = "'" + nodeType + "' is not a record type in this workspace; searched every type instead.";
        }

        var search = await scope.Graph.CreateSearchAsUserAsync(request, scope.CurrentUser, scope.CancellationToken);

        var hits = new List<object>();

        foreach (var node in search.Take(limit).AsEnumerable())
        {
            hits.Add(new { id = node.UID.ToString(), type = node.Type, label = LabelOf(node) });
        }

        return JsonSerializer.Serialize(new { query, nodeType, note, count = hits.Count, results = hits });
    }

    private static string LabelOf(ReadOnlyNode node)
    {
        var parts = new List<string>();

        foreach (var field in LABEL_FIELDS)
        {
            string value = null;
            try { value = node.GetString(field); } catch (Exception) { continue; } // the type has no such field

            if (string.IsNullOrWhiteSpace(value) || parts.Contains(value)) continue;

            parts.Add(value.Length > 160 ? value.Substring(0, 160) + "…" : value);
            if (parts.Count == 2) break;
        }

        return string.Join(" - ", parts);
    }
}

// A tool script ENDS by returning the instance. Without this line it compiles and then loads as
// "Can't parse tool (Tool is null)", exposing no functions.
return new RecordSearchTools();
