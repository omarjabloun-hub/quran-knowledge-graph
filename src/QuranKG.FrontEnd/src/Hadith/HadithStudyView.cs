using System;
using System.Linq;
using Mosaik;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Hadith
{
    /// <summary>
    /// Hadith and the Quran: Sahih al-Bukhari and Sahih Muslim against the text. How many of their
    /// hadith quote a verse, measured; the verses and surahs they quote most; their books by how
    /// much they quote; and every hadith to read, filtered by collection, by how it meets the Quran,
    /// by a surah it quotes, or by words. Whether a hadith is supported by the Quran, explains or
    /// adds to it, or seems to sit in tension with it, is argued with the assistant, case by case.
    /// </summary>
    internal sealed class HadithStudyView : IComponent
    {
        internal const string Route = "#/hadith";

        internal static bool IsShowing() => (window.location.hash ?? "").StartsWith(Route, StringComparison.Ordinal);

        private const int PAGE = 20;

        private readonly IComponent _content;
        private readonly Stack      _results = VStack().WS();
        private readonly Stack      _filters = VStack().WS().Gap(8.px());
        private readonly QK.HadithListRequest _query = new QK.HadithListRequest { Kind = "quotation", Take = PAGE };
        private QK.HadithOverviewDto _overview;

        public HadithStudyView(Parameters state)
        {
            _content = VStack().S().ScrollY().Children(Defer(async () =>
            {
                _overview = await QuranApi.HadithOverviewAsync();
                return Build();
            }, TextBlock("Reading Bukhari and Muslim…".t()).Secondary().P(24)));
        }

        public HTMLElement Render() => _content.Render();

        private IComponent Build()
        {
            var o = _overview;
            var page = VStack().WS().P(24).Gap(16.px()).MaxWidth(1300.px()).Children(
                TextBlock("Hadith and the Quran".t()).MediumPlus().SemiBold(),
                TextBlock("Sahih al-Bukhari and Sahih Muslim, in Arabic and English, read against the Quran. A hadith meets a verse in words when its Arabic quotes it (the verse set in braces by the edition, six words or more, or the whole of a short verse) or shares a phrase with it (four or five words); every hadith also has the verses closest to it in meaning. Whether a hadith is supported by the Quran, explains or details a verse, adds something the Quran does not state, or seems to sit in tension with it is a reading, argued with the evidence — open a hadith, or ask the assistant.".t()).Small().Secondary());

            var cards = HStack().WS().Gap(16.px()).Wrap();
            foreach (var c in o.Collections ?? new QK.HadithCollectionStats[0]) cards.Add(CollectionCard(c));
            page.Add(cards);

            page.Add(Panels.Row(
                Panels.Panel("The verses they quote most".t(), "By the number of hadith whose Arabic quotes the verse — set in braces by the edition, six words or more, or the whole of a short verse. Shared phrases are left out: many are formulas.".t(),
                             Lists.RankedBars(o.TopVerses ?? new QK.Counted[0], HadithView.Color, arabicLabels: false)),
                Panels.Panel("By surah".t(), "Hadith quoting one of its verses. Click a surah to list them below (with the phrases they share too).".t(), SurahBars(o.Surahs ?? new QK.Counted[0]))));

            page.Add(Panels.Panel("The books".t(), "Each book of the two collections, with the share of its hadith that quote the Quran.".t(), Books(o.Books ?? new QK.HadithBookStats[0])));

            BuildFilters();
            page.Add(Panels.Panel("Read the hadith".t(), null, VStack().WS().Gap(12.px()).Children(_filters, _results)));
            Search();
            return page;
        }

        private static IComponent CollectionCard(QK.HadithCollectionStats c)
        {
            double Share(int n) => c.Hadith == 0 ? 0 : 100.0 * n / c.Hadith;
            return Card(VStack().Gap(6.px()).Children(
                       HStack().AlignItemsCenter().Gap(10.px()).Children(Arabic.Inline(c.NameArabic, 24), TextBlock(c.Name).SemiBold()),
                       TextBlock(string.Format("{0} hadith ({1} only repeat the chain of the one before)".t(), Panels.Number(c.Hadith), Panels.Number(c.Variants))).Small(),
                       TextBlock(string.Format("{0} quote a verse ({1:0.0}%) · {2} more share a phrase with one".t(), Panels.Number(c.Quoting), Share(c.Quoting), Panels.Number(c.Phrase))).Small(),
                       TextBlock(string.Format("Measured: the translator cites a verse in {0} hadith; the Arabic quotes it in {1} ({2:0}%)".t(), c.TranslatorCited, c.TranslatorFound, c.TranslatorCited == 0 ? 0 : 100.0 * c.TranslatorFound / c.TranslatorCited)).Tiny().Secondary(),
                       TextBlock(string.Format("{0} embedded for meaning".t(), Panels.Number(c.Embedded))).Tiny().Secondary()))
                   .OnClick(() => Lists.Open(c.Uid)).MinWidth(360.px());
        }

        private IComponent SurahBars(QK.Counted[] surahs)
        {
            var max  = Math.Max(1, surahs.Select(s => s.Count).DefaultIfEmpty(0).Max());
            var list = VStack().WS().Gap(2.px()).MaxHeight(420.px()).ScrollY();
            foreach (var s in surahs.Where(x => x.Count > 0).OrderByDescending(x => x.Count))
            {
                var number = int.Parse(s.Label.Split('.')[0]);
                var bar = Div(Att(""));
                bar.style.height = "10px";
                bar.style.borderRadius = "3px";
                bar.style.background = HadithView.Color;
                bar.style.width = (100.0 * s.Count / max).ToString("0.0") + "%";
                var line = HStack().WS().AlignItemsCenter().Gap(8.px()).Children(
                        TextBlock(s.Label).Small().W(170.px()).NoWrap().Ellipsis(),
                        Arabic.Inline(s.Detail, 16).W(80.px()),
                        Raw(bar).Grow(),
                        TextBlock(s.Count.ToString()).Small().W(40.px()))
                    .Style(st => st.setProperty("cursor", "pointer"));
                line.Render().title   = string.Format("List the hadith quoting {0}".t(), s.Label);
                line.Render().onclick = _ => { _query.Surah = number; _query.Kind = "any"; _query.Skip = 0; BuildFilters(); Search(); };
                list.Add(line);
            }
            return list;
        }

        private static IComponent Books(QK.HadithBookStats[] books)
        {
            var columns = HStack().WS().Gap(24.px()).Wrap();
            foreach (var collection in books.GroupBy(b => b.CollectionId))
            {
                var list = VStack().Gap(2.px()).MinWidth(420.px()).Grow();
                list.Add(TextBlock(collection.Key == "muslim" ? "Sahih Muslim" : "Sahih al-Bukhari").SemiBold().PB(4));
                foreach (var b in collection.OrderByDescending(x => x.Hadith == 0 ? 0 : (double)x.Quoting / x.Hadith).Take(15))
                {
                    var captured = b;
                    var share = b.Hadith == 0 ? 0 : 100.0 * b.Quoting / b.Hadith;
                    var line = HStack().WS().AlignItemsCenter().Gap(8.px()).Children(
                            TextBlock(b.Name).Small().NoWrap().Ellipsis().Grow(),
                            Arabic.Inline(b.NameArabic, 15),
                            TextBlock(string.Format("{0}/{1} · {2:0}%".t(), b.Quoting, b.Hadith, share)).Tiny().Secondary().W(110.px()))
                        .Style(st => st.setProperty("cursor", "pointer"));
                    line.Render().onclick = _ => Lists.Open(captured.Uid);
                    list.Add(line);
                }
                columns.Add(list);
            }
            return VStack().WS().Children(
                TextBlock("The fifteen books that quote the Quran most, by share; open one for all its hadith.".t()).Tiny().Secondary().PB(6),
                columns);
        }

        // -----------------------------------------------------------------------------------
        // the list
        // -----------------------------------------------------------------------------------

        private void BuildFilters()
        {
            _filters.Clear();

            IComponent Choice(string label, bool selected, Action pick)
            {
                var b = Button(label).Compact();
                if (selected) b.Primary(); else b.OnClick(() => { pick(); _query.Skip = 0; BuildFilters(); Search(); });
                return b;
            }

            var collection = (_query.Collection ?? "");
            var kind       = _query.Kind ?? "quotation";

            var search = SearchBox("Words in Arabic or English, or a reference: Bukhari 6018, Muslim 8a".t()).WS();
            search.Text = _query.Text ?? "";
            search.OnSearch((_, text) => { _query.Text = text; _query.Skip = 0; Search(); });

            var row = HStack().WS().AlignItemsCenter().Gap(6.px()).Wrap().Children(
                Choice("Both".t(),       collection == "",        () => _query.Collection = ""),
                Choice("Bukhari".t(),    collection == "bukhari", () => _query.Collection = "bukhari"),
                Choice("Muslim".t(),     collection == "muslim",  () => _query.Collection = "muslim"),
                TextBlock("·").Secondary(),
                Choice("Quote a verse".t(),     kind == "quotation",  () => _query.Kind = "quotation"),
                Choice("Share a phrase".t(),    kind == "phrase",     () => _query.Kind = "phrase"),
                Choice("Translator's citation".t(), kind == "translator", () => _query.Kind = "translator"),
                Choice("Every hadith".t(),      kind == "all",        () => _query.Kind = "all"));

            if (_query.Surah > 0)
                row.Add(Button(string.Format("Surah {0} ✕".t(), _query.Surah)).Compact().Tooltip("Show every surah".t())
                           .OnClick(() => { _query.Surah = 0; _query.Skip = 0; BuildFilters(); Search(); }));

            _filters.Add(row);
            _filters.Add(search);
        }

        private void Search()
        {
            _results.Clear();
            _results.Add(Defer(async () =>
            {
                var page = await QuranApi.HadithListAsync(_query);
                var block = VStack().WS();
                block.Add(TextBlock(string.Format("{0} hadith".t(), Panels.Number(page.Total))).Small().Secondary().PB(8));
                foreach (var row in page.Rows ?? new QK.HadithRow[0]) block.Add(HadithView.Row(row));

                var nav = HStack().AlignItemsCenter().Gap(8.px());
                var previous = Button("Previous".t()).Compact();
                var next     = Button("Next".t()).Compact();
                if (_query.Skip <= 0) previous.Disabled(); else previous.OnClick(() => { _query.Skip = Math.Max(0, _query.Skip - PAGE); Search(); });
                if (_query.Skip + PAGE >= page.Total) next.Disabled(); else next.OnClick(() => { _query.Skip += PAGE; Search(); });
                nav.Children(previous, TextBlock(page.Total == 0 ? "" : string.Format("{0}–{1}".t(), _query.Skip + 1, Math.Min(page.Total, _query.Skip + PAGE))).Small().Secondary(), next);
                block.Add(nav);
                return block;
            }, TextBlock("Reading…".t()).Small().Secondary()));
        }
    }
}
