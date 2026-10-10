using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Dashboard
{
    /// <summary>
    /// The home page: patterns anyone can see, counted live from the graph. One call
    /// (<c>qkg/dashboard/corpus</c>) feeds every panel, so no two numbers on the page can disagree.
    /// Everything that counts a node opens it — a root's label, a heat-map cell (the verses where
    /// that root occurs in that juz), a surah's row.
    ///
    /// <para>The order of the page goes from the whole to the parts: what the text is made of,
    /// how Meccan and Medinan differ, how verse length moves through the book and through the
    /// revelation, which words carry it, and where each root lives.</para>
    /// </summary>
    internal sealed class PatternDashboard : IComponent
    {
        private readonly IComponent _content;

        public PatternDashboard(Parameters state)
        {
            _content = VStack().S().ScrollY().Children(
                Defer(async () =>
                {
                    var corpus = await QuranApi.CorpusAsync();
                    var o      = corpus.Overview;

                    return VStack().WS().P(24).Gap(16.px()).Children(
                        Header(corpus),
                        Totals(o),

                        Panels.Heading("Meccan and Medinan".t(),
                                       "The traditional attribution of each surah to the years in Mecca or the years in Medina, as Tanzil gives it.".t()),
                        Periods(corpus),

                        Panels.Heading("Verse length".t(),
                                       "Average words per verse in each surah. One bar per surah; orange is Meccan, teal Medinan.".t()),
                        VerseLengths(o),

                        Panels.Heading("The vocabulary".t(),
                                       "Counted over the Arabic: roots and lemmas from the Quranic Arabic Corpus morphology. Click a label to open it.".t()),
                        Vocabulary(o),

                        Panels.Heading("Where the roots live".t(),
                                       "The thirty most frequent roots across the 30 juz or the 114 surahs. Click a cell for the verses where that root occurs there; click a root or a number to open it.".t()),
                        RootMap(corpus),

                        Panels.Heading("Every surah".t()),
                        SurahTable(o));
                }, TextBlock("Counting the Quran…".t()).Secondary().P(24)).WS());
        }

        public HTMLElement Render() => _content.Render();

        // -----------------------------------------------------------------------------------
        // header and totals
        // -----------------------------------------------------------------------------------

        private static IComponent Header(QK.CorpusDto c)
        {
            var o = c.Overview;

            return HStack().WS().AlignItemsCenter().Gap(24.px()).Children(
                VStack().Grow().Children(
                    TextBlock("Patterns in the Quran".t()).XLarge().Bold(),
                    TextBlock(string.Format("Every number on this page is counted live from the knowledge graph: the Arabic text of the {0} verses, and the root, lemma and part of speech of each of its {1} words.".t(),
                                            Panels.Number(o.Verses), Panels.Number(o.Words))).Small().Secondary().PT(4),
                    TextBlock(string.Format("Computed in {0} s, cached for an hour.".t(), (c.Milliseconds / 1000).ToString("0.0"))).Tiny().Secondary().PT(2)),
                Arabic.Inline("القرآن الكريم", 40).NoShrink());
        }

        private static IComponent Totals(QK.OverviewDto o)
        {
            var row = HStack().WS().Wrap().Gap(12.px()).AlignItems(ItemAlign.Start);

            row.Add(Tile("Surahs".t(),              o.Surahs,        UIcons.BookQuran,      Theme.Colors.Teal700, () => Lists.ShowNodesOfType("Surahs".t(), N.Surah.Type)));
            row.Add(Tile("Verses".t(),              o.Verses,        UIcons.BookOpenReader, Theme.Colors.Blue600, () => Lists.ShowNodesOfType("Verses".t(), N.Verse.Type)));
            row.Add(Tile("Words".t(),               o.Words,         UIcons.Text,           Theme.Colors.Neutral600, () => Lists.ShowNodesOfType("Words".t(), N.Word.Type)));
            // Distinct forms are not nodes: nothing to open, so the tile does not invite a click.
            row.Add(Tile("Distinct word forms".t(), o.DistinctForms, UIcons.Fingerprint,    Theme.Colors.Neutral500, null));
            row.Add(Tile("Lemmas".t(),              o.Lemmas,        UIcons.LetterCase,     Palette.Lemma, () => Lists.ShowNodesOfType("Lemmas".t(), N.Lemma.Type)));
            row.Add(Tile("Roots".t(),               o.Roots,         UIcons.Seedling,       Palette.Root,  () => Lists.ShowNodesOfType("Roots".t(), N.Root.Type)));

            row.Add(Card(Metric("Parts of speech".t(), Share(o.Nouns, o.Words) + " " + "nouns".t())
                        .Chart(ContributionBar().Decimals(0)
                           .Add("Nouns".t(),     o.Nouns,     Palette.Noun)
                           .Add("Verbs".t(),     o.Verbs,     Palette.Verb)
                           .Add("Particles".t(), o.Particles, Palette.Other)))
                   .MinWidth(300.px()));

            return row;
        }

        private static IComponent Tile(string title, int value, UIcons icon, string color, Action open)
        {
            var metric = Metric(title, Panels.Number(value)).ValueFirst().SetIcon(icon, color);
            var card   = Card(metric).MinWidth(170.px());
            return open is null ? card : card.OnClick(open);
        }

        // -----------------------------------------------------------------------------------
        // Meccan and Medinan
        // -----------------------------------------------------------------------------------

        private static IComponent Periods(QK.CorpusDto c)
        {
            var places  = c.Overview.Places ?? new QK.PlaceStats[0];
            var meccan  = places.FirstOrDefault(p => p.Place == QK.MECCAN);
            var medinan = places.FirstOrDefault(p => p.Place == QK.MEDINAN);
            if (meccan is null || medinan is null) return TextBlock("No revelation places in the graph.".t()).Secondary();

            var finding = string.Format("A Medinan verse runs to {0} words on average, a Meccan one to {1}: {2}× as long. Medinan surahs are {3} of {4}, yet hold {5} of the words.".t(),
                                        medinan.WordsPerVerse.ToString("0.0"), meccan.WordsPerVerse.ToString("0.0"),
                                        (medinan.WordsPerVerse / Math.Max(0.1, meccan.WordsPerVerse)).ToString("0.0"),
                                        medinan.Surahs, meccan.Surahs + medinan.Surahs, Share(medinan.Words, meccan.Words + medinan.Words));

            var shares = VStack().WS().Gap(10.px()).Children(
                TextBlock(finding).Small(),
                Split("Surahs".t(), meccan.Surahs, medinan.Surahs),
                Split("Verses".t(), meccan.Verses, medinan.Verses),
                Split("Words".t(),  meccan.Words,  medinan.Words));

            var distinctive = c.Distinctive;

            return VStack().WS().Gap(16.px()).Children(
                Panels.Row(PlaceCard(meccan), PlaceCard(medinan), Panels.Panel("Shares".t(), null, shares)),
                Panels.Row(
                    Panels.Panel("Most Meccan roots".t(),
                                 "Roots used far more in Meccan surahs than their share of the text predicts (log-odds with an informative prior, z-scored). Hover a bar for the counts.".t(),
                                 Lists.RankedBars(distinctive.Meccan, Palette.Meccan, value: r => r.Score, valueText: r => r.Score.ToString("0.0"))),
                    Panels.Panel("Most Medinan roots".t(),
                                 "The same measure, the other way round.".t(),
                                 Lists.RankedBars(distinctive.Medinan, Palette.Medinan, value: r => -r.Score, valueText: r => (-r.Score).ToString("0.0")))));
        }

        private static IComponent PlaceCard(QK.PlaceStats p)
        {
            var metric = Metric(string.Format("words per verse, {0}".t(), p.Place), p.WordsPerVerse.ToString("0.0"))
               .ValueFirst()
               .SetIcon(p.Place == QK.MEDINAN ? UIcons.Mosque : UIcons.Kaaba, Palette.ForPlace(p.Place))
               .Change(TextBlock(string.Format("{0} surahs · {1} verses · {2} words".t(), p.Surahs, Panels.Number(p.Verses), Panels.Number(p.Words))).Small().Secondary());

            return Card(metric).OnClick(() => Lists.Open(p.Uid));
        }

        private static IComponent Split(string what, int meccan, int medinan)
            => VStack().WS().Children(
                   TextBlock(what).Tiny().SemiBold().Secondary(),
                   ContributionBar().Decimals(0)
                      .Add("Meccan".t(),  meccan,  Palette.Meccan)
                      .Add("Medinan".t(), medinan, Palette.Medinan));

        // -----------------------------------------------------------------------------------
        // verse length, in the book's order and in the order of revelation
        // -----------------------------------------------------------------------------------

        private static IComponent VerseLengths(QK.OverviewDto o)
        {
            var mushaf     = o.SurahRows.OrderBy(s => s.Number).ToArray();
            var revelation = o.SurahRows.OrderBy(s => s.RevelationOrder).ToArray();

            return VStack().WS().Gap(16.px()).Children(
                Panels.Panel("In the order of the book".t(),
                             "Surahs 1 to 114 as the mushaf orders them, broadly longest first.".t(),
                             LengthChart(mushaf)),
                Panels.Panel("In the order of revelation".t(),
                             "The same surahs in the traditional chronological order Tanzil gives. Read left to right, the short Meccan verses give way to the long Medinan ones.".t(),
                             LengthChart(revelation)));
        }

        private static IComponent LengthChart(QK.SurahStats[] surahs)
        {
            var meccan  = surahs.Select(s => s.Place == QK.MEDINAN ? 0 : s.WordsPerVerse).ToArray();
            var medinan = surahs.Select(s => s.Place == QK.MEDINAN ? s.WordsPerVerse : 0).ToArray();

            return BarChart()
               .Series("Meccan".t(),  meccan,  Palette.Meccan)
               .Series("Medinan".t(), medinan, Palette.Medinan)
               .Stacked()
               .XAxis(surahs.Select(s => s.Number.ToString()).ToArray())
               .XAxisTitle("Surah".t())
               .YAxisTitle("Words per verse".t())
               .FormatValues(v => v.ToString("0.0"))
               .Legend()
               .Tooltips()
               .WS()
               .H(240.px());
        }

        // -----------------------------------------------------------------------------------
        // vocabulary
        // -----------------------------------------------------------------------------------

        private static IComponent Vocabulary(QK.OverviewDto o)
            => Panels.Row(
                   Panels.Panel("Most frequent roots".t(),
                                "How many words of the text are built on each root.".t(),
                                Lists.RankedBars(o.TopRoots, Palette.Root)),
                   Panels.Panel("Most frequent nouns".t(),
                                "Noun lemmas; pronouns left out.".t(),
                                Lists.RankedBars(o.TopNouns, Palette.Noun)),
                   Panels.Panel("Most frequent verbs".t(),
                                "Verb lemmas, all forms and tenses together.".t(),
                                Lists.RankedBars(o.TopVerbs, Palette.Verb)));

        // -----------------------------------------------------------------------------------
        // the root map
        // -----------------------------------------------------------------------------------

        private enum HeatUnit { Concentration, PerThousand, Occurrences }

        private static IComponent RootMap(QK.CorpusDto c)
        {
            var host = VStack().WS();

            var byJuz         = new ChoiceGroup.Choice("By juz".t()).Selected();
            var bySurah       = new ChoiceGroup.Choice("By surah".t());
            var concentration = new ChoiceGroup.Choice("Concentration".t()).Selected();
            var perThousand   = new ChoiceGroup.Choice("Per 1,000 words".t());
            var occurrences   = new ChoiceGroup.Choice("Occurrences".t());

            var scope = ChoiceGroup().Horizontal().Choices(byJuz, bySurah);
            var unit  = ChoiceGroup().Horizontal().Choices(concentration, perThousand, occurrences);
            var note  = TextBlock("").Small().Secondary().PT(4);

            // Redraws the one heat map; the rest of the page keeps its DOM and its scroll position.
            void Redraw()
            {
                var surahs = scope.SelectedOption == bySurah;
                var shown  = unit.SelectedOption == occurrences ? HeatUnit.Occurrences
                           : unit.SelectedOption == perThousand ? HeatUnit.PerThousand
                           : HeatUnit.Concentration;

                note.Text = shown == HeatUnit.Concentration
                    ? "Each root against its own average: 1× is the rate the root has across the whole Quran, orange is denser, blue sparser. Short surahs swing widely.".t()
                    : shown == HeatUnit.PerThousand
                        ? "How many words in every 1,000 are built on the root. The most frequent root sets the scale.".t()
                        : "Raw counts: long juz and long surahs dominate.".t();

                host.Clear();
                host.Add(surahs
                    ? RootHeatmap(c.RootsBySurah, shown, E.HasVerse,    "Surah".t())
                    : RootHeatmap(c.RootsByJuz,   shown, E.JuzHasVerse, "Juz".t()));
            }

            scope.AsObservable().ObserveFutureChanges(_ => Redraw());
            unit.AsObservable().ObserveFutureChanges(_ => Redraw());
            Redraw();

            return Card(VStack().WS().Children(
                HStack().WS().Wrap().Gap(24.px()).Children(scope, unit),
                note,
                host.PT(8))).WS();
        }

        /// <summary>
        /// Roots across the X axis and juz or surahs down the Y axis. Rows of the DTO are roots and
        /// its columns the juz or surahs, so the matrix is transposed here. The root labels are HTML
        /// (Arabic shaped right to left, each opening its root); the chart's own SVG labels would
        /// lay the letters out left to right.
        /// </summary>
        private static IComponent RootHeatmap(QK.HeatmapDto h, HeatUnit unit, string columnEdge, string columnName)
        {
            var roots = h.Rows.Length;
            var parts = h.Columns.Length;
            var data  = new double[parts][];

            // A root's own rate across the corpus, against which Concentration measures each part.
            var allWords  = h.ColumnWords.Sum(w => (double)w);
            var rootRates = new double[roots];
            for (var r = 0; r < roots; r++) rootRates[r] = allWords > 0 ? h.Counts[r].Sum() / allWords : 0;

            for (var p = 0; p < parts; p++)
            {
                data[p] = new double[roots];
                for (var r = 0; r < roots; r++)
                {
                    switch (unit)
                    {
                        case HeatUnit.Occurrences: data[p][r] = h.Counts[r][p]; break;
                        case HeatUnit.PerThousand: data[p][r] = h.PerThousand[r][p]; break;
                        default:
                            var rate = h.ColumnWords[p] > 0 ? (double)h.Counts[r][p] / h.ColumnWords[p] : 0;
                            data[p][r] = rootRates[r] > 0 ? rate / rootRates[r] : 0;
                            break;
                    }
                }
            }

            var labels = h.Rows.Select(row =>
            {
                var captured = row;
                return (IComponent)Arabic.Inline(captured.Label, 12)
                   .OnClick((_, __) => Lists.Open(captured.Uid))
                   .Tooltip(Panels.Number(captured.Count) + " " + "occurrences".t())
                   .Style(s => s.setProperty("cursor", "pointer"));
            }).ToArray();

            var map = HeatMap(data)
               .XAxis(labels, h.Rows.Select(r => r.Label).ToArray())
               .YAxis(h.Columns)
               .Legend()
               .Tooltips()
               .OnCellClick(cell => ShowRootIn(h.Rows[cell.Column], h.ColumnUIDs[cell.Row], columnEdge, columnName + " " + h.Columns[cell.Row]).FireAndForget())
               .OnYLabelClick((i, _) => Lists.Open(h.ColumnUIDs[i]));

            if (unit == HeatUnit.Concentration)
            {
                // Diverging around 1×: sparser in blue, denser in orange, the expected rate pale.
                map.ColorScale(Theme.Colors.Blue600, Theme.Colors.Blue400, Theme.Colors.Blue200, Theme.Colors.Blue100,
                               Theme.Colors.Neutral100,
                               Theme.Colors.Orange100, Theme.Colors.Orange200, Theme.Colors.Orange400, Theme.Colors.Orange600)
                   .ValueRange(0, 2)
                   .FormatValues(v => v.ToString("0.0") + "×");
            }
            else
            {
                map.ScaleColor(Palette.Root)
                   .FormatValues(v => unit == HeatUnit.PerThousand ? v.ToString("0.0") : v.ToString("N0"));
            }

            return map.WS().H((Math.Max(12, Math.Min(22, 900 / Math.Max(1, parts))) * parts + 110).px());
        }

        /// <summary>
        /// The verses of one juz or surah in which a root occurs. The query API has no
        /// intersection, so both verse sets are fetched — a root's verses and the part's — and
        /// intersected here: a few thousand UIDs at most.
        /// </summary>
        private static async Task ShowRootIn(QK.Counted root, UID128 partUid, string partEdge, string partName)
        {
            var rootVerses = await Mosaik.API.Query.StartAt(root.Uid).Out(N.Verse.Type, E.RootInVerse).TakeAll().GetUIDsAsync();
            var partVerses = await Mosaik.API.Query.StartAt(partUid).Out(N.Verse.Type, partEdge).TakeAll().GetUIDsAsync();

            var inPart = new HashSet<UID128>();
            for (var i = 0; i < partVerses.UIDs.Length; i++) inPart.Add(partVerses.UIDs[i]);

            var both = new List<UID128>();
            for (var i = 0; i < rootVerses.UIDs.Length; i++)
                if (inPart.Contains(rootVerses.UIDs[i])) both.Add(rootVerses.UIDs[i]);

            Lists.ShowNodes(root.Label + " — " + partName, both.ToArray());
        }

        // -----------------------------------------------------------------------------------
        // the surah table
        // -----------------------------------------------------------------------------------

        private static IComponent SurahTable(QK.OverviewDto o)
        {
            var host       = VStack().WS();
            var mushaf     = new ChoiceGroup.Choice("Order of the book".t()).Selected();
            var revelation = new ChoiceGroup.Choice("Order of revelation".t());
            var order      = ChoiceGroup().Horizontal().Choices(mushaf, revelation);

            void Redraw()
            {
                var rows = order.SelectedOption == revelation
                    ? o.SurahRows.OrderBy(s => s.RevelationOrder).ToArray()
                    : o.SurahRows.OrderBy(s => s.Number).ToArray();

                host.Clear();
                host.Add(SurahGrid(rows));
            }

            order.AsObservable().ObserveFutureChanges(_ => Redraw());
            Redraw();

            return Card(VStack().WS().Children(order, host.PT(8))).WS();
        }

        private static IComponent SurahGrid(QK.SurahStats[] rows)
        {
            var grid = Grid(48.px(), 120.px(), 2.fr(), 2.fr(), 90.px(), 90.px(), 70.px(), 80.px(), 90.px(), 70.px(), 70.px())
               .WS()
               .ColumnGap(12.px())
               .RowGap(4.px())
               .AlignItemsCenter();

            foreach (var header in new[] { "#", "Arabic".t(), "Name".t(), "Meaning".t(), "Place".t(), "Revelation".t(), "Verses".t(), "Words".t(), "Words / verse".t(), "Roots".t(), "Lemmas".t() })
                grid.Add(TextBlock(header).Tiny().SemiBold().Secondary());

            foreach (var s in rows)
            {
                var captured = s;

                grid.Add(TextBlock(captured.Number.ToString()).Small().Secondary());
                grid.Add(Arabic.Inline(captured.NameArabic, 20).OnClick((_, __) => Lists.Open(captured.Uid)).Style(st => st.setProperty("cursor", "pointer")));
                grid.Add(TextBlock(captured.Name).Small().SemiBold().OnClick((_, __) => Lists.Open(captured.Uid)).Style(st => st.setProperty("cursor", "pointer")));
                grid.Add(TextBlock(captured.NameEnglish).Small().Secondary());
                grid.Add(Badge(captured.Place).Foreground(Palette.ForPlace(captured.Place)).Outline());
                grid.Add(TextBlock("#" + captured.RevelationOrder).Small());
                grid.Add(TextBlock(Panels.Number(captured.Verses)).Small());
                grid.Add(TextBlock(Panels.Number(captured.Words)).Small());
                grid.Add(TextBlock(captured.WordsPerVerse.ToString("0.0")).Small().SemiBold());
                grid.Add(TextBlock(Panels.Number(captured.DistinctRoots)).Small());
                grid.Add(TextBlock(Panels.Number(captured.DistinctLemmas)).Small());
            }

            return grid;
        }

        private static string Share(int part, int whole) => whole <= 0 ? "0 %" : (100.0 * part / whole).ToString("0") + " %";
    }
}
