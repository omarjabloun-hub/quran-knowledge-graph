using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuranKG.Workspace;

/// <summary>
/// The definitions folder as the workspace should receive it, and the one switch in it: which
/// model embeds the Arabic of every verse — <c>harrier</c> (built in), <c>ollama</c>
/// (OLLAMA_EMBEDDING_MODEL in Ollama) or <c>arabic</c> (EMBEDDING_SERVER_MODEL, AraModernBert-STS,
/// in tools/embedding-server). The last two are the same external index, pointed at another server.
///
/// <para>Only ONE embeddings index may exist on a field — the endpoints ask
/// <c>Graph.Embeddings.GetIndex(type, field)</c>, which returns the first it finds — and an import
/// only ever adds an index, it never removes the one already there. So switching provider is two
/// steps: <see cref="UseEmbeddingsAsync"/> deletes the other provider's index from the workspace,
/// and <see cref="Stage"/> writes the import with the chosen provider's file in place of the
/// other's (matched on the UniqueExportID the two files share).</para>
/// </summary>
internal static class Definitions
{
    public const string HARRIER = "harrier";
    public const string OLLAMA  = "ollama";
    public const string ARABIC  = "arabic";

    private static readonly string[] Folders = { "code", "config", "nlp" };

    private static string DefinitionsDir(Settings s) => Path.Combine(s.Root, "src", "QuranKG.Definitions");
    private static string VariantDir(Settings s)     => Path.Combine(DefinitionsDir(s), "variants", "embeddings-external", "config", "indexes", "others");

    public static string Provider(Settings s, string? requested)
    {
        var provider = (requested ?? s.Get("EMBEDDING_PROVIDER", HARRIER)!).ToLowerInvariant();
        return provider is HARRIER or OLLAMA or ARABIC ? provider : throw new UsageException($"Unknown embeddings provider '{provider}' (expected: harrier | ollama | arabic)");
    }

    /// <summary>Where the external index of <paramref name="provider"/> sends the text, as the workspace container sees it, and the model it names.</summary>
    private static (string Url, string Model) External(Settings s, string provider) => provider == ARABIC
        ? (s.Get("EMBEDDING_SERVER_URL", "http://host.docker.internal:8765")!, s.Get("EMBEDDING_SERVER_MODEL", "NAMAA-Space/AraModernBert-Base-STS")!)
        : (s.Get("OLLAMA_URL", "http://host.docker.internal:11434")!, s.Get("OLLAMA_EMBEDDING_MODEL", "qwen3-embedding:8b")!);

    /// <summary>Copies code/, config/ and nlp/ into <paramref name="outDir"/> (READMEs left out) and applies the provider.</summary>
    public static int Stage(Settings s, string outDir, string provider)
    {
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
            throw new UsageException($"{outDir} is not empty: stage into a new folder.");

        var source = DefinitionsDir(s);
        var files  = 0;

        foreach (var folder in Folders)
        {
            var from = Path.Combine(source, folder);
            if (!Directory.Exists(from)) continue;

            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;

                var to = Path.Combine(outDir, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
                files++;
            }
        }

        if (provider != HARRIER)
        {
            var (url, model) = External(s, provider);
            var index        = Path.Combine(outDir, "config", "indexes", "others");

            foreach (var variant in Directory.EnumerateFiles(VariantDir(s), "*.json"))
            {
                var json     = JsonNode.Parse(File.ReadAllText(variant))!;
                var exportID = json["UniqueExportID"]!.GetValue<string>();

                foreach (var harrier in Directory.EnumerateFiles(index, "*." + exportID + ".json")) File.Delete(harrier);

                foreach (var setting in json["Settings"]!.AsArray())
                {
                    switch (setting!["Name"]!.GetValue<string>())
                    {
                        case "ExternalProviderUrl":   setting["Value"] = url;   break;
                        case "ExternalProviderModel": setting["Value"] = model; break;
                    }
                }

                File.WriteAllText(Path.Combine(index, Path.GetFileName(variant)), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(true));
                Console.WriteLine($"  {json["NodeType"]}.{json["FieldName"]}: embedded by {model} at {url}");
            }
        }

        Console.WriteLine($"Staged {files} definition files into {outDir} (embeddings: {provider}).");
        return 0;
    }

    /// <summary>
    /// Makes <paramref name="provider"/> the embeddings model of every field the external variant
    /// covers: deletes the other provider's index on those fields from the workspace and records
    /// the choice in .env, so the next upload stages the matching file.
    ///
    /// <para>The workspace does not say which server an external index calls, and an import that
    /// changes its URL or model does not re-embed what is there. So an external index is kept only
    /// when .env records that this workspace already uses the same external provider; switching
    /// between ollama and arabic — or any external switch on production, whose provider .env does
    /// not record — deletes it, and the upload creates it afresh.</para>
    /// </summary>
    public static async Task<int> UseEmbeddingsAsync(Settings s, Target target, string provider)
    {
        var fields = Directory.EnumerateFiles(VariantDir(s), "*.json")
                              .Select(f => JsonNode.Parse(File.ReadAllText(f))!)
                              .Select(j => (NodeType: j["NodeType"]!.GetValue<string>(), Field: j["FieldName"]!.GetValue<string>()))
                              .ToHashSet();

        // An index's Tag ends with its encoder: "MosaikGraph-Verse-TextSimple-Harrier" / "…-External".
        var previous = target.Name == "local" ? Provider(s, null) : null;
        bool Keep(string tag) => provider == HARRIER ? tag.EndsWith("-Harrier", StringComparison.Ordinal)
                                                     : tag.EndsWith("-External", StringComparison.Ordinal) && previous == provider;

        using var client  = await WorkspaceClient.SignInAsync(target);
        var status        = await client.GetJsonAsync("api/index/status/all");
        var removed       = 0;

        foreach (var (uid, index) in status!["Indexes"]!.AsObject())
        {
            if (index is null || index["Type"]?.GetValue<string>() != "SentenceEmbeddingsIndex") continue;

            var nodeType = index["NodeType"]?.GetValue<string>() ?? "";
            var field    = index["FieldName"]?.GetValue<string>() ?? "";
            var tag      = index["Tag"]?.GetValue<string>() ?? "";

            if (!fields.Contains((nodeType, field)) || Keep(tag)) continue;

            await client.DeleteIndexAsync(nodeType, uid);
            Console.WriteLine($"  deleted {tag} ({uid}) from {target.Url}");
            removed++;
        }

        if (target.Name == "local") s.Write("EMBEDDING_PROVIDER", provider);
        else                        Console.WriteLine($"  production: set EMBEDDING_PROVIDER={provider} in .env before uploading to it.");

        Console.WriteLine(removed == 0
            ? $"No other embeddings index to remove; uploads now stage the {provider} file."
            : $"Removed {removed} index(es). Upload the definitions to create the {provider} one.");

        return 0;
    }
}
