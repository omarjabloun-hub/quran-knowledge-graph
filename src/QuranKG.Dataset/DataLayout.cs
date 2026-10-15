namespace QuranKG.Dataset;

/// <summary>
/// Where each source lives under <c>data/raw/</c>. The same relative paths are the
/// <c>target</c>s in <c>data/manifest.json</c>, which <c>just fetch</c> downloads into.
/// </summary>
public sealed class DataLayout(string rawDir)
{
    public string RawDir => rawDir;

    public string KaggleCsvDir       => Path.Combine(rawDir, "kaggle", "csv");
    public string TanzilMetadata     => Path.Combine(rawDir, "tanzil", "quran-data.xml");
    public string TanzilUthmani      => Path.Combine(rawDir, "tanzil", "quran-uthmani.txt");
    public string TanzilSimpleClean  => Path.Combine(rawDir, "tanzil", "quran-simple-clean.txt");
    public string TranslationQarai   => Path.Combine(rawDir, "translations", "en.qarai.txt");
    public string TranslationSaheeh  => Path.Combine(rawDir, "translations", "en.sahih.txt");
    public string Morphology         => Path.Combine(rawDir, "morphology", "quran-morphology.txt");
    public string QurSim             => Path.Combine(rawDir, "qursim", "ibn_kathir_similarity.csv");
    public string QulTopics          => Path.Combine(rawDir, "qul", "topics.db");
    public string TafsirDir          => Path.Combine(rawDir, "tafsir");

    /// <summary>Finds <c>data/raw</c> by walking up from <paramref name="start"/> to the repository root.</summary>
    public static DataLayout Discover(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "manifest.json")))
                return new DataLayout(Path.Combine(dir.FullName, "data", "raw"));
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find data/manifest.json above " + (start ?? Directory.GetCurrentDirectory()) + ". Run from inside the repository.");
    }
}
