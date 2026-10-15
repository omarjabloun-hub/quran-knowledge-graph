using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuranKG.Tools;

/// <summary>
/// Downloads every source in data/manifest.json into data/raw/, checks its SHA-256 and unzips
/// what needs unzipping. Idempotent: a file already present with the right checksum is kept.
/// A source marked "manual" (it needs a login) is not downloaded; its steps are printed.
///
/// A source with "parts" is one file per part (a tafsir: one per surah): "{part}" in its url is
/// replaced by 1…parts, each saved as {target}/{part}.json, and its sha256 is the checksum of the
/// parts' bytes concatenated in order. "partNames" names the parts instead of numbering them (a
/// recitation's pages are addressed by surah name: {part} becomes the i-th name, still saved as
/// {i}.json).
///
/// "extract" keeps only what a regular expression captures (group 1) of each part, after the
/// page's escaped quotes (\") are unescaped: a web page whose data is a few kilobytes of JSON in
/// a few hundred of markup. The checksum is then over what was kept, so it holds while the page's
/// markup changes around the data.
/// </summary>
internal static class Fetch
{
    private sealed class Manifest
    {
        [JsonPropertyName("sources")] public List<Source> Sources { get; set; } = [];
    }

    private sealed class Source
    {
        [JsonPropertyName("name")]    public string       Name    { get; set; } = "";
        [JsonPropertyName("title")]   public string       Title   { get; set; } = "";
        [JsonPropertyName("url")]     public string       Url     { get; set; } = "";
        [JsonPropertyName("target")]  public string       Target  { get; set; } = "";
        [JsonPropertyName("sha256")]  public string?      Sha256  { get; set; }
        [JsonPropertyName("unzipTo")] public string?      UnzipTo { get; set; }
        [JsonPropertyName("manual")]  public bool         Manual  { get; set; }
        [JsonPropertyName("parts")]   public int          Parts   { get; set; }
        [JsonPropertyName("partNames")] public List<string> PartNames { get; set; } = [];
        [JsonPropertyName("extract")] public string?      Extract { get; set; }
        [JsonPropertyName("steps")]   public List<string> Steps   { get; set; } = [];
        [JsonPropertyName("licence")] public string       Licence { get; set; } = "";
    }

    public static async Task<int> RunAsync(string root, bool force, string? only)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(Path.Combine(root, "data", "manifest.json")))
                       ?? throw new InvalidDataException("data/manifest.json is empty");
        var raw = Path.Combine(root, "data", "raw");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("quran-knowledge-graph-fetch/1.0");

        var failures = 0;
        foreach (var source in manifest.Sources.Where(s => only is null || s.Name == only))
        {
            var target = Path.Combine(raw, source.Target);
            Console.WriteLine($"\n==> {source.Name}: {source.Title}");

            if (source.Manual)
            {
                if (File.Exists(target)) { Console.WriteLine($"    present: data/raw/{source.Target}"); continue; }
                Console.WriteLine("    needs a manual download (login required):");
                foreach (var step in source.Steps) Console.WriteLine("      - " + step);
                Console.WriteLine("    (optional: everything else works without it)");
                continue;
            }

            if (source.PartNames.Count > 0) source.Parts = source.PartNames.Count;
            if (source.Parts > 0)
            {
                if (!await FetchPartsAsync(http, source, target, force)) failures++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (!force && File.Exists(target) && (source.Sha256 is null || Sha256Of(target) == source.Sha256))
            {
                Console.WriteLine($"    present: data/raw/{source.Target}");
            }
            else
            {
                Console.WriteLine($"    downloading {source.Url}");
                var tmp = target + ".part";
                using (var response = await http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var file = File.Create(tmp);
                    await response.Content.CopyToAsync(file);
                }

                var actual = Sha256Of(tmp);
                if (source.Sha256 is not null && actual != source.Sha256)
                {
                    Console.Error.WriteLine($"    CHECKSUM MISMATCH for {source.Name}: expected {source.Sha256}, got {actual}.");
                    Console.Error.WriteLine($"    The source changed upstream. Inspect {tmp}; if it is fine, update sha256 in data/manifest.json.");
                    failures++;
                    continue;
                }

                File.Move(tmp, target, overwrite: true);
                Console.WriteLine($"    saved data/raw/{source.Target} ({new FileInfo(target).Length / 1024:N0} KB, sha256 ok)");
            }

            if (source.UnzipTo is not null)
            {
                var dir = Path.Combine(raw, source.UnzipTo);
                if (force && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    ZipFile.ExtractToDirectory(target, dir, overwriteFiles: true);
                    Console.WriteLine($"    unzipped to data/raw/{source.UnzipTo} ({Directory.EnumerateFiles(dir).Count()} files)");
                }
            }
        }

        Console.WriteLine(failures == 0 ? "\nAll sources present. Next: just check-data" : $"\n{failures} source(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>A source in parts: the missing parts downloaded, then the whole checked at once.</summary>
    private static async Task<bool> FetchPartsAsync(HttpClient http, Source source, string dir, bool force)
    {
        var parts = Enumerable.Range(1, source.Parts).Select(i => (Index: i, Path: Path.Combine(dir, i + ".json"))).ToList();

        if (!force && parts.All(p => File.Exists(p.Path)) && (source.Sha256 is null || Sha256Of(parts.Select(p => p.Path)) == source.Sha256))
        {
            Console.WriteLine($"    present: data/raw/{source.Target}/ ({source.Parts} parts)");
            return true;
        }

        Directory.CreateDirectory(dir);
        Console.WriteLine($"    downloading {source.Parts} parts of {source.Url}");

        var missing = parts.Where(p => force || !File.Exists(p.Path)).ToList();
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(missing.Select(async p =>
        {
            await gate.WaitAsync();
            try
            {
                var name  = source.PartNames.Count > 0 ? source.PartNames[p.Index - 1] : p.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var bytes = await http.GetByteArrayAsync(source.Url.Replace("{part}", name));
                if (source.Extract is not null)
                {
                    var page  = System.Text.Encoding.UTF8.GetString(bytes).Replace("\\\"", "\"");
                    var match = System.Text.RegularExpressions.Regex.Match(page, source.Extract);
                    if (!match.Success) throw new InvalidDataException($"{source.Name}: nothing to extract from part {p.Index} ({name}) — the page changed; inspect {source.Url.Replace("{part}", name)}.");
                    bytes = System.Text.Encoding.UTF8.GetBytes(match.Groups[1].Value);
                }
                await File.WriteAllBytesAsync(p.Path, bytes);
            }
            finally { gate.Release(); }
        }));

        var actual = Sha256Of(parts.Select(p => p.Path));
        if (source.Sha256 is not null && actual != source.Sha256)
        {
            Console.Error.WriteLine($"    CHECKSUM MISMATCH for {source.Name}: expected {source.Sha256}, got {actual}.");
            Console.Error.WriteLine($"    The source changed upstream. Inspect data/raw/{source.Target}/; if it is fine, update sha256 in data/manifest.json.");
            return false;
        }

        var size = parts.Sum(p => new FileInfo(p.Path).Length);
        Console.WriteLine($"    saved data/raw/{source.Target}/ ({source.Parts} parts, {size / 1024:N0} KB, sha256 {(source.Sha256 is null ? actual + " — not pinned yet" : "ok")})");
        return true;
    }

    private static string Sha256Of(IEnumerable<string> paths)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths) sha.AppendData(File.ReadAllBytes(path));
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

internal static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "manifest.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Run from inside the quran-knowledge-graph repository (no data/manifest.json found above the current directory).");
    }
}
