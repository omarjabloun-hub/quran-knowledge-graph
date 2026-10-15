[endpoint: Curiosity.Endpoints.Path("qkg/_lib/dto")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

// The shared library every qkg/* endpoint imports (an ImportEndpoint comment naming this path).
// AdminOnly so it fails closed: it is a library, not an API. Never write that comment in THIS
// file: the import directive is matched anywhere in the code, and the library would import itself.
//
// Every class here is declared a SECOND time, identically, in
// src/QuranKG.FrontEnd/src/Api/Dto.cs, which `just sync-dto` GENERATES from this file (up to the
// "shared reads" helpers). Both ends serialise with the same default resolver, so the two
// declarations are the wire format: change this file, then re-run `just sync-dto`.
//
// DTOs are flat (Transpose has no runtime reflection): primitives, UID128 and arrays only.
// Every count ships the UID of what it counted, so a chart bar opens the node it measured.

public static class QK
{
    public const string MECCAN  = "Meccan";
    public const string MEDINAN = "Medinan";

    /// <summary>The two kinds of Cluster the clusters build writes (Cluster.Kind).</summary>
    public const string PASSAGE = "Passage";
    public const string THEME   = "Theme";
    /// <summary>The Clusters qkg/clusters/similar-build writes: passages grouped by meaning, lemmas and roots.</summary>
    public const string SIMILAR_GROUP = "SimilarGroup";

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
        public UID128    Uid               { get; set; }
        public int       Verses            { get; set; }
        public int       Words             { get; set; }
        public int       DistinctRoots     { get; set; }
        public int       DistinctLemmas    { get; set; }
        public int       Nouns             { get; set; }
        public int       Verbs             { get; set; }
        public int       Particles         { get; set; }
        /// <summary>Words per verse, in ayah order.</summary>
        public int[]     VerseLengths      { get; set; }
        public UID128[]  VerseUIDs         { get; set; }
        /// <summary>The Uthmani text of each verse, in ayah order — the surah as it is read.</summary>
        public string[]  VerseTexts        { get; set; }
        /// <summary>Saheeh International, in ayah order, for readers who need a gloss.</summary>
        public string[]  VerseTranslations { get; set; }
        public Counted[] TopRoots          { get; set; }
        /// <summary>Roots this surah uses far more than the rest of the Quran (tf-idf over surahs).</summary>
        public Counted[] DistinctiveRoots  { get; set; }
        public Counted[] TopLemmas         { get; set; }
    }

    /// <summary>One word of a verse, with the morphology the Quranic Arabic Corpus gives it.</summary>
    public class WordRow
    {
        public UID128 Uid          { get; set; }
        public int    Position     { get; set; }
        /// <summary>The word as written in the Uthmani text, with its diacritics.</summary>
        public string Text         { get; set; }
        /// <summary>The morphological segments, "ٱلْ + حَمْدُ".</summary>
        public string Segments     { get; set; }
        /// <summary>The root's letters, spaced ("ح م د"); empty for words without one.</summary>
        public string Root         { get; set; }
        public UID128 RootUid      { get; set; }
        public string Lemma        { get; set; }
        public UID128 LemmaUid     { get; set; }
        public string PartOfSpeech { get; set; }
        /// <summary>The stem's features, "M|NOM" (masculine, nominative).</summary>
        public string Features     { get; set; }
    }

    /// <summary>A verse Ibn Kathir's tafsir relates to another (QurSim), with the grade it was given.</summary>
    public class RelatedVerse
    {
        public UID128 Uid         { get; set; }
        public string VerseKey    { get; set; }
        /// <summary>2 = strongly related, 1 = related, 0 = the relation is not obvious.</summary>
        public int    Degree      { get; set; }
        public string Grade       { get; set; }
        public string Text        { get; set; }
        public string Translation { get; set; }
    }

    public class VerseWordsDto
    {
        public UID128         Uid           { get; set; }
        public UID128         SurahUID      { get; set; }
        /// <summary>The verse's words in reading order.</summary>
        public WordRow[]      Words         { get; set; }
        public UID128[]       RootUIDs      { get; set; }
        public UID128[]       LemmaUIDs     { get; set; }
        /// <summary>
        /// Verses Ibn Kathir relates to this one (QurSim). The ANSWER KEY: shown to people on the
        /// verse page, never to a similarity signal or an AI tool.
        /// </summary>
        public RelatedVerse[] TafsirRelated { get; set; }
    }

    /// <summary>qkg/verse/tafsir: a verse, and the tafsir work to read on it ("mukhtasar" when empty).</summary>
    public class VerseTafsirRequest
    {
        public UID128 Uid  { get; set; }
        public string Work { get; set; }
    }

    /// <summary>
    /// What the tafsirs say on one verse: every work, and the chosen one's text in Arabic and in
    /// English. For people only — no AI tool calls qkg/verse/tafsir (Ibn Kathir's text is where
    /// QurSim, the similarity answer key, was drawn from).
    /// </summary>
    public class VerseTafsirDto
    {
        public UID128       Uid      { get; set; }
        public string       VerseKey { get; set; }
        /// <summary>Every work, in the order a reader is offered them; only <see cref="Work"/> carries its text.</summary>
        public TafsirWork[] Works    { get; set; }
        /// <summary>The work whose text is in <see cref="Works"/>.</summary>
        public string       Work     { get; set; }
    }

    public class TafsirWork
    {
        /// <summary>"mukhtasar", "jalalayn", "ibn-kathir".</summary>
        public string     Work        { get; set; }
        public string     Name        { get; set; }
        public string     NameArabic  { get; set; }
        public string     Author      { get; set; }
        public string     Description { get; set; }
        /// <summary>Null when the work says nothing on this verse in that language (al-Jalalayn passes over some verses).</summary>
        public TafsirText Arabic      { get; set; }
        public TafsirText English     { get; set; }
    }

    /// <summary>One edition's commentary covering the verse — on it alone, or on a run of verses with it.</summary>
    public class TafsirText
    {
        /// <summary>The TafsirEntry.</summary>
        public UID128 Uid           { get; set; }
        /// <summary>The Tafsir (edition) it belongs to.</summary>
        public UID128 TafsirUid     { get; set; }
        public string Language      { get; set; }
        public string FirstVerseKey { get; set; }
        public string LastVerseKey  { get; set; }
        public int    FirstAyah     { get; set; }
        public int    LastAyah      { get; set; }
        /// <summary>Plain text, paragraphs separated by a blank line; empty unless this is the requested work.</summary>
        public string Text          { get; set; }
        public string Attribution   { get; set; }
    }

    /// <summary>One candidate verse and why it was ranked: each signal's standardised contribution.</summary>
    public class SimilarVerse
    {
        public UID128   Uid         { get; set; }
        public string   VerseKey    { get; set; }
        /// <summary>The Uthmani text, so a list of candidates reads without a second call.</summary>
        public string   Text        { get; set; }
        public string   Translation { get; set; }
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

    /// <summary>One candidate surah and why it was ranked.</summary>
    public class SimilarSurah
    {
        public UID128    Uid          { get; set; }
        public int       Number       { get; set; }
        public string    NameArabic   { get; set; }
        public string    Name         { get; set; }
        public string    NameEnglish  { get; set; }
        public string    Place        { get; set; }
        public double    Score        { get; set; }
        /// <summary>Cosine of the two surahs' rarity-weighted lemmas, standardised over the candidates.</summary>
        public double    Lemmas       { get; set; }
        /// <summary>The same over roots, at half weight.</summary>
        public double    Roots        { get; set; }
        /// <summary>The meaning signal: how many of the seed's verses have a verse of this surah among their
        /// nearest by the Arabic embedding (CloseVerses.Length), standardised over the candidates, at its weight.</summary>
        public double    Meaning      { get; set; }
        /// <summary>The distinctive lemmas the two surahs share, the most telling first.</summary>
        public Counted[] SharedLemmas { get; set; }
        /// <summary>Every pair the meaning signal counted — a verse of the seed, one of its nearest verses here — the closest first.</summary>
        public VersePair[] CloseVerses { get; set; }
        /// <summary>Themes both surahs have a passage in — shown as a reason, not counted in the score.</summary>
        public Counted[] SharedThemes { get; set; }
    }

    /// <summary>Two verses of different surahs whose Arabic the embedding reads as close.</summary>
    public class VersePair
    {
        public UID128 SeedUid      { get; set; }
        public string SeedVerseKey { get; set; }
        public UID128 Uid          { get; set; }
        public string VerseKey     { get; set; }
        /// <summary>Cosine of the two verses' embeddings.</summary>
        public double Similarity   { get; set; }
    }

    public class SimilarSurahsDto
    {
        public UID128         Uid    { get; set; }
        public string         Method { get; set; }
        public SimilarSurah[] Surahs { get; set; }
    }

    /// <summary>A passage: a run of consecutive verses of one surah, found by the clusters build.</summary>
    public class PassageRow
    {
        public UID128 Uid                { get; set; }
        public UID128 SurahUid           { get; set; }
        public int    SurahNumber        { get; set; }
        public string SurahName          { get; set; }
        /// <summary>Its place in the surah: 1, 2 …</summary>
        public int    Ordinal            { get; set; }
        public string FirstVerseKey      { get; set; }
        public string LastVerseKey       { get; set; }
        public UID128 FirstVerseUid      { get; set; }
        public int    Verses             { get; set; }
        public int    Words              { get; set; }
        public string Label              { get; set; }
        public string LabelArabic        { get; set; }
        /// <summary>The opening verse in the Uthmani text, and its Saheeh International gloss.</summary>
        public string OpeningText        { get; set; }
        public string OpeningTranslation { get; set; }
        /// <summary>The theme the passage belongs to; default when it has none.</summary>
        public UID128 ThemeUid           { get; set; }
        public string ThemeLabel         { get; set; }
        public string ThemeLabelArabic   { get; set; }
        /// <summary>How many surahs that theme spans.</summary>
        public int    ThemeSurahs        { get; set; }
    }

    public class SurahPassagesDto
    {
        public UID128       Uid      { get; set; }
        /// <summary>False until the clusters build has run (`just clusters`).</summary>
        public bool         Built    { get; set; }
        public string       Method   { get; set; }
        public string       RunId    { get; set; }
        public PassageRow[] Passages { get; set; }
    }

    /// <summary>A theme: passages of several surahs that tell the same story or treat the same subject.</summary>
    public class ThemeRow
    {
        public UID128   Uid          { get; set; }
        public int      Ordinal      { get; set; }
        public string   Label        { get; set; }
        public string   LabelArabic  { get; set; }
        public int      Passages     { get; set; }
        public int      Verses       { get; set; }
        /// <summary>The surahs it spans, in mushaf order.</summary>
        public UID128[] SurahUIDs    { get; set; }
        public int[]    SurahNumbers { get; set; }
        public string[] SurahNames   { get; set; }
    }

    public class ThemesDto
    {
        /// <summary>False until the clusters build has run (`just clusters`).</summary>
        public bool       Built          { get; set; }
        public string     Method         { get; set; }
        public string     RunId          { get; set; }
        /// <summary>Passages over the whole Quran, and how many verses there are and how many fall in a theme.</summary>
        public int        Passages       { get; set; }
        public int        Verses         { get; set; }
        public int        VersesInThemes { get; set; }
        public ThemeRow[] Themes         { get; set; }
    }

    public class VerseLine
    {
        public UID128 Uid         { get; set; }
        public string VerseKey    { get; set; }
        public string Text        { get; set; }
        public string Translation { get; set; }
    }

    /// <summary>One cluster: a theme with its passages, or a passage with its verses.</summary>
    public class ClusterProfileDto
    {
        public UID128       Uid         { get; set; }
        public string       Kind        { get; set; }
        public string       Label       { get; set; }
        public string       LabelArabic { get; set; }
        public string       Method      { get; set; }
        public string       RunId       { get; set; }
        /// <summary>The lemmas and roots its verses use far more than the rest of the Quran.</summary>
        public Counted[]    KeyLemmas   { get; set; }
        public Counted[]    KeyRoots    { get; set; }
        /// <summary>A theme: its passages in mushaf order. A passage: itself, with its theme.</summary>
        public PassageRow[] Passages    { get; set; }
        /// <summary>A passage: its verses in order. A theme: empty — each passage opens its own.</summary>
        public VerseLine[]  Verses      { get; set; }
        /// <summary>A passage: the passages before and after it in its surah (default at either end).</summary>
        public UID128       Previous    { get; set; }
        public UID128       Next        { get; set; }
    }

    // --- reading ------------------------------------------------------------------------------

    /// <summary>
    /// Text to read: a surah, a passage or a theme, as runs of consecutive verses — a surah's
    /// passages, a theme's passages surah by surah — each with what the clusters build says of it.
    /// </summary>
    public class ReadingDto
    {
        /// <summary>The Surah or Cluster read.</summary>
        public UID128           Uid           { get; set; }
        /// <summary>"Surah", Passage or Theme.</summary>
        public string           Kind          { get; set; }
        public string           Label         { get; set; }
        public string           LabelArabic   { get; set; }
        /// <summary>False until the clusters build has run: a surah is then one section with no passage.</summary>
        public bool             PassagesBuilt { get; set; }
        public string           Method        { get; set; }
        public string           RunId         { get; set; }
        /// <summary>A surah: the surahs either side. A passage: the passages either side in its surah. Default at either end.</summary>
        public UID128           Previous      { get; set; }
        public UID128           Next          { get; set; }
        /// <summary>A passage or a theme: the lemmas its verses use far more than the rest of the Quran — the words highlighted.</summary>
        public Counted[]        KeyLemmas     { get; set; }
        public ReadingSection[] Sections      { get; set; }
    }

    /// <summary>A run of consecutive verses of one surah: a passage, or the whole surah before the build.</summary>
    public class ReadingSection
    {
        /// <summary>Default when the clusters have not been built.</summary>
        public UID128         PassageUid       { get; set; }
        public int            Ordinal          { get; set; }
        public string         Label            { get; set; }
        public string         LabelArabic      { get; set; }
        public UID128         SurahUid         { get; set; }
        public int            SurahNumber      { get; set; }
        public string         SurahName        { get; set; }
        public string         SurahNameArabic  { get; set; }
        public UID128         ThemeUid         { get; set; }
        public string         ThemeLabel       { get; set; }
        public string         ThemeLabelArabic { get; set; }
        public int            ThemeSurahs      { get; set; }
        /// <summary>The theme's Ordinal on the Themes page — its colour on the map and in the reader; 0 without a theme.</summary>
        public int            ThemeOrdinal     { get; set; }
        /// <summary>The similarity group (qkg/clusters/similar-build) the passage is in; default without one.</summary>
        public UID128         GroupUid         { get; set; }
        public string         GroupLabelArabic { get; set; }
        public int            GroupOrdinal     { get; set; }
        public int            GroupSurahs      { get; set; }
        public ReadingVerse[] Verses           { get; set; }
    }

    public class ReadingVerse
    {
        public UID128 Uid         { get; set; }
        public string VerseKey    { get; set; }
        public int    Ayah        { get; set; }
        /// <summary>The Uthmani text, and its Saheeh International gloss.</summary>
        public string Text        { get; set; }
        public string Translation { get; set; }
        /// <summary>Its words in the morphology: the text's words line up with them when the counts agree.</summary>
        public int    Words       { get; set; }
        /// <summary>The positions (1-based) of its words whose lemma is one of the key lemmas — of the passage it is read in, or of the theme.</summary>
        public int[]  Highlights  { get; set; }
        /// <summary>"recommended" / "obligatory" on a verse of prostration, else empty.</summary>
        public string Sajda       { get; set; }
    }

    /// <summary>
    /// Every passage of the Quran, surah by surah, with the theme and the similarity group it is in:
    /// both groupings of the passages at a glance.
    /// </summary>
    public class ClusterMapDto
    {
        public bool         Built        { get; set; }
        public string       Method       { get; set; }
        public string       RunId        { get; set; }
        /// <summary>The similarity groups' build (qkg/clusters/similar-build), empty until it has run.</summary>
        public string       GroupsMethod { get; set; }
        public string       GroupsRunId  { get; set; }
        public MapSurah[]   Surahs       { get; set; }
        /// <summary>In mushaf order.</summary>
        public MapPassage[] Passages     { get; set; }
        public MapGroup[]   Themes       { get; set; }
        public MapGroup[]   Groups       { get; set; }
    }

    /// <summary>A theme or a similarity group: its passages counted, and the surahs it spans.</summary>
    public class MapGroup
    {
        public UID128 Uid          { get; set; }
        public string Kind         { get; set; }
        public int    Ordinal      { get; set; }
        public string Label        { get; set; }
        public string LabelArabic  { get; set; }
        public int    Passages     { get; set; }
        public int    Verses       { get; set; }
        public int[]  SurahNumbers { get; set; }
    }

    public class MapSurah
    {
        public UID128 Uid        { get; set; }
        public int    Number     { get; set; }
        public string Name       { get; set; }
        public string NameArabic { get; set; }
        public string Place      { get; set; }
        public int    Words      { get; set; }
    }

    public class MapPassage
    {
        public UID128 Uid          { get; set; }
        public int    SurahNumber  { get; set; }
        public int    Ordinal      { get; set; }
        public int    FirstAyah    { get; set; }
        public int    LastAyah     { get; set; }
        public int    Words        { get; set; }
        public string LabelArabic  { get; set; }
        public string Label        { get; set; }
        /// <summary>Its theme's Ordinal on the Themes page (0 when the passage has no theme), and its UID.</summary>
        public int    ThemeOrdinal { get; set; }
        public UID128 ThemeUid     { get; set; }
        /// <summary>The same for its similarity group.</summary>
        public int    GroupOrdinal { get; set; }
        public UID128 GroupUid     { get; set; }
    }

    // --- hadith (qkg/hadith/*, qkg/verse/hadith) ---------------------------------------------

    /// <summary>qkg/hadith/profile: a hadith; the two weights only to measure them (tools/hadith-benchmark), null for the defaults.</summary>
    public class HadithProfileRequest
    {
        public UID128  Uid      { get; set; }
        public double? Semantic { get; set; }
        public double? Lexical  { get; set; }
    }

    /// <summary>A hadith in a list: how it is cited, where it is, the opening of its text.</summary>
    public class HadithRow
    {
        public UID128   Uid          { get; set; }
        public string   Reference    { get; set; }
        public string   CollectionId { get; set; }
        public string   BookName     { get; set; }
        /// <summary>The text without its chain, cut to a few lines.</summary>
        public string   Matn         { get; set; }
        public string   English      { get; set; }
        /// <summary>"quotation", "phrase", "translator", or "" when it is listed for its meaning.</summary>
        public string   Kind         { get; set; }
        /// <summary>The verses it quotes, "2:255".</summary>
        public string[] VerseKeys    { get; set; }
        /// <summary>For a hadith listed by meaning: the cosine of its text and the verse's.</summary>
        public double   Score        { get; set; }
    }

    /// <summary>Where a hadith's words meet a verse.</summary>
    public class HadithQuoteRow
    {
        public UID128 Uid         { get; set; }
        public UID128 VerseUid    { get; set; }
        public string VerseKey    { get; set; }
        public string Kind        { get; set; }
        public int    Words       { get; set; }
        public int    VerseWords  { get; set; }
        public string Quoted      { get; set; }
        public string VerseText   { get; set; }
        public string Translation { get; set; }
    }

    /// <summary>A verse close to a hadith: meaning (embeddings) and shared rare words, each standardised.</summary>
    public class HadithVerseRow
    {
        public UID128 Uid         { get; set; }
        public string VerseKey    { get; set; }
        public string Text        { get; set; }
        public string Translation { get; set; }
        public double Score       { get; set; }
        public double Semantic    { get; set; }
        public double Lexical     { get; set; }
        /// <summary>The words they share, in the matching spelling.</summary>
        public string[] Shared    { get; set; }
        public bool   Quoted      { get; set; }
    }

    public class HadithProfileDto
    {
        public UID128           Uid                  { get; set; }
        public string           Key                  { get; set; }
        public string           Reference            { get; set; }
        public UID128           CollectionUid        { get; set; }
        public string           CollectionId         { get; set; }
        public string           CollectionName       { get; set; }
        public string           CollectionNameArabic { get; set; }
        public UID128           BookUid              { get; set; }
        public int              BookNumber           { get; set; }
        public string           BookName             { get; set; }
        public string           BookNameArabic       { get; set; }
        public int              NumberInBook         { get; set; }
        public string           TextArabic           { get; set; }
        public string           Matn                 { get; set; }
        public string           TextEnglish          { get; set; }
        public bool             IsVariant            { get; set; }
        public string           TranslatorCites      { get; set; }
        public HadithQuoteRow[] Quotes               { get; set; }
        public HadithVerseRow[] Closest              { get; set; }
        /// <summary>False until the hadith's text has been embedded: Closest is then by shared words only.</summary>
        public bool             SemanticReady        { get; set; }
        /// <summary>The hadith before and after it in its book.</summary>
        public UID128           Previous             { get; set; }
        public UID128           Next                 { get; set; }
    }

    public class VerseHadithDto
    {
        public UID128      Uid           { get; set; }
        public string      VerseKey      { get; set; }
        /// <summary>The hadith whose Arabic quotes the verse or shares a phrase with it, and those whose translator cites it.</summary>
        public HadithRow[] Quoting       { get; set; }
        /// <summary>The hadith closest to the verse in meaning, variants and quoting hadith left out.</summary>
        public HadithRow[] Closest       { get; set; }
        public bool        SemanticReady { get; set; }
    }

    public class HadithCollectionStats
    {
        public UID128 Uid             { get; set; }
        public string Id              { get; set; }
        public string Name            { get; set; }
        public string NameArabic      { get; set; }
        public int    Hadith          { get; set; }
        /// <summary>Hadith that quote a verse (a quotation).</summary>
        public int    Quoting         { get; set; }
        /// <summary>Hadith that only share a phrase with a verse.</summary>
        public int    Phrase          { get; set; }
        public int    Variants        { get; set; }
        /// <summary>Hadith whose English translator cites a verse, and of those, the ones whose Arabic quotes it — the detector's measured recall.</summary>
        public int    TranslatorCited { get; set; }
        public int    TranslatorFound { get; set; }
        /// <summary>Hadith whose text has been embedded.</summary>
        public int    Embedded        { get; set; }
    }

    public class HadithBookStats
    {
        public UID128 Uid          { get; set; }
        public string CollectionId { get; set; }
        public int    Number       { get; set; }
        public string Name         { get; set; }
        public string NameArabic   { get; set; }
        public int    Hadith       { get; set; }
        public int    Quoting      { get; set; }
    }

    public class HadithOverviewDto
    {
        public HadithCollectionStats[] Collections { get; set; }
        /// <summary>The verses quoted by the most hadith (quotations and phrases).</summary>
        public Counted[]               TopVerses   { get; set; }
        /// <summary>Every surah, with how many hadith quote one of its verses.</summary>
        public Counted[]               Surahs      { get; set; }
        public HadithBookStats[]       Books       { get; set; }
        public double                  Milliseconds { get; set; }
    }

    /// <summary>qkg/hadith/list: hadith that quote the Quran, or that match a search.</summary>
    public class HadithListRequest
    {
        /// <summary>"bukhari", "muslim" or empty for both.</summary>
        public string Collection { get; set; }
        public UID128 BookUid    { get; set; }
        /// <summary>A surah: only hadith quoting one of its verses; 0 for any.</summary>
        public int    Surah      { get; set; }
        /// <summary>"quotation", "phrase", "any" (both), "all" (every hadith, quoting or not).</summary>
        public string Kind       { get; set; }
        /// <summary>Words to find in the Arabic (any spelling) or the English, or a reference ("Bukhari 6018").</summary>
        public string Text       { get; set; }
        public int    Skip       { get; set; }
        public int    Take       { get; set; }
    }

    public class HadithListDto
    {
        public int         Total { get; set; }
        public HadithRow[] Rows  { get; set; }
    }

    // --- shared reads -------------------------------------------------------------------------

    /// <summary>The Lemma node key the connector writes: lemma|root|coarse tag.</summary>
    public static string LemmaKey(string lemma, string root, string posTag) => lemma + "|" + (root ?? "") + "|" + posTag;

    /// <summary>
    /// The nouns that carry meaning. The Corpus tags as nouns (N) a set of function words too —
    /// pronouns, إِذَا and قَبْل (time and location adverbs), مَن and مَا asking or conditioning —
    /// which would otherwise crowd every "most frequent nouns" list.
    /// </summary>
    public static bool IsContentNoun(string partOfSpeech)
        => !FunctionNouns.Contains(partOfSpeech ?? "");

    private static readonly HashSet<string> FunctionNouns = new HashSet<string>(StringComparer.Ordinal)
    {
        "Pronoun", "Relative pronoun", "Demonstrative pronoun", "Time adverb", "Location adverb", "Interrogative noun", "Conditional noun",
    };

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

    /// <summary>
    /// The words of an Arabic text in the spelling a hadith and a verse are compared in — the same
    /// rule as the corpus's quotation detector (QuranKG.Dataset Arabic.MatchTokens): no diacritics,
    /// every alef form as ا, ى as ي, ة as ه, ؤ as و, ئ as ي.
    /// </summary>
    public static List<string> MatchTokens(string text)
    {
        var sb = new System.Text.StringBuilder((text ?? "").Length);
        foreach (var c in text ?? "")
        {
            if ((c >= '\u0610' && c <= '\u061A') || (c >= '\u064B' && c <= '\u065F') || c == '\u0670' || (c >= '\u06D6' && c <= '\u06ED') || c == '\u0640' || (c >= '\u200C' && c <= '\u200F')) continue;
            var d = c == 'أ' || c == 'إ' || c == 'آ' || c == 'ٱ' ? 'ا' : c == 'ى' ? 'ي' : c == 'ة' ? 'ه' : c == 'ؤ' ? 'و' : c == 'ئ' ? 'ي' : c;
            sb.Append(d >= '\u0621' && d <= '\u064A' ? d : ' ');
        }
        return sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static readonly string[] StemSuffixes = { "هما", "كما", "هم", "هن", "كم", "كن", "نا", "ها", "ه", "ك", "ي" };

    /// <summary>
    /// A word without و/ف, the article and an attached pronoun, so جاره and الجار count as one —
    /// for counting shared words. The same rule as QuranKG.Dataset Arabic.Stem, which writes
    /// Hadith.Keywords: change both.
    /// </summary>
    public static string Stem(string word)
    {
        var w = word ?? "";
        if (w == "الله" || w == "لله" || w == "بالله" || w == "والله" || w == "فالله" || w == "تالله") return "الله";
        if (w.Length > 3 && (w[0] == 'و' || w[0] == 'ف')) w = w.Substring(1);
        if (w.Length > 4 && (w.StartsWith("بال", StringComparison.Ordinal) || w.StartsWith("كال", StringComparison.Ordinal))) w = w.Substring(3);
        else if (w.Length > 3 && w.StartsWith("ال", StringComparison.Ordinal)) w = w.Substring(2);
        else if (w.Length > 4 && w.StartsWith("لل", StringComparison.Ordinal)) w = w.Substring(2);
        foreach (var suffix in StemSuffixes)
            if (w.Length - suffix.Length >= 3 && w.EndsWith(suffix, StringComparison.Ordinal)) { w = w.Substring(0, w.Length - suffix.Length); break; }
        return w;
    }

    /// <summary>"Muslim 8a", "bukhari 6018", "B6018" → the collection id and the cited number, or (null, null).</summary>
    public static (string Collection, string Number) ParseReference(string text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant().Replace("sahih", "").Replace("al-", "").Replace("#", " ").Trim();
        string collection = t.StartsWith("bukhari") || t.StartsWith("b ") || (t.StartsWith("b") && t.Length > 1 && char.IsDigit(t[1])) ? "bukhari"
                          : t.StartsWith("muslim")  || t.StartsWith("m ") || (t.StartsWith("m") && t.Length > 1 && char.IsDigit(t[1])) ? "muslim" : null;
        if (collection is null) return (null, null);
        var number = new string(t.SkipWhile(ch => !char.IsDigit(ch)).TakeWhile(ch => char.IsLetterOrDigit(ch) || ch == '.').ToArray());
        return number.Length == 0 ? (null, null) : (collection, number);
    }
}
