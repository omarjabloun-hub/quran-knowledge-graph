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
    /// A root — the two to four consonants a family of Arabic words is built on (ر ح م: mercy, the
    /// womb, the Merciful). Its page shows where in the Quran the family lives: across the 114
    /// surahs, between Meccan and Medinan, which lemmas grow from it and which forms it takes.
    /// </summary>
    public sealed class RootRenderer : QuranRendererBase
    {
        public RootRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Root.Type,
            DisplayName = "Root",
            Icon        = UIcons.Seedling.ToString(),
            LabelField  = LabelFields.FirstOf(N.Root.Spaced, N.Root.RootLetters),
        }) { ShortDisplayName = "ROOT"; }

        protected override string[] Related360Types => new[] { N.Lemma.Type, N.Verse.Type, N.Word.Type };

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("distribution", "Distribution".t(), () => DistributionTab(node));
        }

        private static IComponent DistributionTab(Node root)
        {
            return Defer(async () =>
            {
                var p      = await QuranApi.RootProfileAsync(root.UID);
                var corpus = await QuranApi.CorpusAsync();
                var surahs = corpus.Overview.SurahRows.OrderBy(s => s.Number).ToArray();

                var totals = HStack().WS().Wrap().Gap(12.px()).Children(
                    Card(Metric("Occurrences".t(), Panels.Number(p.Occurrences)).ValueFirst().SetIcon(UIcons.Seedling, Palette.Root)),
                    Card(Metric("Verses".t(),      Panels.Number(p.Verses)).ValueFirst().SetIcon(UIcons.BookOpenReader, Theme.Colors.Blue600))
                       .OnClick(() => Lists.ShowNodes(WordRenderer.Spaced(p.Letters) + " — " + "verses".t(), p.VerseUIDs)),
                    Card(Metric("of 114 surahs".t(), Panels.Number(p.Surahs)).ValueFirst().SetIcon(UIcons.BookQuran, Theme.Colors.Teal700)),
                    Card(Metric("Meccan · Medinan".t(), Panels.Number(p.Meccan) + " · " + Panels.Number(p.Medinan))
                        .Chart(ContributionBar().Decimals(0)
                           .Add("Meccan".t(),  p.Meccan,  Palette.Meccan)
                           .Add("Medinan".t(), p.Medinan, Palette.Medinan))).MinWidth(320.px()));

                var chartHost   = VStack().WS();
                var perThousand = new ChoiceGroup.Choice("Per 1,000 words".t()).Selected();
                var occurrences = new ChoiceGroup.Choice("Occurrences".t());
                var mode        = ChoiceGroup().Horizontal().Choices(perThousand, occurrences);

                // Swaps the one chart; the rest of the page keeps its DOM, scroll and state.
                mode.AsObservable().Observe(choice =>
                {
                    chartHost.Clear();
                    chartHost.Add(SurahChart(p, surahs, perThousand: choice != occurrences));
                });

                // Where the root is densest, relative to each surah's length — clickable, unlike the bars.
                var densest = surahs.Select((s, i) => new QK.Counted
                    {
                        Uid    = s.Uid,
                        Label  = s.Number + ". " + s.Name,
                        Detail = Panels.Number(p.PerSurah[i]) + " " + "occurrences in".t() + " " + Panels.Number(s.Words) + " " + "words".t(),
                        Count  = p.PerSurah[i],
                        Score  = s.Words > 0 ? 1000.0 * p.PerSurah[i] / s.Words : 0,
                    })
                   .Where(c => c.Count > 0)
                   .OrderByDescending(c => c.Score)
                   .Take(12)
                   .ToArray();

                return VStack().WS().P(16).Gap(16.px()).Children(
                    totals,
                    Panels.Panel("Across the 114 surahs".t(),
                                 "Orange bars are Meccan surahs, teal Medinan. Per 1,000 words makes a short surah comparable to a long one.".t(),
                                 VStack().WS().Children(mode, chartHost)),
                    Panels.Row(
                        Panels.Panel("Where it is densest".t(),
                                     "Surahs where the root is most frequent for their length.".t(),
                                     Lists.RankedBars(densest, Palette.Root, arabicLabels: false, value: c => c.Score, valueText: c => c.Score.ToString("0.0") + " ‰")),
                        Panels.Panel("Lemmas".t(),
                                     "The dictionary headwords built on this root.".t(),
                                     Lists.RankedBars(p.Lemmas, Palette.Lemma)),
                        Panels.Panel("Word forms".t(),
                                     "The forms it takes in the text, without diacritics.".t(),
                                     Lists.RankedBars((p.Forms ?? new QK.Counted[0]).Take(20).ToArray(), Theme.Colors.Neutral600))),
                    Panels.Panel("Verses".t(),
                                 "Every verse a word of this root occurs in.".t(),
                                 SearchArea().WithFacets()
                                    .SearchFor(new SearchRequest().WithTargetUIDs(p.VerseUIDs ?? new UID.UID128[0]))
                                    .WS().H(640.px())));
            }).S();
        }

        private static IComponent SurahChart(QK.RootProfileDto p, QK.SurahStats[] surahs, bool perThousand)
        {
            var meccan  = new double[surahs.Length];
            var medinan = new double[surahs.Length];

            for (var i = 0; i < surahs.Length; i++)
            {
                var value = perThousand ? (surahs[i].Words > 0 ? 1000.0 * p.PerSurah[i] / surahs[i].Words : 0) : p.PerSurah[i];
                if (surahs[i].Place == QK.MEDINAN) medinan[i] = value; else meccan[i] = value;
            }

            return BarChart()
               .Series("Meccan".t(),  meccan,  Palette.Meccan)
               .Series("Medinan".t(), medinan, Palette.Medinan)
               .Stacked()
               .XAxis(surahs.Select(s => s.Number.ToString()).ToArray())
               .XAxisTitle("Surah".t())
               .YAxisTitle(perThousand ? "per 1,000 words".t() : "occurrences".t())
               .FormatValues(v => perThousand ? v.ToString("0.0") : v.ToString("N0"))
               .Legend()
               .Tooltips()
               .WS()
               .H(260.px());
        }
    }
}
