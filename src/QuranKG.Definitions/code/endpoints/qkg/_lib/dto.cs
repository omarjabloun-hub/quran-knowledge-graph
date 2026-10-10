[endpoint: Curiosity.Endpoints.Path("qkg/_lib/dto")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

// The shared library every qkg/* endpoint imports (an ImportEndpoint comment naming this path).
// AdminOnly so it fails closed: it is a library, not an API. Never write that comment in THIS
// file: the import directive is matched anywhere in the code, and the library would import itself.
//
// Every class here is declared a SECOND time, identically, in
// src/QuranKG.FrontEnd/src/Api/Dto.cs. Both ends serialise with the same default resolver, so
// the two declarations are the wire format: add a field to one, add it to the other.
//
// DTOs are flat (Transpose has no runtime reflection): primitives, UID128 and arrays only.
// Every count ships the UID of what it counted, so a chart bar opens the node it measured.

public static class QK
{
    public const string MECCAN  = "Meccan";
    public const string MEDINAN = "Medinan";

    // --- requests -----------------------------------------------------------------------------

    public class UidRequest { public UID128 Uid { get; set; } }

    public class SimilarRequest
    {
        public UID128 Uid   { get; set; }
        /// <summary>How many verses to return (default 20, at most 100).</summary>
        public int    Count { get; set; }
    }

    // --- shared rows --------------------------------------------------------------------------

    /// <summary>A labelled count, with the UID of the node it counts (default when it is not a node).</summary>
    public class Counted
    {
        public UID128 Uid    { get; set; }
        public string Label  { get; set; }
        public string Detail { get; set; }
        public int    Count  { get; set; }
        public double Score  { get; set; }
    }

    public class PlaceStats
    {
        public UID128 Uid           { get; set; }
        public string Place         { get; set; }
        public int    Surahs        { get; set; }
        public int    Verses        { get; set; }
        public int    Words         { get; set; }
        public double WordsPerVerse { get; set; }
    }

    public class SurahStats
    {
        public UID128 Uid             { get; set; }
        public int    Number          { get; set; }
        public string NameArabic      { get; set; }
        public string Name            { get; set; }
        public string NameEnglish     { get; set; }
        public string Place           { get; set; }
        public int    RevelationOrder { get; set; }
        public int    Verses          { get; set; }
        public int    Words           { get; set; }
        public int    Nouns           { get; set; }
        public int    Verbs           { get; set; }
        public int    Particles       { get; set; }
        public int    DistinctRoots   { get; set; }
        public int    DistinctLemmas  { get; set; }
        public double WordsPerVerse   { get; set; }
    }

    // --- responses ----------------------------------------------------------------------------

    /// <summary>Everything the pattern dashboard draws, from one pass over the corpus.</summary>
    public class CorpusDto
    {
        public OverviewDto    Overview       { get; set; }
        /// <summary>The most frequent roots across the 30 juz.</summary>
        public HeatmapDto     RootsByJuz     { get; set; }
        /// <summary>The most frequent roots across the 114 surahs.</summary>
        public HeatmapDto     RootsBySurah   { get; set; }
        /// <summary>Roots most characteristic of Meccan and of Medinan surahs.</summary>
        public DistinctiveDto Distinctive    { get; set; }
        public double         Milliseconds   { get; set; }
    }

    public class OverviewDto
    {
        public int          Surahs        { get; set; }
        public int          Verses        { get; set; }
        public int          Words         { get; set; }
        public int          DistinctForms { get; set; }
        public int          Lemmas        { get; set; }
        public int          Roots         { get; set; }
        public int          Topics        { get; set; }
        public int          Clusters      { get; set; }
        public int          Nouns         { get; set; }
        public int          Verbs         { get; set; }
        public int          Particles     { get; set; }
        public PlaceStats[] Places        { get; set; }
        public SurahStats[] SurahRows     { get; set; }
        public Counted[]    TopRoots      { get; set; }
        public Counted[]    TopNouns      { get; set; }
        public Counted[]    TopVerbs      { get; set; }
    }

    public class HeatmapDto
    {
        public Counted[]  Rows          { get; set; }
        public string[]   Columns       { get; set; }
        public UID128[]   ColumnUIDs    { get; set; }
        public int[]      ColumnWords   { get; set; }
        /// <summary>Counts[row][column].</summary>
        public int[][]    Counts        { get; set; }
        /// <summary>Occurrences per 1,000 words of the column — what makes a short surah comparable to a long one.</summary>
        public double[][] PerThousand   { get; set; }
    }

    public class DistinctiveDto
    {
        public Counted[] Meccan       { get; set; }
        public Counted[] Medinan      { get; set; }
        public int       MeccanWords  { get; set; }
        public int       MedinanWords { get; set; }
    }

    public class RootProfileDto
    {
        public UID128    Uid          { get; set; }
        public string    Letters      { get; set; }
        public int       Occurrences  { get; set; }
        public int       Verses       { get; set; }
        public int       Surahs       { get; set; }
        public int       Meccan       { get; set; }
        public int       Medinan      { get; set; }
        /// <summary>Occurrences in surah 1..114 (index 0 = surah 1).</summary>
        public int[]     PerSurah     { get; set; }
        public UID128[]  SurahUIDs    { get; set; }
        public Counted[] Lemmas       { get; set; }
        public Counted[] Forms        { get; set; }
        public UID128[]  VerseUIDs    { get; set; }
    }

    public class SurahProfileDto
    {
        public UID128    Uid              { get; set; }
        public int       Verses           { get; set; }
        public int       Words            { get; set; }
        public int       DistinctRoots    { get; set; }
        public int       DistinctLemmas   { get; set; }
        public int       Nouns            { get; set; }
        public int       Verbs            { get; set; }
        public int       Particles        { get; set; }
        /// <summary>Words per verse, in ayah order.</summary>
        public int[]     VerseLengths     { get; set; }
        public UID128[]  VerseUIDs        { get; set; }
        public Counted[] TopRoots         { get; set; }
        /// <summary>Roots this surah uses far more than the rest of the Quran (tf-idf over surahs).</summary>
        public Counted[] DistinctiveRoots { get; set; }
        public Counted[] TopLemmas        { get; set; }
    }

    public class VerseWordsDto
    {
        public UID128    Uid           { get; set; }
        public UID128    SurahUID      { get; set; }
        /// <summary>The verse's words in reading order.</summary>
        public UID128[]  WordUIDs      { get; set; }
        public UID128[]  RootUIDs      { get; set; }
        public UID128[]  LemmaUIDs     { get; set; }
        /// <summary>
        /// Verses Ibn Kathir relates to this one (QurSim), with the grade in Detail. The ANSWER KEY:
        /// shown to people on the verse page, never to a similarity signal or an AI tool.
        /// </summary>
        public Counted[] TafsirRelated { get; set; }
    }

    /// <summary>One candidate verse and why it was ranked: each signal's standardised contribution.</summary>
    public class SimilarVerse
    {
        public UID128   Uid         { get; set; }
        public string   VerseKey    { get; set; }
        public double   Score       { get; set; }
        /// <summary>The Arabic text's meaning (embedding of the plain Arabic), standardised over all verses.</summary>
        public double   Semantic    { get; set; }
        /// <summary>Shared lemmas weighted by rarity, standardised.</summary>
        public double   Lemmas      { get; set; }
        /// <summary>Shared roots weighted by rarity, standardised.</summary>
        public double   Roots       { get; set; }
        /// <summary>The rarest roots the two verses share, spaced ("ر ح م").</summary>
        public string[] SharedRoots { get; set; }
    }

    public class SimilarDto
    {
        public UID128         Uid               { get; set; }
        /// <summary>False until the Arabic embeddings index has a vector for this verse: the ranking is then lexical only.</summary>
        public bool           SemanticAvailable { get; set; }
        public string         Method            { get; set; }
        public SimilarVerse[] Verses            { get; set; }
    }

    // --- shared reads -------------------------------------------------------------------------

    /// <summary>The Lemma node key the connector writes: lemma|root|coarse tag.</summary>
    public static string LemmaKey(string lemma, string root, string posTag) => lemma + "|" + (root ?? "") + "|" + posTag;

    public static bool IsContentNoun(string partOfSpeech)
        => partOfSpeech != "Pronoun" && partOfSpeech != "Relative pronoun" && partOfSpeech != "Demonstrative pronoun";

    public static Counted[] Top(Dictionary<string, int> counts, Dictionary<string, UID128> uids, int take, Func<string, string> label = null)
        => counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(take)
                 .Select(kv => new Counted { Uid = uids.TryGetValue(kv.Key, out var u) ? u : default(UID128), Label = label is null ? kv.Key : label(kv.Key), Count = kv.Value })
                 .ToArray();

    /// <summary>"رحم" → "ر ح م".</summary>
    public static string Spaced(string letters) => string.Join(" ", (letters ?? "").ToCharArray().Select(c => c.ToString()));

    public static void Bump(Dictionary<string, int> counts, string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
    }
}
