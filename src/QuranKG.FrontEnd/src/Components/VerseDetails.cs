using System;
using System.Linq;
using Mosaik;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// The side panel a verse opens in a reader: the verse, its translations and transliteration,
    /// the passage and theme it sits in, its words with their roots, the verses closest to it in
    /// the Arabic, and the verses Ibn Kathir relates to it. Each part loads on its own, so the
    /// translation shows at once and the similarity ranking follows.
    /// </summary>
    internal static class VerseDetails
    {
        private const int SIMILAR_COUNT = 5;
        private const int TAFSIR_COUNT  = 8;

        /// <param name="goToVerse">Where a related verse leads: in the Quran View, to the verse in the text; elsewhere, to its page.</param>
        /// <param name="readInQuran">Whether to offer to read the verse in its surah (not in the Quran View itself).</param>
        internal static IComponent Create(QK.ReadingVerse v, QK.ReadingSection s, Action close, Action<UID128, string> goToVerse, bool readInQuran)
        {
            goToVerse = goToVerse ?? ((uid, _) => Lists.Open(uid));

            var actions = HStack().AlignItemsCenter().Gap(4.px());
            if (readInQuran)
                actions.Add(Button("Read in the Quran".t()).SetIcon(UIcons.BookQuran).Compact().Tooltip("This verse in its surah, in the Quran View".t()).OnClick(() => QuranLinks.Go(v.VerseKey)));
            actions.Add(Button().SetIcon(UIcons.ArrowUpRightFromSquare).Compact().Tooltip("Open the verse's page".t()).OnClick(() => Lists.Open(v.Uid)));
            actions.Add(Button().SetIcon(UIcons.Cross).Compact().Tooltip("Close".t()).OnClick(() => close()));

            var page = VStack().WS().P(16).Gap(14.px());

            page.Add(HStack().WS().AlignItemsCenter().Children(
                VStack().Grow().Children(
                    TextBlock(v.VerseKey).SemiBold().MediumPlus(),
                    TextBlock(s.SurahNumber + ". " + s.SurahName).Small().Secondary()),
                actions));

            page.Add(Arabic.Text(v.Text, 26));

            // The verse node: the second translation and the transliteration.
            page.Add(Defer(async () =>
            {
                var node = await Mosaik.API.Nodes.GetAsync(v.Uid);
                return VStack().WS().Gap(10.px()).Children(
                    TextBlock(node.GetString(N.Verse.Transliteration) ?? "").Small().Secondary().Style(st => st.setProperty("font-style", "italic")),
                    Translation("Saheeh International", node.GetString(N.Verse.EnglishSaheeh)),
                    Translation("Ali Quli Qarai",       node.GetString(N.Verse.EnglishQarai)),
                    TextBlock(string.Format("Juz {0} · page {1} · {2}".t(), node.GetInt(N.Verse.Juz), node.GetInt(N.Verse.Page), node.GetString(N.Verse.RevelationPlace))).Tiny().Secondary());
            }, Translation("Saheeh International", v.Translation)));

            if (s.PassageUid != default(UID128))
            {
                var passage = VStack().WS().Gap(4.px()).Children(
                    Heading("Passage".t()),
                    HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(
                        Link(string.Format("Passage {0} of {1} · {2}".t(), s.Ordinal, s.SurahName, QuranReader.Range(s)), () => Lists.Open(s.PassageUid)),
                        Arabic.Inline(s.LabelArabic, 17).Foreground(Palette.Lemma)));
                if (s.ThemeUid != default(UID128))
                    passage.Add(HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().Children(
                        TextBlock("Theme shared with other surahs:".t()).Tiny().Secondary(),
                        Clusters.ThemeLink(s.ThemeUid, s.ThemeLabelArabic, s.ThemeLabel, s.ThemeSurahs)));
                if (s.GroupUid != default(UID128))
                    passage.Add(HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().Children(
                        TextBlock("Similarity group:".t()).Tiny().Secondary(),
                        Clusters.ThemeLink(s.GroupUid, s.GroupLabelArabic, "Passages close in Arabic meaning, lemmas and roots".t(), s.GroupSurahs)));
                page.Add(passage);
            }

            page.Add(Defer(async () =>
            {
                var words = await QuranApi.VerseWordsAsync(v.Uid);
                var chips = HStack().WS().Wrap().Gap(6.px()).Style(st => st.setProperty("direction", "rtl"));
                foreach (var word in words.Words ?? new QK.WordRow[0]) chips.Add(WordChip(word));
                return VStack().WS().Gap(6.px()).Children(Heading("Word by word".t()), chips);
            }, TextBlock("Reading the words…".t()).Small().Secondary()));

            page.Add(Defer(async () =>
            {
                var similar = await QuranApi.SimilarVersesAsync(v.Uid, SIMILAR_COUNT);
                var list    = VStack().WS().Gap(8.px()).Children(
                    Heading("Closest verses".t()),
                    TextBlock("Ranked from the Arabic: meaning (embedding), shared lemmas and roots.".t()).Tiny().Secondary());
                foreach (var other in similar.Verses ?? new QK.SimilarVerse[0])
                    list.Add(VerseLine(other.Uid, other.VerseKey, other.Text, Badge("score".t() + " " + other.Score.ToString("0.0")).Primary(), goToVerse));
                return list;
            }, TextBlock("Ranking the closest verses…".t()).Small().Secondary()));

            page.Add(Defer(async () =>
            {
                var words   = await QuranApi.VerseWordsAsync(v.Uid);
                var related = (words.TafsirRelated ?? new QK.RelatedVerse[0]).Take(TAFSIR_COUNT).ToArray();
                var list    = VStack().WS().Gap(8.px()).Children(
                    Heading("Related by Ibn Kathir".t()),
                    TextBlock("The verses his tafsir relates to this one (QurSim) — the reference the similarity is scored against, shown for reading.".t()).Tiny().Secondary());
                if (related.Length == 0) list.Add(TextBlock("None.".t()).Small().Secondary());
                foreach (var r in related)
                {
                    var grade = Badge(r.Grade);
                    list.Add(VerseLine(r.Uid, r.VerseKey, r.Text, r.Degree == 2 ? grade.Success() : r.Degree == 1 ? grade.Info() : grade.Neutral(), goToVerse));
                }
                return list;
            }, Raw()));

            return page;
        }

        private static IComponent Heading(string text) => TextBlock(text).SemiBold().PT(4);

        private static IComponent Translation(string source, string text)
            => VStack().WS().Children(
                   TextBlock(source).Tiny().SemiBold().Secondary(),
                   TextBlock(text ?? "", selectable: true).Small());

        private static IComponent Link(string text, Action onClick)
            => TextBlock(text).Small().Foreground(Theme.Primary.Background)
                              .OnClick((_, __) => onClick())
                              .Style(st => st.setProperty("cursor", "pointer"));

        private static IComponent VerseLine(UID128 uid, string key, string text, IComponent badge, Action<UID128, string> goToVerse)
            => VStack().WS().Children(
                   HStack().AlignItemsCenter().Gap(8.px()).Children(Link(key, () => goToVerse(uid, key)), badge),
                   Arabic.Text(text, 19).OnClick((_, __) => goToVerse(uid, key)).Style(st => st.setProperty("cursor", "pointer")));

        private static IComponent WordChip(QK.WordRow word)
        {
            var body = VStack().AlignItemsCenter().Children(
                Arabic.Inline(word.Text, 21),
                Arabic.Inline(string.IsNullOrEmpty(word.Root) ? "—" : word.Root, 13).Foreground(Palette.Root),
                TextBlock(word.PartOfSpeech ?? "").Tiny().Secondary().Style(st => st.setProperty("direction", "ltr")));

            var tooltip = string.Join(" · ", new[] { word.Lemma, word.Segments, word.Features }.Where(x => !string.IsNullOrEmpty(x)));
            return Card(body).Compact().MinWidth(60.px()).OnClick(() => Lists.Open(word.Uid)).Tooltip(tooltip);
        }
    }

    /// <summary>The Quran View's addresses: #/read/2 opens Al-Baqarah, #/read/2/255 opens it on Ayat al-Kursi.</summary>
    internal static class QuranLinks
    {
        internal const string Route = "#/read";

        internal static string For(int surah, int ayah = 0) => Route + "/" + surah + (ayah > 0 ? "/" + ayah : "");

        /// <summary>"2:255" → the Quran View on that verse.</summary>
        internal static void Go(string verseKey)
        {
            var parts = (verseKey ?? "").Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var surah) && int.TryParse(parts[1], out var ayah)) Go(surah, ayah);
        }

        internal static void Go(int surah, int ayah = 0)
        {
            // A node page, or a verse opened from the chat, is a modal over the page: reading in the
            // Quran replaces them all.
            VerseReferences.Close();
            ModalStack.Clear();
            Router.Navigate(For(surah, ayah));
        }
    }
}
