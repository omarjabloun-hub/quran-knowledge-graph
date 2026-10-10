// Local tools — none of them contacts the workspace.
//
//   dotnet run --project src/QuranKG.Tools -- fetch [--force] [--only <source>]   download data/raw from data/manifest.json
//   dotnet run --project src/QuranKG.Tools -- check                              load + validate the corpus, print counts
//   dotnet run --project src/QuranKG.Tools -- sync-dto [--check]                 regenerate the front end's copy of the endpoint DTOs
//
// `just fetch`, `just check-data` and `just sync-dto` call these.

using System.Diagnostics;
using QuranKG.Dataset;
using QuranKG.Tools;

var command = args.FirstOrDefault() ?? "help";
var root    = RepoRoot.Find();

try
{
    return command switch
    {
        "fetch"    => await Fetch.RunAsync(root, force: args.Contains("--force"), only: Arg(args, "--only")),
        "check"    => Check(root),
        "sync-dto" => SyncDto.Run(root, check: args.Contains("--check")),
        _          => Help(),
    };
}
catch (CorpusException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Check(string root)
{
    var sw     = Stopwatch.StartNew();
    var corpus = CorpusLoader.Load(new DataLayout(Path.Combine(root, "data", "raw")));
    sw.Stop();

    Console.WriteLine($"Corpus loaded and validated in {sw.Elapsed.TotalSeconds:F1} s\n");
    Console.WriteLine($"  surahs        {corpus.Surahs.Count,8:N0}");
    Console.WriteLine($"  verses        {corpus.Verses.Count,8:N0}");
    Console.WriteLine($"  words         {corpus.Words.Count,8:N0}   ({corpus.Words.Select(w => w.TextPlain).Distinct().Count():N0} distinct forms without diacritics)");
    Console.WriteLine($"  roots         {corpus.Roots.Count,8:N0}");
    Console.WriteLine($"  lemmas        {corpus.Lemmas.Count,8:N0}");
    Console.WriteLine($"  juz           {corpus.Juzs.Count,8:N0}");
    Console.WriteLine($"  topics        {corpus.Topics.Count,8:N0}");
    Console.WriteLine($"  qursim pairs  {corpus.QurSimPairs.Count,8:N0}   (answer key: degree 2 {corpus.QurSimPairs.Count(p => p.Degree == 2):N0}, 1 {corpus.QurSimPairs.Count(p => p.Degree == 1):N0}, 0 {corpus.QurSimPairs.Count(p => p.Degree == 0):N0})");
    Console.WriteLine("\nNotes");
    foreach (var note in corpus.Notes) Console.WriteLine("  - " + note);

    var top = corpus.Words.Where(w => w.Root is not null).GroupBy(w => w.Root!).OrderByDescending(g => g.Count()).Take(5);
    Console.WriteLine("\nMost frequent roots: " + string.Join("  ", top.Select(g => $"{new Root { Letters = g.Key }.Spaced} ({g.Count():N0})")));
    return 0;
}

static int Help()
{
    Console.WriteLine("usage: QuranKG.Tools fetch [--force] [--only <source>] | check | sync-dto [--check]");
    return 2;
}

static string? Arg(string[] argv, string name)
{
    var i = Array.IndexOf(argv, name);
    return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
}
