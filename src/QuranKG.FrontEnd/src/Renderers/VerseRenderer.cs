using System.Collections.Generic;
using System.Linq;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using static Mosaik.UI;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// A verse. Its page reads Arabic first: the Uthmani text, then each word with its root, lemma
    /// and part of speech, and only then the translations. The Tafsir tab gives what al-Mukhtasar,
    /// al-Jalalayn and Ibn Kathir say on it, in Arabic and English. Two more tabs answer "where else does
    /// the Quran say this?" — the similarity ranking, computed from the Arabic, and the verses Ibn
    /// Kathir's tafsir relates to it, which is the reference that ranking is measured against.
    /// </summary>
    public sealed class VerseRenderer : QuranRendererBase
    {
        private const int SIMILAR_COUNT = 20;

        public VerseRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Verse.Type,
            DisplayName = "Verse",
            Icon        = UIcons.BookOpenReader.ToString(),
            LabelField  = N.Verse.VerseKey,
        }) { ShortDisplayName = "AYAH"; }

        protected override string[] Related360Types => new[] { N.Surah.Type, N.Juz.Type, N.Root.Type, N.Lemma.Type, N.Topic.Type, N.Cluster.Type, N.QurSimPair.Type };

        protected override IComponent CardBody(Node node)
            => VStack().WS().Children(
                   Arabic.Text(node.GetString(N.Verse.TextUthmani), 20),
                   TextBlock(node.GetString(N.Verse.EnglishSaheeh) ?? "").Tiny().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Verse.SurahName);
            yield return "Juz".t() + " " + node.GetInt(N.Verse.Juz);
            yield return "page".t() + " " + node.GetInt(N.Verse.Page);
            yield return node.GetString(N.Verse.RevelationPlace);

            var sajda = node.GetString(N.Verse.SajdaType);
            if (!string.IsNullOrEmpty(sajda)) yield return "Sajda".t() + " (" + sajda + ")";
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("verse",      "Verse".t(),            () => VerseTab(node));
            yield return Tab("commentary", "Tafsir".t(),           () => VStack().WS().P(16).Children(TafsirView.Create(node.UID, heading: false)));
            yield return Tab("similar",    "Similar verses".t(),   () => SimilarTab(node));
            yield return Tab("tafsir",     "Ibn Kathir links".t(), () => TafsirTab(node));
        }

        // -----------------------------------------------------------------------------------
        // Verse: the Arabic, word by word, then the translations
        // -----------------------------------------------------------------------------------

        private static IComponent VerseTab(Node verse)
        {
            return Defer(async () =>
            {
                var words = await QuranApi.VerseWordsAsync(verse.UID);

                // Right to left, so the first word sits where an Arabic reader starts.
                var chips = HStack().WS().Wrap().Gap(8.px()).Style(s => s.setProperty("direction", "rtl"));
                foreach (var word in words.Words ?? new QK.WordRow[0]) chips.Add(WordChip(word));

                return VStack().WS().P(16).Gap(16.px()).Children(
                    Card(VStack().WS().Children(
                        Arabic.Text(verse.GetString(N.Verse.TextUthmani), 32),
                        TextBlock(verse.GetString(N.Verse.Transliteration) ?? "").Small().Secondary().PT(8).Style(s => s.setProperty("font-style", "italic")))),
                    Panels.Panel("Word by word".t(),
                                 "Each word with its root, its lemma and its part of speech, from the Quranic Arabic Corpus. Open a word for its segments and features.".t(),
                                 chips),
                    Panels.Row(
                        Panels.Panel("Roots".t(),  null, NeighborsLinks(words.RootUIDs  ?? new UID.UID128[0], wrap: true, maxItemsToShow: 40)),
                        Panels.Panel("Lemmas".t(), null, NeighborsLinks(words.LemmaUIDs ?? new UID.UID128[0], wrap: true, maxItemsToShow: 40))),
                    Panels.Panel("Translations".t(),
                                 "For readers who need a gloss. The similarity work reads the Arabic, not these.".t(),
                                 VStack().WS().Gap(8.px()).Children(
                                     Translation("Saheeh International", verse.GetString(N.Verse.EnglishSaheeh)),
                                     Translation("Ali Quli Qarai",       verse.GetString(N.Verse.EnglishQarai)))));
            }).S();
        }

        private static IComponent WordChip(QK.WordRow word)
        {
            var body = VStack().AlignItemsCenter().Children(
                Arabic.Inline(word.Text, 26),
                Arabic.Inline(string.IsNullOrEmpty(word.Root) ? "—" : word.Root, 15).Foreground(Palette.Root),
                TextBlock(word.PartOfSpeech ?? "").Tiny().Secondary().Style(s => s.setProperty("direction", "ltr")));

            var tooltip = string.Join(" · ", new[] { word.Lemma, word.Segments, word.Features }.Where(x => !string.IsNullOrEmpty(x)));

            return Card(body).Compact().MinWidth(72.px()).OnClick(() => Lists.Open(word.Uid)).Tooltip(tooltip);
        }

        private static IComponent Translation(string source, string text)
            => VStack().WS().Children(
                   TextBlock(source).Tiny().SemiBold().Secondary(),
                   TextBlock(text ?? "", selectable: true).Small());

        // -----------------------------------------------------------------------------------
        // Similar verses: the ranking, and why each verse is in it
        // -----------------------------------------------------------------------------------

        private static IComponent SimilarTab(Node verse)
        {
            return Defer(async () =>
            {
                var similar = await QuranApi.SimilarVersesAsync(verse.UID, SIMILAR_COUNT);
                var list    = VStack().WS().P(16);

                list.Add(TextBlock("Ranked by".t() + ": " + similar.Method + ".").Small().Secondary());
                list.Add(TextBlock("Each signal is standardised over every verse of the Quran, so the badges say how unusual this verse's overlap is — 2.0 means two standard deviations above the typical verse.".t()).Small().Secondary().PB(12));

                if (!similar.SemanticAvailable)
                    list.Add(Banner("The Arabic embeddings are not ready for this verse yet, so this ranking is lexical only.".t()).Warning().MB(12));

                foreach (var v in similar.Verses ?? new QK.SimilarVerse[0])
                {
                    var badges = new List<IComponent> { Badge("score".t() + " " + v.Score.ToString("0.0")).Primary() };
                    if (similar.SemanticAvailable) badges.Add(Signal("meaning".t(), v.Semantic));
                    badges.Add(Signal("lemmas".t(), v.Lemmas));
                    badges.Add(Signal("roots".t(),  v.Roots));
                    foreach (var root in v.SharedRoots ?? new string[0]) badges.Add(Arabic.Inline(root, 15).Foreground(Palette.Root).PL(4));

                    list.Add(Lists.VerseRow(v.Uid, v.VerseKey, v.Text, v.Translation, badges.ToArray()));
                }

                return list;
            }).S();
        }

        private static IComponent Signal(string name, double z)
        {
            var badge = Badge(name + " " + z.ToString("+0.0;-0.0;0.0"));
            return z >= 2 ? badge.Success() : z >= 1 ? badge.Info() : badge.Neutral();
        }

        // -----------------------------------------------------------------------------------
        // Tafsir links: the reference, for people to read
        // -----------------------------------------------------------------------------------

        private static IComponent TafsirTab(Node verse)
        {
            return Defer(async () =>
            {
                var words   = await QuranApi.VerseWordsAsync(verse.UID);
                var related = words.TafsirRelated ?? new QK.RelatedVerse[0];
                var list    = VStack().WS().P(16);

                list.Add(TextBlock("Verses Ibn Kathir's commentary relates to this one, as collected in the QurSim corpus (Sharaf & Atwell, 2012). This is the reference the similarity ranking is measured against — it is never used to compute it.".t()).Small().Secondary().PB(12));

                if (related.Length == 0)
                {
                    list.Add(TextBlock("Ibn Kathir relates no other verse to this one in QurSim.".t()).Secondary());
                    return list;
                }

                foreach (var r in related)
                {
                    var grade = Badge(r.Grade);
                    list.Add(Lists.VerseRow(r.Uid, r.VerseKey, r.Text, r.Translation, r.Degree == 2 ? grade.Success() : r.Degree == 1 ? grade.Info() : grade.Neutral()));
                }

                return list;
            }).S();
        }
    }
}
