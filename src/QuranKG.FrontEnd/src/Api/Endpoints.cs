namespace QuranKG.FrontEnd.Api
{
    /// <summary>
    /// One const per endpoint path. The implementations live in
    /// src/QuranKG.Definitions/code/endpoints/qkg/, and their Path attributes must stay IDENTICAL
    /// to the values here — an endpoint is resolved by path, not by name.
    /// </summary>
    internal static class Endpoints
    {
        /// <summary>
        /// Everything the pattern dashboard draws, from one pass over the corpus: totals, per-surah
        /// statistics, Meccan and Medinan side by side, the top roots across the 30 juz and the 114
        /// surahs, and the roots most characteristic of each period. Cached for an hour. Read-only.
        /// </summary>
        public const string DashboardCorpus = "qkg/dashboard/corpus";

        /// <summary>One root: its spread over the surahs, lemmas, word forms and verses. Body: <c>{ "Uid": … }</c>.</summary>
        public const string RootProfile = "qkg/root/profile";

        /// <summary>One surah: verse lengths and texts in ayah order, top and distinctive roots, lemmas. Body: <c>{ "Uid": … }</c>.</summary>
        public const string SurahProfile = "qkg/surah/profile";

        /// <summary>One verse's words with their morphology, and the verses Ibn Kathir relates to it. Body: <c>{ "Uid": … }</c>.</summary>
        public const string VerseWords = "qkg/verse/words";

        /// <summary>Verses similar to one verse, Arabic first. Body: <c>{ "Uid": …, "Count": 20 }</c>.</summary>
        public const string VerseSimilar = "qkg/verse/similar";

        /// <summary>Surahs similar to one surah, by their distinctive lemmas and roots. Body: <c>{ "Uid": …, "Count": 10 }</c>.</summary>
        public const string SurahSimilar = "qkg/surah/similar";

        /// <summary>One surah's passages — its sections, found by the clusters build — with their themes. Body: <c>{ "Uid": … }</c>.</summary>
        public const string SurahPassages = "qkg/surah/passages";

        /// <summary>Every theme: passages of several surahs that tell the same story or treat the same subject. No body.</summary>
        public const string ClustersThemes = "qkg/clusters/themes";

        /// <summary>One cluster: a theme with its passages, or a passage with its verses. Body: <c>{ "Uid": … }</c>.</summary>
        public const string ClusterProfile = "qkg/cluster/profile";
    }
}
