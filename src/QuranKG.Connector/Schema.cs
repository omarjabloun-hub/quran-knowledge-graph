// The graph schema — the contract every endpoint and the front end are written against, through
// the N/E constants SchemaEmitter generates from these classes. See docs/graph-model.md.
//
// Rules this file follows (curiosity-graph-modelling, curiosity-data-connector):
//   - one stable [Key] per type, derivable from the source with no lookup;
//   - no field named Key, Type, Summary, Timestamp, Item, System …;
//   - no [Timestamp]: nothing in the Quran corpus is dated, so order comes from MushafIndex,
//     AyahNumber and RevelationOrder;
//   - no stored counts: every number a dashboard shows is counted live from the edges.

using Curiosity.Library;

namespace QuranKG.Connector;

internal static class Nodes
{
    /// <summary>A surah (chapter). Key: its number, "1"–"114".</summary>
    [Node]
    internal sealed class Surah
    {
        [Key]      public string SurahId         { get; set; } = "";
        [Property] public int    Number          { get; set; }
        [Property] public string NameArabic      { get; set; } = "";
        [Property] public string NameLatin       { get; set; } = "";
        [Property] public string NameScholarly   { get; set; } = "";
        [Property] public string NameEnglish     { get; set; } = "";
        /// <summary>"Meccan" or "Medinan" — also a Revelation node.</summary>
        [Property] public string RevelationPlace { get; set; } = "";
        [Property] public int    RevelationOrder { get; set; }
        [Property] public int    AyahCount       { get; set; }
        [Property] public int    RukuCount       { get; set; }
        [Property] public int    FirstVerseIndex { get; set; }
        [Property] public string Introduction    { get; set; } = "";
    }

    /// <summary>A verse (ayah). Key: "surah:ayah". The Arabic is the text; the translations are reading aids.</summary>
    [Node]
    internal sealed class Verse
    {
        [Key]      public string VerseKey        { get; set; } = "";
        [Property] public int    SurahNumber     { get; set; }
        [Property] public int    AyahNumber      { get; set; }
        [Property] public int    MushafIndex     { get; set; }
        [Property] public string SurahName       { get; set; } = "";
        /// <summary>Uthmani script with diacritics — what a reader sees.</summary>
        [Property] public string TextUthmani     { get; set; } = "";
        /// <summary>Standard spelling without diacritics — what the Arabic indexes and the embeddings read.</summary>
        [Property] public string TextSimple      { get; set; } = "";
        [Property] public string EnglishQarai    { get; set; } = "";
        [Property] public string EnglishSaheeh   { get; set; } = "";
        [Property] public string Transliteration { get; set; } = "";
        [Property] public int    Juz             { get; set; }
        [Property] public int    Hizb            { get; set; }
        [Property] public int    HizbQuarter     { get; set; }
        [Property] public int    Manzil          { get; set; }
        [Property] public int    Ruku            { get; set; }
        [Property] public int    Page            { get; set; }
        [Property] public string SajdaType       { get; set; } = "";
        [Property] public string RevelationPlace { get; set; } = "";
    }

    /// <summary>One word occurrence. Key: "surah:ayah:position" (the Quranic Arabic Corpus numbering).</summary>
    [Node]
    internal sealed class Word
    {
        [Key]      public string WordKey      { get; set; } = "";
        [Property] public string VerseKey     { get; set; } = "";
        [Property] public int    SurahNumber  { get; set; }
        [Property] public int    AyahNumber   { get; set; }
        [Property] public int    Position     { get; set; }
        [Property] public string TextArabic   { get; set; } = "";
        /// <summary>The form without diacritics — distinct words are counted on this.</summary>
        [Property] public string TextPlain    { get; set; } = "";
        /// <summary>Prefix + stem + suffix, e.g. "وَ + إِيَّاكَ".</summary>
        [Property] public string Segments     { get; set; } = "";
        [Property] public string SegmentTags  { get; set; } = "";
        [Property] public string RootLetters  { get; set; } = "";
        [Property] public string LemmaText    { get; set; } = "";
        /// <summary>N, V or P (noun, verb, particle) — the stem's coarse tag.</summary>
        [Property] public string PosTag       { get; set; } = "";
        [Property] public string PartOfSpeech { get; set; } = "";
        [Property] public string Features     { get; set; } = "";
        [Property] public int    VerbForm     { get; set; }
    }

