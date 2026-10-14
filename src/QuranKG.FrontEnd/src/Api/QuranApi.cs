using System.Threading.Tasks;
using UID;

namespace QuranKG.FrontEnd.Api
{
    /// <summary>
    /// One method per endpoint. Every server call goes through this class, so a reader can tell
    /// at a glance which line reaches the server. Every parameter naming something is a
    /// <see cref="UID128"/>: the page already holds the node it is looking at.
    ///
    /// <para>All the endpoints are read-only (<c>readOnlyCompatible: true</c>). Most are cached on
    /// the server for an hour, so calling one again costs a cache hit; the ones that read the
    /// clusters (passages, themes, the map, a cluster, the reading text, similar surahs) are not, so a rebuild shows at once.</para>
    /// </summary>
    internal static class QuranApi
    {
        private static Task<QK.CorpusDto> _corpus;

        /// <summary>
        /// The dashboard's data. Kept for the session: the dashboard, the root pages and the
        /// heat maps all read the same answer, so they cannot disagree, and coming back to the home
        /// page draws at once.
        /// </summary>
        internal static Task<QK.CorpusDto> CorpusAsync()
        {
            if (_corpus is null || _corpus.IsFaulted)
                _corpus = Mosaik.API.Endpoints.CallAsync<QK.CorpusDto>(Endpoints.DashboardCorpus, readOnlyCompatible: true);

            return _corpus;
        }

        internal static Task<QK.RootProfileDto> RootProfileAsync(UID128 rootUID)
            => Mosaik.API.Endpoints.CallAsync<QK.RootProfileDto>(Endpoints.RootProfile, new QK.UidRequest { Uid = rootUID }, readOnlyCompatible: true);

        internal static Task<QK.SurahProfileDto> SurahProfileAsync(UID128 surahUID)
            => Mosaik.API.Endpoints.CallAsync<QK.SurahProfileDto>(Endpoints.SurahProfile, new QK.UidRequest { Uid = surahUID }, readOnlyCompatible: true);

        internal static Task<QK.VerseWordsDto> VerseWordsAsync(UID128 verseUID)
            => Mosaik.API.Endpoints.CallAsync<QK.VerseWordsDto>(Endpoints.VerseWords, new QK.UidRequest { Uid = verseUID }, readOnlyCompatible: true);

        internal static Task<QK.SimilarDto> SimilarVersesAsync(UID128 verseUID, int count)
            => Mosaik.API.Endpoints.CallAsync<QK.SimilarDto>(Endpoints.VerseSimilar, new QK.SimilarRequest { Uid = verseUID, Count = count }, readOnlyCompatible: true);

        internal static Task<QK.SimilarSurahsDto> SimilarSurahsAsync(UID128 surahUID, int count)
            => Mosaik.API.Endpoints.CallAsync<QK.SimilarSurahsDto>(Endpoints.SurahSimilar, new QK.SimilarRequest { Uid = surahUID, Count = count }, readOnlyCompatible: true);

        internal static Task<QK.SurahPassagesDto> SurahPassagesAsync(UID128 surahUID)
            => Mosaik.API.Endpoints.CallAsync<QK.SurahPassagesDto>(Endpoints.SurahPassages, new QK.UidRequest { Uid = surahUID }, readOnlyCompatible: true);

        internal static Task<QK.ThemesDto> ThemesAsync()
            => Mosaik.API.Endpoints.CallAsync<QK.ThemesDto>(Endpoints.ClustersThemes, readOnlyCompatible: true);

        internal static Task<QK.ClusterProfileDto> ClusterProfileAsync(UID128 clusterUID)
            => Mosaik.API.Endpoints.CallAsync<QK.ClusterProfileDto>(Endpoints.ClusterProfile, new QK.UidRequest { Uid = clusterUID }, readOnlyCompatible: true);

        internal static Task<QK.ClusterMapDto> ClustersMapAsync()
            => Mosaik.API.Endpoints.CallAsync<QK.ClusterMapDto>(Endpoints.ClustersMap, readOnlyCompatible: true);

        /// <summary>A Surah or a Cluster (a passage or a theme) as text to read.</summary>
        internal static Task<QK.ReadingDto> ReadTextAsync(UID128 surahOrClusterUID)
            => Mosaik.API.Endpoints.CallAsync<QK.ReadingDto>(Endpoints.ReadText, new QK.UidRequest { Uid = surahOrClusterUID }, readOnlyCompatible: true);
    }
}
