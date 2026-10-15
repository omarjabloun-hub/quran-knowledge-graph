[tools: Curiosity.ChatAITools.UID("HadiThTooLs11111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Hadith Tools")]
[tools: Curiosity.ChatAITools.Description("Reads the hadith of Sahih al-Bukhari and Sahih Muslim against the Quran: finds hadith by reference (Bukhari 6018, Muslim 8a), by words in Arabic or English, or by a verse (FindHadith); reads one hadith with the verses its Arabic quotes and the verses closest to it in meaning and words (ReadHadith); lists the hadith that quote a verse or are close to it (HadithForVerse). Use these for any question about a hadith or about how a hadith and the Quran relate, INSTEAD OF quoting a hadith from memory.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-comment-quote")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// The assistant's instruments for hadith and the Quran. They call the endpoints the hadith pages
// read — qkg/hadith/list, qkg/hadith/profile, qkg/verse/hadith — and give the model the evidence:
// the hadith's Arabic without its chain (Hadith.Matn) and its English, the verses its Arabic quotes
// (with how: a quotation, a shared phrase, the translator's citation), and the verses closest to it
// by the meaning of the Arabic and the words they share, with each signal's part.
//
// THE ANSWER KEYS STAY OUT (CLAUDE.md, non-negotiable 2): those endpoints read neither QurSimPair,
// Topic, Verse.Ruku nor the tafsirs. The hadith are not an answer key: nothing is scored against them.

[ToolSystemPrompt("""
                  A hadith is quoted from the Hadith tools, never from memory: ReadHadith gives its
                  Arabic (the text without the chain of narrators) and its English, and how it meets
                  the Quran. Cite a hadith as the tools do (Bukhari 6018, Muslim 8a). "Quotes" means
                  the hadith's Arabic contains the verse's words; "close in meaning" is a ranking of
                  candidates, not a link the text makes — read both texts before saying how they relate.
                  """)]
public class HadithTools
{
    [Tool("Finds hadith of Sahih al-Bukhari and Sahih Muslim. `query` is a reference (Bukhari 6018, Muslim 8a), Arabic words (any spelling, diacritics ignored) or English words. `verse` (2:255) lists instead the hadith that quote that verse and those closest to it in meaning. `collection` is bukhari, muslim or empty for both. `quoting` = true keeps only hadith whose Arabic quotes a verse. Returns each hadith's reference, uid, book, the opening of its Arabic text and its English, how it meets the Quran, and the verses it quotes. Read one in full with ReadHadith.")]
    public static async Task<string> FindHadith(ToolScope scope,
        [Parameter("A reference (Bukhari 6018, Muslim 8a) or words in Arabic or English.", required: false)] string query = null,
        [Parameter("A verse key (2:255): the hadith that quote it or are close to it in meaning.", required: false)] string verse = null,
        [Parameter("bukhari | muslim | empty for both.", required: false)] string collection = null,
        [Parameter("Only hadith whose Arabic quotes a verse (default false).", required: false)] bool quoting = false,
        [Parameter("How many to return (default 15, at most 40).", required: false)] int limit = 15)
    {
        limit = limit <= 0 ? 15 : Math.Min(limit, 40);

        if (!string.IsNullOrWhiteSpace(verse))
            return await HadithForVerse(scope, verse, limit);

        if (string.IsNullOrWhiteSpace(query) && !quoting)
            return Out(new { error = "Give a reference, words to find, or a verse." });

        scope.SetToolCallDisplayName("Finding hadith: " + (query ?? "quoting the Quran"));
        var body = Newtonsoft.Json.JsonConvert.SerializeObject(new { Text = query ?? "", Collection = collection ?? "", Kind = quoting ? "any" : "all", Take = limit });
        var answer = await Endpoint(scope, "qkg/hadith/list", body);
        return answer?.ToString(Newtonsoft.Json.Formatting.None) ?? Out(new { error = "qkg/hadith/list returned nothing" });
    }

    [Tool("Reads one hadith in full: its reference, collection and book; its Arabic text without the chain of narrators (the evidence) and its English (a reading aid); every verse its Arabic quotes, with how (quotation: six words or more or a whole short verse; phrase: four or five words, often a shared formula; translator: only the English translator cites it), the words quoted, the verse's Arabic and English; and the ten verses closest to it — by the meaning of the Arabic (one embedding model for the hadith and the verses) and the hadith's distinctive words the verse uses — with each signal's part and the shared words. Takes a reference (Bukhari 6018, Muslim 8a) or a hadith uid.")]
    public static async Task<string> ReadHadith(ToolScope scope,
        [Parameter("A reference (Bukhari 6018, Muslim 8a) or the hadith's uid.", required: true)] string hadith)
    {
        var uid = await Resolve(scope, hadith);
        if (uid is null) return Out(new { error = "No hadith '" + hadith + "': give a reference like Bukhari 6018 or Muslim 8a, or find it with FindHadith." });

        scope.SetToolCallDisplayName("Reading " + hadith);
        var answer = await Endpoint(scope, "qkg/hadith/profile", "{\"Uid\":\"" + uid + "\"}");
        if (answer == null) return Out(new { error = "qkg/hadith/profile returned nothing" });

        // The full Arabic with its chain is long and not the evidence: the text without it is.
        answer.Remove("TextArabic");
        return answer.ToString(Newtonsoft.Json.Formatting.None);
    }

    [Tool("Lists the hadith of Bukhari and Muslim on one verse: those whose Arabic quotes it or shares a phrase with it, then those whose translator cites it, then those closest to it in meaning (the Arabic of both, one embedding model). Takes a verse key (2:255).")]
    public static async Task<string> HadithForVerse(ToolScope scope,
        [Parameter("The verse key, e.g. 4:36.", required: true)] string verse,
        [Parameter("How many hadith close in meaning to list (default 10, at most 40).", required: false)] int count = 10)
    {
        count = count <= 0 ? 10 : Math.Min(count, 40);
        var key = (verse ?? "").Trim();
        var v = scope.Q().StartAt(N.Verse.Type).AsEnumerable().FirstOrDefault(x => x.GetString(N.Verse.VerseKey) == key);
        if (v == null) return Out(new { error = "No verse " + verse + ": use surah:ayah, e.g. 4:36." });

        scope.SetToolCallDisplayName("Hadith on " + key);
        var answer = await Endpoint(scope, "qkg/verse/hadith", "{\"Uid\":\"" + v.UID + "\",\"Count\":" + count + "}");
        return answer?.ToString(Newtonsoft.Json.Formatting.None) ?? Out(new { error = "qkg/verse/hadith returned nothing" });
    }

    // -----------------------------------------------------------------------------------------

    /// <summary>A reference ("Bukhari 6018") or a uid → the hadith's uid, or null.</summary>
    private static async Task<string> Resolve(ToolScope scope, string hadith)
    {
        var text = (hadith ?? "").Trim();
        if (text.Length == 22 && UID128.TryParse(text, out var uid) && scope.Graph.TryGet(uid, out var node) && node.Type == N.Hadith.Type) return text;

        var body   = Newtonsoft.Json.JsonConvert.SerializeObject(new { Text = text, Kind = "all", Take = 1 });
        var answer = await Endpoint(scope, "qkg/hadith/list", body);
        var rows   = answer?["Rows"] as Newtonsoft.Json.Linq.JArray;
        return rows != null && rows.Count > 0 ? rows[0]["Uid"]?.ToString() : null;
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new System.Text.Json.JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Out(object o) => System.Text.Json.JsonSerializer.Serialize(o, Json);

    /// <summary>An endpoint as the user, read as a JSON object (RunEndpointAsync&lt;string&gt; is null for a DTO).</summary>
    private static Task<Newtonsoft.Json.Linq.JObject> Endpoint(ToolScope scope, string path, string body)
        => scope.RunEndpointAsync<Newtonsoft.Json.Linq.JObject>(new EndpointUID(path), body, cancellationToken: scope.CancellationToken);
}

return new HadithTools();
