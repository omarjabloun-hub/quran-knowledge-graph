// The graph connector.
//
//   dotnet run --project src/QuranKG.Connector                       ingest into CURIOSITY_URL with CURIOSITY_API_TOKEN
//   dotnet run --project src/QuranKG.Connector -- --dry-run          map everything, write nothing, print the counts
//   dotnet run --project src/QuranKG.Connector -- --schema-only      register node + edge schemas, write no data
//   dotnet run --project src/QuranKG.Connector -- --only tafsir      only these steps (comma-separated), the rest left as it is
//   dotnet run --project src/QuranKG.Connector -- --emit-schema <N.cs path>
//
// Idempotent: every node is keyed, so a re-run updates in place. There is deliberately no hash
// cache — the corpus is fixed-size and ingests in about a minute, and a cache surviving a wiped
// workspace would silently skip everything.

using System.Diagnostics;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using QuranKG.Connector;
using QuranKG.Dataset;

var emitTo = Arg("--emit-schema");
if (emitTo is not null)
{
    SchemaEmitter.Emit(emitTo);
    return 0;
}

var dryRun     = Flag("--dry-run");
var schemaOnly = Flag("--schema-only");
var only       = Arg("--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
var url        = Arg("--url")   ?? Environment.GetEnvironmentVariable("CURIOSITY_URL") ?? "http://localhost:8080/";
var token      = Arg("--token") ?? Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var name       = Environment.GetEnvironmentVariable("WORKSPACE_CONNECTOR_NAME") is { Length: > 0 } n ? n : "Quran Knowledge Graph";

if (!dryRun && (string.IsNullOrWhiteSpace(token) || token == "replace-with-api-token"))
{
    Console.Error.WriteLine("CURIOSITY_API_TOKEN is not set (see .env and docs/local-setup.md), or pass --dry-run.");
    return 2;
}

var stopwatch = Stopwatch.StartNew();

Corpus corpus;
try
{
    corpus = CorpusLoader.Load(DataLayout.Discover());
}
catch (Exception ex) when (ex is CorpusException or FileNotFoundException or DirectoryNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

Console.WriteLine($"corpus: {corpus.Verses.Count:N0} verses, {corpus.Words.Count:N0} words, {corpus.Roots.Count:N0} roots, {corpus.Lemmas.Count:N0} lemmas, {corpus.Topics.Count:N0} topics, {corpus.QurSimPairs.Count:N0} QurSim pairs, {corpus.TafsirEntries.Count:N0} tafsir entries in {corpus.TafsirEditions.Count} editions, {corpus.Hadiths.Count:N0} hadith, {corpus.QuranQuotes.Count:N0} hadith–verse links, {corpus.Recitations.Count} recited surahs");

using var loggerFactory = LoggerFactory.Create(f => f.AddConsole().SetMinimumLevel(LogLevel.Warning));

// A dry run never creates the client: see ConnectorContext.
var graph = dryRun ? null : Graph.Connect(url, token!, connectorName: name).WithLoggingFactory(loggerFactory);

var ctx = new ConnectorContext(graph, corpus);

try
{
    if (graph is not null)
    {
        await SyncSchemaAsync(graph);
        Console.WriteLine($"schema registered on {url}");
        if (schemaOnly) return Finish(0);
        graph.SetAutoCommitCost(everyNodes: 10_000, everyEdges: 50_000);
    }

    await Step("structure",  () => StructureIngest.RunAsync(ctx));
    await Step("language",   () => LanguageIngest.RunAsync(ctx));
    await Step("verses",     () => VerseIngest.RunAsync(ctx));
    await Step("words",      () => WordIngest.RunAsync(ctx));
    await Step("topics",     () => TopicIngest.RunAsync(ctx));
    await Step("qursim",     () => QurSimIngest.RunAsync(ctx));
    await Step("tafsir",     () => TafsirIngest.RunAsync(ctx));
    await Step("hadith",     () => HadithIngest.RunAsync(ctx));
    await Step("recitation", () => RecitationIngest.RunAsync(ctx));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.GetType().Name} — {ex.Message}");
    return Finish(1);
}

stopwatch.Stop();
ctx.PrintSummary();
Console.WriteLine($"\nTOTAL {ctx.TotalNodes:N0} nodes, {ctx.TotalEdges:N0} edges in {stopwatch.Elapsed.TotalSeconds:F1} s{(dryRun ? "  (dry run — nothing written)" : "")}");
return Finish(0);

// Graph.Connect leaves a foreground worker running, and Dispose blocks when the endpoint is
// unreachable (the dry-run case), so tear down with a deadline and exit explicitly.
int Finish(int exitCode)
{
    var teardown = Task.Run(() => graph?.Dispose());
    if (!teardown.Wait(TimeSpan.FromSeconds(15))) Console.WriteLine("teardown did not finish in 15 s — exiting anyway");
    Console.Out.Flush();
    Environment.Exit(exitCode);
    return exitCode;
}

async Task Step(string label, Func<Task> action)
{
    if (only is not null && !only.Contains(label)) return;
    var (nodes, edges) = (ctx.TotalNodes, ctx.TotalEdges);
    var sw = Stopwatch.StartNew();
    await action();
    Console.WriteLine($"  {label,-10} +{ctx.TotalNodes - nodes,8:N0} nodes  +{ctx.TotalEdges - edges,8:N0} edges  {sw.Elapsed.TotalSeconds,6:F1} s");
}

static async Task SyncSchemaAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<Nodes.Surah>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Verse>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Word>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Root>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Lemma>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Juz>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Revelation>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Topic>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.QurSimPair>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Cluster>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Tafsir>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.TafsirEntry>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.HadithCollection>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.HadithBook>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Hadith>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.QuranQuote>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Reciter>(overwrite: true);
    await graph.CreateNodeSchemaAsync<Nodes.Recitation>(overwrite: true);
    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

string? Arg(string flag)
{
    var argv = Environment.GetCommandLineArgs();
    var i    = Array.IndexOf(argv, flag);
    return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
}

bool Flag(string flag) => Environment.GetCommandLineArgs().Contains(flag);
