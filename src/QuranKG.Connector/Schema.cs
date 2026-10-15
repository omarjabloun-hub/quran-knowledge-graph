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
    /// A tafsir in one language — an edition: al-Mukhtasar, al-Jalalayn or Ibn Kathir, in Arabic or
    /// English. Key: "mukhtasar-ar", "ibn-kathir-en" …. Read by people; never by a signal or the AI
    /// (Ibn Kathir's is the text QurSim was drawn from).
    /// </summary>
    [Node]
    internal sealed class Tafsir
    {
        [Key]      public string TafsirId     { get; set; } = "";
        /// <summary>The work its editions share: "mukhtasar", "jalalayn", "ibn-kathir".</summary>
        [Property] public string Work         { get; set; } = "";
        /// <summary>"ar" or "en".</summary>
        [Property] public string Language     { get; set; } = "";
        [Property] public string TafsirName   { get; set; } = "";
        [Property] public string TafsirNameArabic { get; set; } = "";
        [Property] public string Author       { get; set; } = "";
        [Property] public string Description  { get; set; } = "";
        [Property] public string Attribution  { get; set; } = "";
        /// <summary>Where a reader is offered it first: 1 = al-Mukhtasar.</summary>
        [Property] public int    Rank         { get; set; }
    }

    /// <summary>
    /// One commentary of a tafsir, on a verse or on the run of verses it explains together (Ibn
    /// Kathir explains up to 20 at a time). Key: "{tafsir}:{surah}:{first}" or "…:{first}-{last}".
    /// </summary>
    [Node]
    internal sealed class TafsirEntry
    {
        [Key]      public string EntryKey      { get; set; } = "";
        [Property] public string TafsirId      { get; set; } = "";
        [Property] public string Language      { get; set; } = "";
        [Property] public int    SurahNumber   { get; set; }
        [Property] public string FirstVerseKey { get; set; } = "";
        [Property] public string LastVerseKey  { get; set; } = "";
        [Property] public int    FirstAyah     { get; set; }
        [Property] public int    LastAyah      { get; set; }
        /// <summary>Plain text, paragraphs separated by a blank line.</summary>
        [Property] public string Text          { get; set; } = "";
    }

    /// <summary>A hadith collection. Key: "bukhari", "muslim".</summary>
    [Node]
    internal sealed class HadithCollection
    {
        [Key]      public string CollectionId   { get; set; } = "";
        [Property] public string CollectionName { get; set; } = "";
        [Property] public string CollectionNameArabic { get; set; } = "";
        [Property] public string Compiler       { get; set; } = "";
        [Property] public string Translator     { get; set; } = "";
    }

    /// <summary>A book (kitab) of a collection. Key: "bukhari:65" (65 is Bukhari's Book of Tafsir).</summary>
    [Node]
    internal sealed class HadithBook
    {
        [Key]      public string BookKey        { get; set; } = "";
        [Property] public string CollectionId   { get; set; } = "";
        [Property] public int    BookNumber     { get; set; }
        [Property] public string BookName       { get; set; } = "";
        [Property] public string BookNameArabic { get; set; } = "";
    }

    /// <summary>
    /// One hadith. Key: "bukhari:6018"; <c>Reference</c> is how it is cited ("Bukhari 6018",
    /// "Muslim 8a"). The Arabic is the text — <c>Matn</c>, without the chain of narrators, is what
    /// the embeddings and the assistant read; the English is a reading aid.
    /// </summary>
    [Node]
    internal sealed class Hadith
    {
        [Key]      public string HadithKey     { get; set; } = "";
        [Property] public string CollectionId  { get; set; } = "";
        [Property] public string Number        { get; set; } = "";
        [Property] public int    Sequence      { get; set; }
        [Property] public string Reference     { get; set; } = "";
        [Property] public int    BookNumber    { get; set; }
        [Property] public string BookName      { get; set; } = "";
        [Property] public int    NumberInBook  { get; set; }
        [Property] public string TextArabic    { get; set; } = "";
        [Property] public string Matn          { get; set; } = "";
        /// <summary>Only a chain pointing to the hadith before it ("بهذا الإسناد مثله").</summary>
        [Property] public bool   IsVariant     { get; set; }
        [Property] public string TextEnglish   { get; set; } = "";
        /// <summary>The verses the English translator cites in brackets, "15:90, 2:196" — his note.</summary>
        [Property] public string TranslatorCites { get; set; } = "";
        /// <summary>Its distinctive words, stemmed, most distinctive first, space-separated: what shared words with a verse are counted on.</summary>
        [Property] public string Keywords      { get; set; } = "";
    }

    /// <summary>
    /// Where a hadith's words meet a verse. Kind: "quotation" (six words or more, or a whole short
    /// verse), "phrase" (four or five), "translator" (only the English translator cites it).
    /// Key: "bukhari:4500>15:90".
    /// </summary>
    [Node]
    internal sealed class QuranQuote
    {
        [Key]      public string QuoteKey   { get; set; } = "";
        [Property] public string HadithKey  { get; set; } = "";
        [Property] public string VerseKey   { get; set; } = "";
        [Property] public string Kind       { get; set; } = "";
        [Property] public int    Words      { get; set; }
        [Property] public int    VerseWords { get; set; }
        [Property] public string Quoted     { get; set; } = "";
        /// <summary>The ingest that wrote it (UTC, yyyyMMddHHmmss): qkg/hadith/prune-quotes deletes the links an older run found and this one did not.</summary>
        [Property] public string RunId      { get; set; } = "";
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
