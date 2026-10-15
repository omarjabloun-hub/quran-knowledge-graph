using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using QuranKG.FrontEnd.Themes;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Dashboard
{
    /// <summary>
    /// The Patterns page (#/patterns): what anyone can see in the text, counted live from the graph. One call
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
        internal const string Route = "#/patterns";

        /// <summary>Whether the page on screen is this one — what lights its sidebar entry.</summary>
        internal static bool IsShowing() => (window.location.hash ?? "").StartsWith(Route, StringComparison.Ordinal);

        private readonly IComponent _content;

        public PatternDashboard(Parameters state)
        {
            EnsureStyles();
            _content = Look.Page(Defer(async () =>
                {
                    var corpus = await QuranApi.CorpusAsync();
                    var o      = corpus.Overview;

                    return VStack().WS().Gap(28.px()).Children(
                        Header(corpus),
                        Totals(o),

                        Look.Heading("Meccan and Medinan".t(),
                                     "Each surah's attribution to the years in Mecca or in Medina, as Tanzil gives it.".t()),
                        Periods(corpus),

                        VerseLengths(o),

                        Look.Heading("The vocabulary".t(),
                                     "Counted over the Arabic: roots and lemmas from the Quranic Arabic Corpus morphology. Click a label to open it.".t()),
                        Vocabulary(o),

                        Look.Heading("Where the roots live".t(),
                                     "The thirty most frequent roots across the 30 juz or the 114 surahs. Click a cell for the verses where that root occurs there; click a root or a number to open it.".t()),
                        RootMap(corpus),

                        Look.Heading("Stories and themes".t(),
                                     "Passages of different surahs that lean on the same distinctive words: the stories the Quran tells more than once, and the subjects it returns to.".t(),
                                     Raw(Look.OnClick(Look.El("span", "qkg-link", "All themes →".t()), () => Router.Navigate(ThemesView.Route)))),
                        ThemesView.Preview(6),

                        Look.Heading("Every surah".t()),
                        SurahTable(o));
                }, Raw(Look.El("p", "qkg-note", "Counting the Quran…".t()))));
        }

        public HTMLElement Render() => _content.Render();

        // -----------------------------------------------------------------------------------
        // header and totals
        // -----------------------------------------------------------------------------------

        private static IComponent Header(QK.CorpusDto c)
        {
            var o = c.Overview;
            return Look.Title("Patterns in the Quran".t(),
                string.Format("Counted live from the graph: the Arabic of the {0} verses, and the root, lemma and part of speech of each of its {1} words. Every number opens what it counted.".t(),
                              Panels.Number(o.Verses), Panels.Number(o.Words)),
                Raw(Look.El("span", "qkg-note", string.Format("Computed in {0} s".t(), (c.Milliseconds / 1000).ToString("0.0")))));
        }

        private static IComponent Totals(QK.OverviewDto o)
        {
            var parts = Look.Card(null,
                Raw(Look.El("div", "qkg-keys",
                    Look.El("h3", "qkg-h3", "Parts of speech".t()),
                    Look.El("span", "qkg-note", string.Format("{0} of the words are nouns".t(), Share(o.Nouns, o.Words))))),
                Raw(Look.Parts(string.Format("Nouns {0}, verbs {1}, particles {2}".t(), o.Nouns, o.Verbs, o.Particles),
                               (o.Nouns, Palette.Noun), (o.Verbs, Palette.Verb), (o.Particles, Palette.Other))),
                Raw(Look.El("div", "qkg-keys",
                    Look.Key(Palette.Noun,  "Nouns".t(),     Panels.Number(o.Nouns)),
                    Look.Key(Palette.Verb,  "Verbs".t(),     Panels.Number(o.Verbs)),
                    Look.Key(Palette.Other, "Particles".t(), Panels.Number(o.Particles)))));

            return VStack().WS().Gap(14.px()).Children(
                Look.Columns(160,
                    Look.Stat(Panels.Number(o.Surahs),        "surahs".t(),              () => Lists.ShowNodesOfType("Surahs".t(), N.Surah.Type), "س", "var(--qkg-teal-tint)", "var(--qkg-teal-strong)"),
                    Look.Stat(Panels.Number(o.Verses),        "verses".t(),              () => Lists.ShowNodesOfType("Verses".t(), N.Verse.Type), "۝", "var(--qkg-gold-tint)", "var(--qkg-gold)"),
                    Look.Stat(Panels.Number(o.Words),         "words".t(),               () => Lists.ShowNodesOfType("Words".t(), N.Word.Type), "T"),
                    // Distinct forms are not nodes: nothing to open, so the tile does not invite a click.
                    Look.Stat(Panels.Number(o.DistinctForms), "distinct word forms".t(), null, "≡"),
                    Look.Stat(Panels.Number(o.Lemmas),        "lemmas".t(),              () => Lists.ShowNodesOfType("Lemmas".t(), N.Lemma.Type), "Aa", "var(--qkg-lemma-tint)", "var(--qkg-lemma)"),
                    Look.Stat(Panels.Number(o.Roots),         "roots".t(),               () => Lists.ShowNodesOfType("Roots".t(), N.Root.Type), "ج", "var(--qkg-root-tint)", "var(--qkg-root)")),
                parts);
        }

        // -----------------------------------------------------------------------------------
        // Meccan and Medinan
        // -----------------------------------------------------------------------------------

        private static IComponent Periods(QK.CorpusDto c)
        {
            var places  = c.Overview.Places ?? new QK.PlaceStats[0];
            var meccan  = places.FirstOrDefault(p => p.Place == QK.MECCAN);
            var medinan = places.FirstOrDefault(p => p.Place == QK.MEDINAN);
            if (meccan is null || medinan is null) return Raw(Look.El("p", "qkg-note", "No revelation places in the graph.".t()));

            var finding = string.Format("A Medinan verse runs {0}× as long as a Meccan one. Medinan surahs are {1} of {2}, yet hold {3} of the words.".t(),
                                        (medinan.WordsPerVerse / Math.Max(0.1, meccan.WordsPerVerse)).ToString("0.0"),
                                        medinan.Surahs, meccan.Surahs + medinan.Surahs, Share(medinan.Words, meccan.Words + medinan.Words));

            var shares = Look.Card(null, Raw(Look.El("p", "", finding)));
            foreach (var (what, m, d) in new[] { ("Surahs".t(), meccan.Surahs, medinan.Surahs), ("Verses".t(), meccan.Verses, medinan.Verses), ("Words".t(), meccan.Words, medinan.Words) })
                shares.Add(Raw(Look.El("div", "qkg-share-row",
                    Look.El("span", "qkg-note", what),
                    Look.Parts(string.Format("{0}: Meccan {1}, Medinan {2}".t(), what, m, d), (m, Palette.Meccan), (d, Palette.Medinan)))));
            shares.Add(Raw(Look.El("div", "qkg-keys", Look.Key(Palette.Meccan, "Meccan".t()), Look.Key(Palette.Medinan, "Medinan".t()))));

            var distinctive = c.Distinctive;

            return VStack().WS().Gap(14.px()).Children(
                Look.Columns(300, PlaceCard(meccan), PlaceCard(medinan), shares),
                Look.Columns(420,
                    Look.Panel("Most Meccan roots".t(),
                               "Used far more in Meccan surahs than their share of the text predicts (log-odds with an informative prior, z-scored). Hover a bar for the counts.".t(),
                               Charts.Bars((distinctive.Meccan ?? new QK.Counted[0]).Take(12).ToArray(), Palette.Meccan, value: r => r.Score, valueText: r => r.Score.ToString("0.0"))),
                    Look.Panel("Most Medinan roots".t(),
                               "The same measure, the other way round.".t(),
                               Charts.Bars((distinctive.Medinan ?? new QK.Counted[0]).Take(12).ToArray(), Palette.Medinan, value: r => -r.Score, valueText: r => (-r.Score).ToString("0.0")))));
        }

        private static IComponent PlaceCard(QK.PlaceStats p)
        {
            var medinan = p.Place == QK.MEDINAN;
            var card = Look.El("div", "qkg-card qkg-place qkg-place-" + (medinan ? "medinan" : "meccan"));
            card.appendChild(Look.El("span", "qkg-kicker", p.Place));
            card.appendChild(Look.El("div", "qkg-place-row",
                Look.El("span", "qkg-stat-value qkg-num qkg-place-value", p.WordsPerVerse.ToString("0.0")),
                Look.El("span", "qkg-note", "words per verse".t())));
            card.appendChild(Look.El("span", "qkg-note qkg-num", string.Format("{0} surahs · {1} verses · {2} words".t(), p.Surahs, Panels.Number(p.Verses), Panels.Number(p.Words))));
            Look.OnClick(card, () => Lists.Open(p.Uid));
            return Raw(card);
        }

        // -----------------------------------------------------------------------------------
        // verse length, in the book's order and in the order of revelation
        // -----------------------------------------------------------------------------------

        private static IComponent VerseLengths(QK.OverviewDto o)
        {
            var mushaf     = o.SurahRows.OrderBy(s => s.Number).ToArray();
            var revelation = o.SurahRows.OrderBy(s => s.RevelationOrder).ToArray();
            var max        = Math.Ceiling(o.SurahRows.Max(s => s.WordsPerVerse) / 5) * 5;
            var host       = VStack().WS();
            var note       = Look.El("p", "qkg-note");

            void Show(bool byRevelation)
            {
                var rows = byRevelation ? revelation : mushaf;
                note.textContent = byRevelation
                    ? "The surahs in the traditional chronological order Tanzil gives: left to right, the short Meccan verses give way to the long Medinan ones.".t()
                    : string.Format("Surahs 1 to {0} as the mushaf orders them, broadly longest first.".t(), rows.Length);
                host.Clear();
                host.Add(LengthChart(rows, max, byRevelation));
            }

            Show(false);
            var longest = mushaf.OrderByDescending(s => s.WordsPerVerse).First();

            return Look.Card(null,
                Look.Heading("Verse length".t(), "Average words per verse in each surah. One bar per surah; click one to open it.".t(),
                    Raw(Look.Segmented(new[] { "Order of the book".t(), "Order of revelation".t() }, 0, i => Show(i == 1), "Order".t()))),
                Raw(note),
                host,
                Raw(Look.El("div", "qkg-keys",
                    Look.Key(Palette.Meccan, "Meccan".t()), Look.Key(Palette.Medinan, "Medinan".t()),
                    Look.El("span", "qkg-note", string.Format("Longest: {0}. {1}, {2} words per verse".t(), longest.Number, longest.Name, longest.WordsPerVerse.ToString("0.0"))))));
        }

        private static IComponent LengthChart(QK.SurahStats[] surahs, double max, bool byRevelation)
            => Charts.Columns(surahs.Select(s => s.WordsPerVerse).ToArray(),
                              i => Palette.ForPlace(surahs[i].Place),
                              i => string.Format("{0}. {1} — {2} words per verse{3}".t(), surahs[i].Number, surahs[i].Name, surahs[i].WordsPerVerse.ToString("0.0"),
                                                 byRevelation ? " · " + string.Format("revealed #{0}".t(), surahs[i].RevelationOrder) : ""),
                              i => Lists.Open(surahs[i].Uid),
                              height: 220, max: max,
                              xLabels: new[] { "1", "20", "40", "60", "80", "100", surahs.Length.ToString() },
                              label: "Words per verse in each surah".t());

        // -----------------------------------------------------------------------------------
        // vocabulary
        // -----------------------------------------------------------------------------------

        private static IComponent Vocabulary(QK.OverviewDto o)
            => Look.Columns(320,
                   Look.Panel("Most frequent roots".t(),
                              "How many words of the text are built on each root.".t(),
                              Charts.Bars(o.TopRoots, Palette.Root)),
                   Look.Panel("Most frequent nouns".t(),
                              "Noun lemmas; pronouns left out.".t(),
                              Charts.Bars(o.TopNouns, Palette.Noun)),
                   Look.Panel("Most frequent verbs".t(),
                              "Verb lemmas, all forms and tenses together.".t(),
                              Charts.Bars(o.TopVerbs, Palette.Verb)));

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

            return Look.Card(null,
                HStack().WS().Wrap().Gap(24.px()).Children(scope, unit),
                note,
                host);
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

            return Look.Card(null, order, host);
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
                grid.Add(Raw(Look.PlaceBadge(captured.Place)));
                grid.Add(TextBlock("#" + captured.RevelationOrder).Small());
                grid.Add(TextBlock(Panels.Number(captured.Verses)).Small());
                grid.Add(TextBlock(Panels.Number(captured.Words)).Small());
                grid.Add(TextBlock(captured.WordsPerVerse.ToString("0.0")).Small().SemiBold());
                grid.Add(TextBlock(Panels.Number(captured.DistinctRoots)).Small());
                grid.Add(TextBlock(Panels.Number(captured.DistinctLemmas)).Small());
            }

            return grid;
        }

        private static string Share(int part, int whole) => whole <= 0 ? "0%" : (100.0 * part / whole).ToString("0") + "%";

        private static bool _styled;

        internal static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-share-row { display: grid; grid-template-columns: 56px minmax(0, 1fr); gap: 10px; align-items: center; }
.qkg-place { display: flex; flex-direction: column; gap: 8px; border-top-width: 4px; }
.qkg-place-meccan { border-top-color: #d4914a; }
.qkg-place-medinan { border-top-color: #2c5aa0; }
.qkg-place-meccan .qkg-kicker { color: var(--qkg-meccan); }
.qkg-place-medinan .qkg-kicker { color: var(--qkg-medinan); }
.qkg-place-row { display: flex; align-items: baseline; gap: 10px; }
.qkg-place-value { font-size: 40px; }
";
            document.head.appendChild(style);
        }
    }
}
