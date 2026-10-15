// Every edge, in BOTH directions, one meaning each — registered in one call
// (CreateEdgeSchemaAsync(typeof(Edges))) and emitted as E.* for the endpoints and the front end.
//
// Location is linked at EVERY level (a word to its verse AND its surah; a verse to its surah, juz
// and revelation place) because a filter or a shared-neighbour signal only sees direct neighbours.

namespace QuranKG.Connector;

internal static class Edges
{
    // --- where: surah, verse, word -------------------------------------------------------
    public const string HasVerse            = nameof(HasVerse);            // Surah → Verse
    public const string InSurah             = nameof(InSurah);             // Verse → Surah

    public const string HasWord             = nameof(HasWord);             // Verse → Word
    public const string InVerse             = nameof(InVerse);             // Word → Verse

    public const string SurahHasWord        = nameof(SurahHasWord);        // Surah → Word
    public const string WordInSurah         = nameof(WordInSurah);         // Word → Surah

    public const string InJuz               = nameof(InJuz);               // Verse → Juz
    public const string JuzHasVerse         = nameof(JuzHasVerse);         // Juz → Verse

    public const string SurahInJuz          = nameof(SurahInJuz);          // Surah → Juz (every juz it spans)
    public const string JuzHasSurah         = nameof(JuzHasSurah);         // Juz → Surah

    public const string RevealedIn          = nameof(RevealedIn);          // Verse → Revelation
    public const string RevelationOfVerse   = nameof(RevelationOfVerse);   // Revelation → Verse

    public const string SurahRevealedIn     = nameof(SurahRevealedIn);     // Surah → Revelation
    public const string RevelationOfSurah   = nameof(RevelationOfSurah);   // Revelation → Surah

    // --- language: roots and lemmas ------------------------------------------------------
    // A word's root and lemma are its STEM's. The verse-level pairs are the same facts one level
    // up, distinct per verse: the rarity-weighted "shares a root" signal and the "verses with
    // root X" filter need the root as a direct neighbour of the verse.
    public const string HasRoot             = nameof(HasRoot);             // Word → Root
    public const string RootOfWord          = nameof(RootOfWord);          // Root → Word

    public const string HasLemma            = nameof(HasLemma);            // Word → Lemma
    public const string LemmaOfWord         = nameof(LemmaOfWord);         // Lemma → Word

    public const string LemmaHasRoot        = nameof(LemmaHasRoot);        // Lemma → Root
    public const string RootOfLemma         = nameof(RootOfLemma);         // Root → Lemma

    public const string VerseHasRoot        = nameof(VerseHasRoot);        // Verse → Root
    public const string RootInVerse         = nameof(RootInVerse);         // Root → Verse

    public const string VerseHasLemma       = nameof(VerseHasLemma);       // Verse → Lemma
    public const string LemmaInVerse        = nameof(LemmaInVerse);        // Lemma → Verse

    // --- topics (QUL) — content, never a similarity or clustering signal -------------------
    public const string HasTopic            = nameof(HasTopic);            // Verse → Topic (as QUL assigns it)
    public const string TopicOfVerse        = nameof(TopicOfVerse);        // Topic → Verse

    public const string InTopicBranch       = nameof(InTopicBranch);       // Verse → every ancestor of its topics
    public const string TopicBranchOfVerse  = nameof(TopicBranchOfVerse);  // Topic → Verse

    public const string ParentTopic         = nameof(ParentTopic);         // Topic → its parent
    public const string ChildTopic          = nameof(ChildTopic);          // Topic → its children

    // --- the answer key (QurSim) — never read by a signal or by the AI ---------------------
    public const string InQurSimPair        = nameof(InQurSimPair);        // Verse → QurSimPair
    public const string QurSimPairVerse     = nameof(QurSimPairVerse);     // QurSimPair → Verse

    // --- tafsir — for people to read, never a signal and never the AI's ----------------------
    public const string HasTafsirEntry      = nameof(HasTafsirEntry);      // Verse → TafsirEntry (every verse it explains)
    public const string ExplainsVerse       = nameof(ExplainsVerse);       // TafsirEntry → Verse

    public const string TafsirHasEntry      = nameof(TafsirHasEntry);      // Tafsir → TafsirEntry
    public const string EntryOfTafsir       = nameof(EntryOfTafsir);       // TafsirEntry → Tafsir

    // --- hadith: the collections, and where they meet the Quran -------------------------------
    public const string InCollection        = nameof(InCollection);        // Hadith → HadithCollection
    public const string CollectionHasHadith = nameof(CollectionHasHadith); // HadithCollection → Hadith

    public const string InHadithBook        = nameof(InHadithBook);        // Hadith → HadithBook
    public const string HadithBookHasHadith = nameof(HadithBookHasHadith); // HadithBook → Hadith

    public const string BookOfCollection    = nameof(BookOfCollection);    // HadithBook → HadithCollection
    public const string CollectionHasBook   = nameof(CollectionHasBook);   // HadithCollection → HadithBook

    public const string HasQuranQuote       = nameof(HasQuranQuote);       // Hadith → QuranQuote
    public const string QuoteInHadith       = nameof(QuoteInHadith);       // QuranQuote → Hadith

    public const string QuoteOfVerse        = nameof(QuoteOfVerse);        // QuranQuote → Verse
    public const string VerseHasQuote       = nameof(VerseHasQuote);       // Verse → QuranQuote

    // The same fact one hop shorter, for "the hadith that quote this verse" (quotations and phrases, not the translator's notes).
    public const string HadithQuotesVerse   = nameof(HadithQuotesVerse);   // Hadith → Verse
    public const string VerseQuotedInHadith = nameof(VerseQuotedInHadith); // Verse → Hadith

    // --- clusters: written by the Clusters work inside the workspace, never by the connector --
    public const string HasMember           = nameof(HasMember);           // Cluster → Verse
    public const string InCluster           = nameof(InCluster);           // Verse → Cluster

    public const string ClusterOfSurah      = nameof(ClusterOfSurah);      // Cluster → Surah (a passage)
    public const string HasCluster          = nameof(HasCluster);          // Surah → Cluster

    public const string HasPassage          = nameof(HasPassage);          // Cluster (theme) → Cluster (passage)
    public const string InTheme             = nameof(InTheme);             // Cluster (passage) → Cluster (theme)

    // --- the workspace's own NLP pair: written by the parsing pipeline, never by the connector --
    public const string _Mentions           = "_Mentions";
    public const string _AppearsIn          = "_AppearsIn";
}