    /// <summary>A triliteral (or quadriliteral) root. Key: its letters, e.g. "رحم".</summary>
    [Node]
    internal sealed class Root
    {
        [Key]      public string RootLetters { get; set; } = "";
        /// <summary>The letters spaced for a reader: "ر ح م".</summary>
        [Property] public string Spaced      { get; set; } = "";
    }

    /// <summary>A dictionary form. Key: lemma|root|tag, because 13 spellings belong to two different words.</summary>
    [Node]
    internal sealed class Lemma
    {
        [Key]      public string LemmaKey     { get; set; } = "";
        [Property] public string LemmaText    { get; set; } = "";
        [Property] public string LemmaPlain   { get; set; } = "";
        [Property] public string RootLetters  { get; set; } = "";
        [Property] public string PosTag       { get; set; } = "";
        [Property] public string PartOfSpeech { get; set; } = "";
    }

    /// <summary>One of the 30 parts. Key: "1"–"30".</summary>
    [Node]
    internal sealed class Juz
    {
        [Key]      public string JuzId         { get; set; } = "";
        [Property] public int    Number        { get; set; }
        [Property] public string FirstVerseKey { get; set; } = "";
    }

    /// <summary>Where a surah was revealed. Key: "Meccan" or "Medinan".</summary>
    [Node]
    internal sealed class Revelation
    {
        [Key] public string PlaceName { get; set; } = "";
    }

    /// <summary>A QUL topic. Content for reading and filtering — never a similarity or clustering signal.</summary>
    [Node]
    internal sealed class Topic
    {
        [Key]      public string TopicId         { get; set; } = "";
        [Property] public string TopicName       { get; set; } = "";
        [Property] public string TopicNameArabic { get; set; } = "";
        [Property] public string TopicFamily     { get; set; } = "";
        [Property] public string ParentTopicId   { get; set; } = "";
        [Property] public string Description     { get; set; } = "";
    }

    /// <summary>
    /// Two verses Ibn Kathir relates (QurSim). THE ANSWER KEY: scored against, never read by a
    /// similarity signal, a clustering run or anything the AI can reach.
    /// </summary>
    [Node]
    internal sealed class QurSimPair
    {
        [Key]      public string PairKey { get; set; } = "";
        [Property] public string VerseA  { get; set; } = "";
        [Property] public string VerseB  { get; set; } = "";
        /// <summary>0 = not obvious, 1 = related, 2 = strongly related.</summary>
        [Property] public int    Degree  { get; set; }
    }

    /// <summary>
    /// A group of verses found by clustering. Written by the qkg/clusters/build endpoint inside the
    /// workspace, never by this connector, which only registers the type so the schema and N.cs are
    /// complete from the start. Two kinds:
    ///   Passage  a run of consecutive verses of one surah — the surah's sections;
    ///   Theme    passages of several surahs that tell the same story or treat the same subject.
    /// Key: "P:{surah}:{first ayah}-{last ayah}" for a passage, "T:{hash of its passages' keys}" for
    /// a theme, so a rebuild that finds the same groups keeps the same nodes.
    /// </summary>
    [Node]
    internal sealed class Cluster
    {
        [Key]      public string ClusterKey    { get; set; } = "";
        /// <summary>"Passage" or "Theme".</summary>
        [Property] public string Kind          { get; set; } = "";
        /// <summary>"Quran" or "Surah N".</summary>
        [Property] public string Scope         { get; set; } = "";
        [Property] public string Label         { get; set; } = "";
        [Property] public string LabelArabic   { get; set; } = "";
        [Property] public string Description   { get; set; } = "";
        [Property] public string Method        { get; set; } = "";
        [Property] public string RunId         { get; set; } = "";
        /// <summary>A passage's surah; 0 for a theme.</summary>
        [Property] public int    SurahNumber   { get; set; }
        /// <summary>A passage's place in its surah (1, 2 …); a theme's rank by the surahs it spans.</summary>
        [Property] public int    Ordinal       { get; set; }
        /// <summary>A passage's first and last verse ("7:103"); empty for a theme.</summary>
        [Property] public string FirstVerseKey { get; set; } = "";
        [Property] public string LastVerseKey  { get; set; } = "";
    }
}
