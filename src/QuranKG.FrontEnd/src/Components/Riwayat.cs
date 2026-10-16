using System.Linq;
using QuranKG.FrontEnd.Api;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// The reading (riwaya) the Quran is read in: the reader's choice, kept in the browser — empty
    /// until they choose, which is the main reading (Qalun; the graph says which: Riwaya.Rank 1).
    /// </summary>
    internal static class Riwayat
    {
        private const string KEY = "qkg-riwaya";

        /// <summary>The reading chosen, or "" for the main one.</summary>
        internal static string Saved()
        {
            try { return localStorage.getItem(KEY) ?? ""; }
            catch { return ""; }
        }

        internal static void Save(string id)
        {
            try { localStorage.setItem(KEY, id ?? ""); }
            catch { /* nothing to keep it in */ }
        }

        /// <summary>The reading a text is in.</summary>
        internal static QK.RiwayaOption Of(QK.ReadingDto text)
            => (text?.Riwayat ?? new QK.RiwayaOption[0]).FirstOrDefault(r => r.Id == text.Riwaya);

        /// <summary>True when a text is in a reading other than the graph's own (Hafs): its verses are not Verse nodes.</summary>
        internal static bool IsOther(QK.ReadingDto text) => Of(text) is QK.RiwayaOption r && !r.IsCorpusText;

        /// <summary>A reading's short name: its transmitter («قالون», «حفص»).</summary>
        internal static string Short(QK.RiwayaOption r) => (r?.NameArabic ?? "").Split(' ')[0];

        /// <summary>The Hafs verse ("2:255") a verse of the reader opens on: its first Hafs verse.</summary>
        internal static string HafsKey(QK.ReadingVerse v)
            => v?.HafsKeys is object && v.HafsKeys.Length > 0 ? v.HafsKeys[0] : v?.VerseKey;

        /// <summary>The ayah of <see cref="HafsKey"/>; 0 for none.</summary>
        internal static int HafsAyah(QK.ReadingVerse v)
        {
            var key   = HafsKey(v) ?? "";
            var colon = key.IndexOf(':');
            return colon > 0 && int.TryParse(key.Substring(colon + 1), out var ayah) ? ayah : 0;
        }
    }
}
