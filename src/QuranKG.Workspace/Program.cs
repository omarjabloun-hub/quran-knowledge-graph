// Operations on a running workspace, for the scripts in scripts/ — written once in .NET so the
// bash and PowerShell scripts stay thin and a Mac needs neither jq nor Python.
//
//   dotnet run --project src/QuranKG.Workspace -- tokens          [--target local|prod] [--days 365]
//   dotnet run --project src/QuranKG.Workspace -- status          [--target local|prod]
//   dotnet run --project src/QuranKG.Workspace -- call <path> [json]  [--target local|prod]
//   dotnet run --project src/QuranKG.Workspace -- warm            [--target local|prod]
//   dotnet run --project src/QuranKG.Workspace -- stage-definitions <out-dir> [--provider harrier|ollama]
//   dotnet run --project src/QuranKG.Workspace -- use-embeddings harrier|ollama [--target local|prod]
//
// Every command reads .env from the repository root (a variable set in the environment wins).

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuranKG.Workspace;

var positional = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || !args[i - 1].StartsWith("--", StringComparison.Ordinal))).ToList();
var command    = positional.FirstOrDefault() ?? "help";

try
{
    var settings = Settings.Load();
    var target   = settings.ResolveTarget(Option("--target") ?? "local");

    return command switch
    {
        "tokens"            => await Tokens(settings, target, int.Parse(Option("--days") ?? "365", CultureInfo.InvariantCulture)),
        "status"            => await Status(target),
        "call"              => await Call(target, positional.ElementAtOrDefault(1) ?? throw new UsageException("call <endpoint path> [json body]"), positional.ElementAtOrDefault(2) ?? "{}"),
        "warm"              => await Warm(target),
        "stage-definitions" => Definitions.Stage(settings, positional.ElementAtOrDefault(1) ?? throw new UsageException("stage-definitions <out-dir>"), Definitions.Provider(settings, Option("--provider"))),
        "use-embeddings"    => await Definitions.UseEmbeddingsAsync(settings, target, Definitions.Provider(settings, positional.ElementAtOrDefault(1) ?? throw new UsageException("use-embeddings harrier|ollama"))),
        _                   => Help(),
    };
}
catch (UsageException ex)
{
    Console.Error.WriteLine("ERROR: " + ex.Message);
    return 2;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"ERROR: the workspace did not answer ({ex.Message}). For the local one, run `just up` first.");
    return 1;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Help()
{
    Console.WriteLine("usage: QuranKG.Workspace tokens | status | call <path> [json] | warm | stage-definitions <dir> | use-embeddings harrier|ollama   [--target local|prod]");
    return 2;
}

// Signs in as the administrator in .env and mints the two tokens the scripts use, writing them
// into .env: a Library token (connector + definitions import) and an Interface Upload token.
static async Task<int> Tokens(Settings settings, Target target, int days)
{
    using var client = await WorkspaceClient.SignInAsync(target);

    var library = (await client.MintLibraryTokenAsync("QuranKG scripts: connector and definitions", days)).Trim().Trim('"');
    var upload  = (await client.MintUploadTokenAsync("QuranKG scripts: front-end upload", days)).Trim().Trim('"');

    settings.Write(target.ApiTokenKey, library);
    settings.Write(target.UploadTokenKey, upload);

    Console.WriteLine($"Wrote {target.ApiTokenKey} and {target.UploadTokenKey} to .env ({target.Url}, valid {days} days).");
    return 0;
}

// What is in the workspace: node counts, whether the code compiles, and how far the embeddings
// indexes have got — the semantic part of "similar verses" waits for Verse.TextSimple.
static async Task<int> Status(Target target)
{
    string[] types = { "Surah", "Verse", "Word", "Root", "Lemma", "Juz", "Revelation", "Topic", "QurSimPair", "Cluster" };

    using var client = await WorkspaceClient.SignInAsync(target);

    Console.WriteLine($"Workspace {target.Url}");
    Console.WriteLine();
    Console.WriteLine("Nodes");
    foreach (var type in types) Console.WriteLine($"  {type,-12} {await client.CountAsync(type),10:N0}");

    var compile = await client.GetJsonAsync("api/build/compile-status");
    var failing = compile?["FailingIds"]?.AsArray().Count ?? 0;
    Console.WriteLine();
    Console.WriteLine(failing == 0 ? "Code     compiles (no failing endpoint)" : $"Code     {failing} definition(s) FAIL to compile — open Manage > Code");

    var status = await client.GetJsonAsync("api/index/status/all");
    var paused = status?["Pauses"]?.AsObject().Count ?? 0;
    Console.WriteLine();
    Console.WriteLine("Embeddings" + (paused > 0 ? "   (indexing is PAUSED)" : ""));

    foreach (var (uid, index) in status!["Indexes"]!.AsObject())
    {
        if (index?["Type"]?.GetValue<string>() != "SentenceEmbeddingsIndex") continue;

        var nodeType = index["NodeType"]?.GetValue<string>() ?? "";
        if (Array.IndexOf(types, nodeType) < 0) continue;

        var state  = status["Status"]?[uid];
        var queued = state?["QueueSize"]?.GetValue<int>() ?? 0;
        var size   = state?["Values"]?["IndexSize"]?.GetValue<string>() ?? "?";
        var tag    = index["Tag"]?.GetValue<string>() ?? "";
        var model  = tag[(tag.LastIndexOf('-') + 1)..];

        Console.WriteLine($"  {nodeType + "." + index["FieldName"]?.GetValue<string>(),-22} {model,-9} {size,7} embedded · {queued,6:N0} queued");
    }

    return 0;
}

static async Task<int> Call(Target target, string path, string json)
{
    using var client = await WorkspaceClient.SignInAsync(target);
    var (body, elapsed) = await client.RunEndpointAsync(path, json);

    try   { Console.WriteLine(JsonNode.Parse(body)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); }
    catch (JsonException) { Console.WriteLine(body); }

    Console.Error.WriteLine($"({path}: {elapsed.TotalSeconds:F1} s)");
    return 0;
}

// Compiles the endpoints and fills the dashboard's cache, so the first visitor does not wait for
// either. The cache is per user: other people still compute their own copy once an hour.
static async Task<int> Warm(Target target)
{
    using var client = await WorkspaceClient.SignInAsync(target);
    var (_, elapsed) = await client.RunEndpointAsync("qkg/dashboard/corpus", "{}");
    Console.WriteLine($"qkg/dashboard/corpus answered in {elapsed.TotalSeconds:F1} s.");
    return 0;
}
