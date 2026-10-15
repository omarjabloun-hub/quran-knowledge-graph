using System.Collections.Generic;
using System.Linq;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// A surah. Its page opens on the text itself — read in order, in Arabic, as continuous text in
    /// its passages (<see cref="ReadingPane"/>), a clicked verse opening its translation beside it —
    /// then its lexical profile: how long its verses run, which roots it leans on
    /// and which it uses far more than the rest of the Quran.
    /// </summary>
    public sealed class SurahRenderer : QuranRendererBase
    {
        public SurahRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Surah.Type,
            DisplayName = "Surah",
            Icon        = UIcons.BookQuran.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.Surah.Number) + ". " + LabelFields.Field(N.Surah.NameScholarly)),
        }) { ShortDisplayName = "SURA"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Word.Type, N.Juz.Type, N.Revelation.Type, N.Cluster.Type };

        protected override IComponent CardBody(Node node)
            => HStack().WS().AlignItemsCenter().Gap(12.px()).Children(
                   Arabic.Inline(node.GetString(N.Surah.NameArabic), 24),
                   TextBlock(node.GetString(N.Surah.NameEnglish)).Small().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Surah.RevelationPlace);
            yield return Panels.Number(node.GetInt(N.Surah.AyahCount)) + " " + "verses".t();
            yield return "revealed".t() + " #" + node.GetInt(N.Surah.RevelationOrder);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("read",     "Read".t(),           () => ReadTab(node), scrolls: false);
            yield return Tab("passages", "Passages".t(),       () => PassagesTab(node));
            yield return Tab("similar",  "Similar surahs".t(), () => SimilarTab(node));
            yield return Tab("profile",  "Profile".t(),        () => ProfileTab(node));
        }

        // -----------------------------------------------------------------------------------
        // Read: the surah as it is read
        // -----------------------------------------------------------------------------------

        private static IComponent ReadTab(Node surah)
        {
            var number = surah.GetInt(N.Surah.Number);
            return new ReadingPane(new ReadingPane.Settings
            {
                Uid               = surah.UID,
                OptionsKey        = "quran",
                KeyWordsByDefault = false,
                ReadInQuran       = true,
                Header            = _ => HStack().WS().AlignItemsCenter().PL(16).PR(16).PT(10).Children(
                                        TextBlock("Click a verse for its translation, its words and the verses related to it.".t()).Small().Secondary().Grow(),
                                        Button("Open in the Quran View".t()).SetIcon(UIcons.BookQuran).Compact().OnClick(() => QuranLinks.Go(number))),
            }).S();
        }

        // -----------------------------------------------------------------------------------
        // Passages: the surah's sections, and the themes they share with other surahs
        // -----------------------------------------------------------------------------------

        private static IComponent PassagesTab(Node surah)
        {
            return Defer(async () =>
            {
                var result = await QuranApi.SurahPassagesAsync(surah.UID);
                var page   = VStack().WS().P(16).Gap(12.px());

                if (!result.Built)
                    return page.Children(Banner("The passages have not been built yet. An administrator builds them with `just clusters` (the qkg/clusters/build endpoint); it takes a few seconds.".t()).Warning());

                var passages = result.Passages ?? new QK.PassageRow[0];
                var themed   = passages.Count(p => UID128.IsNotNull(p.ThemeUid));

                page.Add(TextBlock(string.Format("{0} passages, cut where the vocabulary changes; {1} of them tell a story or treat a subject that other surahs share — in green.".t(), passages.Length, themed)).Small());
                page.Add(Clusters.Strip(passages));
                page.Add(TextBlock(result.Method + " " + "Built".t() + " " + result.RunId + ".").Tiny().Secondary().PB(4));

                foreach (var p in passages) page.Add(Clusters.PassageCard(p, showSurah: false));
                return page;
            }).S();
        }

        // -----------------------------------------------------------------------------------
        // Similar surahs: which surahs lean on the same distinctive words, and hold verses close in meaning
        // -----------------------------------------------------------------------------------

        private const int SIMILAR_COUNT = 10;

        private static IComponent SimilarTab(Node surah)
        {
            return Defer(async () =>
            {
                var similar = await QuranApi.SimilarSurahsAsync(surah.UID, SIMILAR_COUNT);
                var list    = VStack().WS().P(16);

                list.Add(TextBlock("Ranked by".t() + ": " + similar.Method + ".").Small().Secondary());
                list.Add(TextBlock("Each score is standardised over the other surahs: 2.0 means two standard deviations above the typical surah. The Arabic below each is what they share most; the verse pairs are the closest in meaning the Arabic embedding found; the themes are stories or subjects both tell.".t()).Small().Secondary().PB(12));

                foreach (var s in similar.Surahs ?? new QK.SimilarSurah[0]) list.Add(SimilarCard(s));
                return list;
            }).S();
        }

        private static IComponent SimilarCard(QK.SimilarSurah s)
        {
            var header = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(
                TextBlock(s.Number + ". " + s.Name).SemiBold(),
                Arabic.Inline(s.NameArabic, 20),
                TextBlock(s.NameEnglish).Small().Secondary(),
                Badge(s.Place).Foreground(Palette.ForPlace(s.Place)).Outline(),
                Badge("score".t() + " " + s.Score.ToString("0.0")).Primary(),
                Badge("lemmas".t() + " " + s.Lemmas.ToString("+0.0;-0.0;0.0")).Neutral(),
                Badge("roots".t() + " " + s.Roots.ToString("+0.0;-0.0;0.0")).Neutral(),
                Badge("meaning".t() + " " + s.Meaning.ToString("+0.0;-0.0;0.0")).Neutral());

            var shared = HStack().WS().AlignItemsCenter().Gap(12.px()).Wrap().PT(6);
            foreach (var lemma in s.SharedLemmas ?? new QK.Counted[0])
            {
                var captured = lemma;
                shared.Add(Arabic.Inline(captured.Label, 18).Foreground(Palette.Lemma)
                                 .OnClick((_, e) => { e.stopPropagation(); Lists.Open(captured.Uid); })
                                 .Style(st => st.setProperty("cursor", "pointer")));
            }

            var body = VStack().WS().Children(header, shared);

            var pairs = s.CloseVerses ?? new QK.VersePair[0];
            if (pairs.Length > 0)
            {
                var row = HStack().WS().AlignItemsCenter().Gap(14.px()).Wrap().PT(6).Children(TextBlock("Close in meaning:".t()).Tiny().Secondary());
                foreach (var pair in pairs.Take(3))
                    row.Add(HStack().AlignItemsCenter().Gap(4.px()).Children(VerseLink(pair.SeedUid, pair.SeedVerseKey), TextBlock("↔").Tiny().Secondary(), VerseLink(pair.Uid, pair.VerseKey)));
                row.Add(TextBlock(string.Format("{0} pairs among each verse's 10 nearest".t(), pairs.Length)).Tiny().Secondary());
                body.Add(row);
            }

            var themes = (s.SharedThemes ?? new QK.Counted[0]).Take(4).ToArray();
            if (themes.Length > 0)
            {
                var row = HStack().WS().AlignItemsCenter().Gap(16.px()).Wrap().PT(6).Children(TextBlock("Shared themes:".t()).Tiny().Secondary());
                foreach (var t in themes) row.Add(Clusters.ThemeLink(t.Uid, t.Detail, t.Label, 0));
                body.Add(row);
            }

            return Card(body).OnClick(() => Lists.Open(s.Uid)).MB(8);
        }

        private static IComponent VerseLink(UID128 verse, string key)
            => TextBlock(key).Small().Foreground(Palette.Meaning)
                             .OnClick((_, e) => { e.stopPropagation(); Lists.Open(verse); })
                             .Style(st => st.setProperty("cursor", "pointer"));

        // -----------------------------------------------------------------------------------
        // Profile: what this surah's language looks like next to the rest
        // -----------------------------------------------------------------------------------

        private static IComponent ProfileTab(Node surah)
        {
            return Defer(async () =>
            {
                var p = await QuranApi.SurahProfileAsync(surah.UID);

                var totals = HStack().WS().Wrap().Gap(12.px()).Children(
                    Card(Metric("Verses".t(),         Panels.Number(p.Verses)).ValueFirst().SetIcon(UIcons.BookOpenReader, Theme.Colors.Blue600)),
                    Card(Metric("Words".t(),          Panels.Number(p.Words)).ValueFirst().SetIcon(UIcons.Text, Theme.Colors.Neutral600)),
                    Card(Metric("Distinct roots".t(), Panels.Number(p.DistinctRoots)).ValueFirst().SetIcon(UIcons.Seedling, Palette.Root)),
                    Card(Metric("Distinct lemmas".t(), Panels.Number(p.DistinctLemmas)).ValueFirst().SetIcon(UIcons.LetterCase, Palette.Lemma)),
                    Card(Metric("Parts of speech".t(), Panels.Number(p.Words) + " " + "words".t())
                        .Chart(ContributionBar().Decimals(0)
                           .Add("Nouns".t(),     p.Nouns,     Palette.Noun)
                           .Add("Verbs".t(),     p.Verbs,     Palette.Verb)
                           .Add("Particles".t(), p.Particles, Palette.Other))).MinWidth(320.px()));

                var lengths = BarChart()
                   .Series("Words".t(), p.VerseLengths.Select(x => (double)x).ToArray(), Palette.ForPlace(surah.GetString(N.Surah.RevelationPlace)))
                   .XAxis(Enumerable.Range(1, p.VerseLengths.Length).Select(i => i.ToString()).ToArray())
                   .XAxisTitle("Ayah".t())
                   .YAxisTitle("Words".t())
                   .Tooltips()
                   .WS()
                   .H(220.px());

                return VStack().WS().P(16).Gap(16.px()).Children(
                    totals,
                    Panels.Panel("Verse lengths".t(),
                                 "Words in each verse, in ayah order. Early Meccan surahs move in short verses; Medinan ones in long, legislative ones.".t(),
                                 lengths),
                    Panels.Row(
                        Panels.Panel("Distinctive roots".t(),
                                     "Roots this surah uses far more than the rest of the Quran: share of its words × log(114 ÷ surahs using the root). They tend to name what the surah is about.".t(),
                                     Lists.RankedBars(p.DistinctiveRoots, Palette.Root, value: r => r.Score, valueText: r => r.Score.ToString("0.0"))),
                        Panels.Panel("Most frequent roots".t(),
                                     "How often each root occurs in this surah.".t(),
                                     Lists.RankedBars(p.TopRoots, Palette.Root)),
                        Panels.Panel("Most frequent nouns and verbs".t(),
                                     "Lemmas — dictionary headwords — of the content words; pronouns and particles left out.".t(),
                                     Lists.RankedBars(p.TopLemmas, Palette.Lemma))));
            }).S();
        }
    }
}
