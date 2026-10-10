using System.Collections.Generic;
using System.Linq;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// A surah. Its page opens on the text itself — read in order, in Arabic, each verse opening
    /// its own page — then its lexical profile: how long its verses run, which roots it leans on
    /// and which it uses far more than the rest of the Quran.
    /// </summary>
    public sealed class SurahRenderer : QuranRendererBase
    {
        private const string BISMILLAH = "بِسْمِ ٱللَّهِ ٱلرَّحْمَـٰنِ ٱلرَّحِيمِ";

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
            yield return Tab("read",    "Read".t(),    () => ReadTab(node));
            yield return Tab("profile", "Profile".t(), () => ProfileTab(node));
        }

        // -----------------------------------------------------------------------------------
        // Read: the surah as it is read
        // -----------------------------------------------------------------------------------

        private static IComponent ReadTab(Node surah)
        {
            var number = surah.GetInt(N.Surah.Number);

            return Defer(async () =>
            {
                var profile = await QuranApi.SurahProfileAsync(surah.UID);
                var page    = VStack().WS().P(16);

                page.Add(Arabic.Centered("سورة " + surah.GetString(N.Surah.NameArabic), 34));
                page.Add(TextBlock(surah.GetString(N.Surah.NameScholarly) + " — " + surah.GetString(N.Surah.NameEnglish)).Small().Secondary().TextCenter().WS().PB(8));

                // Tanzil prefixes the Bismillah to verse 1 of every surah but 1 (where it IS verse 1)
                // and 9 (which has none); the connector strips it, so it is drawn here as the heading.
                if (number != 1 && number != 9) page.Add(Arabic.Centered(BISMILLAH, 26).PB(12));

                for (var i = 0; i < profile.VerseUIDs.Length; i++)
                {
                    var uid  = profile.VerseUIDs[i];
                    var text = profile.VerseTexts[i] + " ﴿" + Panels.ArabicDigits(i + 1) + "﴾";

                    page.Add(Card(VStack().WS().Children(
                                 Arabic.Text(text, 26),
                                 TextBlock(profile.VerseTranslations[i] ?? "").Small().Secondary()))
                             .OnClick(() => Lists.Open(uid))
                             .MB(6));
                }

                return page;
            }).S();
        }

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
