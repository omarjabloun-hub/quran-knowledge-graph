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
            Look.EnsureStyles();
            EnsureStyles();
            _content = Look.Page(Defer(async () =>
            {
                _overview = await QuranApi.HadithOverviewAsync();
                return Build();
            }, Raw(Look.El("p", "qkg-note", "Reading Bukhari and Muslim…".t()))));
        }

        public HTMLElement Render() => _content.Render();

        private IComponent Build()
        {
            var o = _overview;
            var page = VStack().WS().Gap(28.px()).Children(
                Look.Title("Hadith and the Quran".t(),
                    "Sahih al-Bukhari and Sahih Muslim, in Arabic and English, read against the Quran. A hadith quotes a verse when its Arabic carries it — set in braces by the edition, six words or more, or a whole short verse — and shares a phrase with it at four or five words. Whether a hadith is supported by the Quran, explains or adds to a verse, or seems to sit in tension with it, is a reading: open a hadith, or ask the assistant.".t()));

            page.Add(Look.Columns(340, (o.Collections ?? new QK.HadithCollectionStats[0]).Select(CollectionCard).ToArray()));

            page.Add(Look.Columns(420,
                Look.Panel("The verses they quote most".t(), "By the number of hadith whose Arabic quotes the verse. Shared phrases are left out: many are formulas.".t(),
                           TopVerses(o.TopVerses ?? new QK.Counted[0])),
                VStack().WS().Gap(14.px()).Children(
                    Look.Panel("By surah".t(), "Hadith quoting one of its verses. Pick a surah to list them below (with the phrases they share too).".t(), SurahBars(o.Surahs ?? new QK.Counted[0])),
                    Look.Panel("Books that quote the Quran most".t(), "Share of each book's hadith that quote a verse (books of 20 hadith or more); open one for all its hadith.".t(), Books(o.Books ?? new QK.HadithBookStats[0])))));

            BuildFilters();
            var read = VStack().WS().Gap(14.px()).Children(Look.Heading("Read the hadith".t()), _filters, _results);
            read.Render().id = "qkg-hadith-read";
            page.Add(read);
            Search();
            return page;
        }

        private static IComponent CollectionCard(QK.HadithCollectionStats c)
        {
            double Share(int n) => c.Hadith == 0 ? 0 : 100.0 * n / c.Hadith;
            var card = Look.El("div", "qkg-card qkg-collection");
            card.appendChild(Look.El("div", "qkg-collection-head",
                Look.El("div", "qkg-collection-name", Look.El("span", "qkg-h3", c.Name), Look.El("span", "qkg-note", string.Format("{0} hadith · {1} only repeat the chain of the one before".t(), Panels.Number(c.Hadith), Panels.Number(c.Variants)))),
                Look.Ar(c.NameArabic, "qkg-arw qkg-collection-ar")));
            card.appendChild(Look.El("div", "qkg-collection-big",
                Look.El("span", "qkg-stat-value", Panels.Number(c.Quoting)),
                Look.El("span", "qkg-muted", string.Format("quote a verse · {0:0.0}%".t(), Share(c.Quoting)))));
            card.appendChild(Look.Track(Share(c.Quoting) / 100, null, string.Format("{0:0.0}% of the collection quotes a verse".t(), Share(c.Quoting))));
            card.appendChild(Look.El("span", "qkg-note", string.Format("{0} more share a phrase · the translator cites a verse in {1} hadith; the Arabic quotes it in {2} ({3:0}%) · {4} embedded for meaning".t(),
                Panels.Number(c.Phrase), c.TranslatorCited, c.TranslatorFound, c.TranslatorCited == 0 ? 0 : 100.0 * c.TranslatorFound / c.TranslatorCited, Panels.Number(c.Embedded))));
            var uid = c.Uid;
            Look.OnClick(card, () => Lists.Open(uid));
            return Raw(card);
        }

        private static IComponent TopVerses(QK.Counted[] verses)
        {
            var list = Look.El("div", "qkg-topverses");
            var max  = Math.Max(1, verses.Select(v => v.Count).DefaultIfEmpty(0).Max());
            foreach (var v in verses)
            {
                var key = v.Label;
                var row = Look.El("div", "qkg-topverse");
                row.appendChild(Look.El("span", "qkg-similar-vkey", key));
                row.appendChild(Look.El("div", "qkg-topverse-text",
                    Look.Ar(v.Detail ?? "", "qkg-arw qkg-topverse-ar"),
                    Look.Track((double)v.Count / max)));
                row.appendChild(Look.El("span", "qkg-topverse-n", v.Count.ToString()));
                list.appendChild(Look.OnClick(row, () => VerseReferences.Open(key)));
            }
            return Raw(list).WS();
        }

        private IComponent SurahBars(QK.Counted[] surahs)
        {
            var rows = surahs.Where(x => x.Count > 0).OrderByDescending(x => x.Count).ToArray();
            var list = Look.El("div", "qkg-bars qkg-hadith-surahs");
            var max  = Math.Max(1, rows.Select(x => x.Count).DefaultIfEmpty(0).Max());
            foreach (var s in rows)
            {
                var number = int.Parse(s.Label.Split('.')[0]);
                var line = Look.El("div", "qkg-bar-row qkg-hadith-surah");
                line.appendChild(Look.El("span", "qkg-bar-label", s.Label));
                line.appendChild(Look.Ar(s.Detail, "qkg-arw qkg-hadith-surah-ar"));
                line.appendChild(Look.Track((double)s.Count / max));
                line.appendChild(Look.El("span", "qkg-bar-value", s.Count.ToString()));
                line.title = string.Format("List the hadith quoting {0}".t(), s.Label);
                Look.OnClick(line, () =>
                {
                    _query.Surah = number; _query.Kind = "any"; _query.Skip = 0; BuildFilters(); Search();
                    document.getElementById("qkg-hadith-read")?.scrollIntoView();
                });
                list.appendChild(line);
            }
            return Raw(list).WS();
        }

        private static IComponent Books(QK.HadithBookStats[] books)
        {
            var list = Look.El("div", "qkg-books");
            foreach (var b in books.Where(x => x.Hadith >= 20).OrderByDescending(x => (double)x.Quoting / x.Hadith).Take(10))
            {
                var captured = b;
                var share = 100.0 * b.Quoting / b.Hadith;
                var row = Look.El("div", "qkg-book");
                row.appendChild(Look.El("div", "qkg-book-text",
                    Look.El("span", "qkg-book-name", b.Name),
                    Look.El("span", "qkg-note", (b.CollectionId == "muslim" ? "Muslim" : "Bukhari") + " · " + string.Format("{0} of {1}".t(), b.Quoting, b.Hadith))));
                row.appendChild(Look.Ar(b.NameArabic, "qkg-arw qkg-book-ar"));
                row.appendChild(Look.Badge(share.ToString("0") + "%", "teal"));
                list.appendChild(Look.OnClick(row, () => Lists.Open(captured.Uid)));
            }
            return Raw(list).WS();
        }

        // -----------------------------------------------------------------------------------
        // the list
        // -----------------------------------------------------------------------------------

        private void BuildFilters()
        {
            _filters.Clear();

            var collection = (_query.Collection ?? "");
            var kind       = _query.Kind ?? "quotation";

            HTMLElement Choice(string label, bool selected, Action pick)
                => Look.Chip(label, selected ? (Action)null : () => { pick(); _query.Skip = 0; BuildFilters(); Search(); }, selected);

            var field = Look.El("label", "qkg-field");
            field.appendChild(Look.El("span", "", "⌕"));
            var input = document.createElement("input").As<HTMLInputElement>();
            input.type = "search";
            input.value = _query.Text ?? "";
            input.placeholder = "Words in Arabic or English, or a reference: Bukhari 6018, Muslim 8a".t();
            input.setAttribute("aria-label", "Search the hadith".t());
            field.appendChild(input);
            var generation = 0;
            input.oninput = _ =>
            {
                var gen = ++generation;
                window.setTimeout(__ => { if (gen != generation) return; _query.Text = input.value; _query.Skip = 0; Search(); }, 300);
            };

            var row = Look.El("div", "qkg-keys qkg-hadith-filters",
                Choice("Both".t(),       collection == "",        () => _query.Collection = ""),
                Choice("Bukhari".t(),    collection == "bukhari", () => _query.Collection = "bukhari"),
                Choice("Muslim".t(),     collection == "muslim",  () => _query.Collection = "muslim"),
                Look.El("span", "qkg-hadith-sep"),
                Choice("Quote a verse".t(),         kind == "quotation",  () => _query.Kind = "quotation"),
                Choice("Share a phrase".t(),        kind == "phrase",     () => _query.Kind = "phrase"),
                Choice("Translator's citation".t(), kind == "translator", () => _query.Kind = "translator"),
                Choice("Every hadith".t(),          kind == "all",        () => _query.Kind = "all"));

            if (_query.Surah > 0)
                row.appendChild(Look.Chip(string.Format("Surah {0} ✕".t(), _query.Surah), () => { _query.Surah = 0; _query.Skip = 0; BuildFilters(); Search(); }, true));

            _filters.Add(Look.Card(null, Raw(field).WS(), Raw(row).WS()));
        }

        private void Search()
        {
            _results.Clear();
            _results.Add(Defer(async () =>
            {
                var page  = await QuranApi.HadithListAsync(_query);
                var block = VStack().WS().Gap(12.px());
                block.Add(Raw(Look.El("p", "qkg-note", string.Format("{0} hadith".t(), Panels.Number(page.Total)))));
                foreach (var row in page.Rows ?? new QK.HadithRow[0]) block.Add(HadithView.Row(row));

                var previous = Look.Button("Previous".t(), null, "plain", "‹");
                var next     = Look.Button("Next".t(), null, "plain");
                if (_query.Skip <= 0) previous.setAttribute("disabled", "disabled"); else previous.onclick = _ => { _query.Skip = Math.Max(0, _query.Skip - PAGE); Search(); };
                if (_query.Skip + PAGE >= page.Total) next.setAttribute("disabled", "disabled"); else next.onclick = _ => { _query.Skip += PAGE; Search(); };
                block.Add(Raw(Look.El("div", "qkg-keys", previous,
                    Look.El("span", "qkg-note", page.Total == 0 ? "" : string.Format("{0}–{1}".t(), _query.Skip + 1, Math.Min(page.Total, _query.Skip + PAGE))),
                    next)));
                return block;
            }, Raw(Look.El("p", "qkg-note", "Reading…".t()))));
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-collection { display: flex; flex-direction: column; gap: 14px; }
.qkg-collection-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.qkg-collection-name { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.qkg-collection-ar { font-size: 32px; color: var(--qkg-teal-strong); }
.qkg-collection-big { display: flex; align-items: baseline; gap: 10px; }
.qkg-collection-big .qkg-stat-value { font-size: 36px; }
.qkg-muted { color: var(--qkg-ink-2); }
.qkg-topverses { display: flex; flex-direction: column; }
.qkg-topverse { display: grid; grid-template-columns: 56px minmax(0, 1fr) 36px; gap: 12px; align-items: center; padding: 8px 0; border-top: 1px solid var(--qkg-line); color: var(--qkg-ink); }
.qkg-topverse:hover .qkg-similar-vkey { text-decoration: underline; }
.qkg-topverse-text { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.qkg-topverse-ar { font-size: 19px; line-height: 1.7; text-align: right; display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.qkg-topverse .qkg-track { height: 5px; }
.qkg-topverse-n { text-align: right; font-weight: 600; }
.qkg-hadith-surahs { max-height: 330px; overflow-y: auto; padding-right: 4px; }
.qkg-hadith-surah { grid-template-columns: 130px 70px minmax(0, 1fr) 36px; }
.qkg-hadith-surah-ar { font-size: 16px; color: var(--qkg-ink-3); }
.qkg-books { display: flex; flex-direction: column; }
.qkg-book { display: grid; grid-template-columns: minmax(0, 1fr) auto auto; gap: 10px; align-items: center; padding: 8px 0; border-top: 1px solid var(--qkg-line); color: var(--qkg-ink); }
.qkg-book-text { display: flex; flex-direction: column; min-width: 0; }
.qkg-book-name { font-size: 14px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.qkg-book-ar { font-size: 15px; color: var(--qkg-ink-3); }
.qkg-hadith-sep { width: 1px; height: 22px; background: var(--qkg-line-2); }
.qkg-hadith-filters { gap: 8px; }
";
            document.head.appendChild(style);
        }
    }
}
