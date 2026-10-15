using System;
using System.Linq;
using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Dashboard;
using QuranKG.FrontEnd.Themes;
using Tesserae;
using TNT;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Home
{
    /// <summary>
    /// The home page. The search box first (<see cref="HomeSearch"/>), with where the reader left
    /// off and the recitation under it; then three ways in — continue reading, listen, the stories
    /// and themes —; then the 114 surahs, by the order of the book or of revelation; and last the
    /// text's numbers, each opening the Patterns page (<see cref="PatternDashboard"/>). Every number
    /// is read from the graph (qkg/dashboard/corpus, qkg/clusters/themes).
    /// </summary>
    internal sealed class HomeView : IComponent
    {
        private readonly IComponent _content;

        public HomeView(Parameters state)
        {
            Look.EnsureStyles();
            EnsureSetup();

            _content = Look.Page(VStack().WS().Gap(40.px()).Children(
                HomeSearch.Hero(),
                Defer(async () =>
                {
                    var corpus = await QuranApi.CorpusAsync();
                    var surahs = corpus.Overview.SurahRows.OrderBy(s => s.Number).ToArray();
                    return VStack().WS().Gap(40.px()).Children(
                        Ways(surahs),
                        SurahIndex(surahs),
                        Numbers(corpus.Overview));
                }, Raw(Look.El("p", "qkg-note", "Counting the Quran…".t())))));
        }

        public HTMLElement Render() => _content.Render();

        // -----------------------------------------------------------------------------------

        /// <summary>Continue reading, listen, and the themes — three cards.</summary>
        private static IComponent Ways(QK.SurahStats[] surahs)
        {
            var (last, ayah) = QuranLinks.LastRead();
            var current = surahs.FirstOrDefault(s => s.Number == (last > 0 ? last : 1));

            var cont = Look.El("div", "qkg-card qkg-way");
            cont.appendChild(Look.El("span", "qkg-kicker", last > 0 ? "Continue reading".t() : "Start reading".t()));
            var row = Look.El("div", "qkg-way-row");
            row.appendChild(Look.El("div", "qkg-way-text",
                Look.El("span", "qkg-way-title", current?.Name),
                Look.El("span", "qkg-note", current is null ? "" : ayah > 0 ? string.Format("{0} · verse {1} of {2}".t(), current.NameEnglish, ayah, current.Verses)
                                                                             : string.Format("{0} · {1} verses".t(), current.NameEnglish, current.Verses))));
            row.appendChild(Look.Ar(current?.NameArabic, "qkg-arw qkg-way-ar"));
            cont.appendChild(row);
            if (current is object) cont.appendChild(Look.Track(ayah > 0 ? (double)ayah / Math.Max(1, current.Verses) : 0, null, string.Format("Verse {0} of {1}".t(), ayah, current.Verses)));
            Look.OnClick(cont, () => QuranLinks.Go(last > 0 ? last : 1, ayah));

            var listen = Look.El("div", "qkg-card qkg-way");
            listen.appendChild(Look.El("span", "qkg-kicker", "Listen".t()));
            var lrow = Look.El("div", "qkg-way-row");
            lrow.appendChild(Look.El("span", "qkg-way-play", "▶"));
            lrow.appendChild(Look.El("div", "qkg-way-text",
                Look.El("span", "qkg-way-title qkg-way-title-sm", "Sheikh Mohamed (Hamadi) Latrech"),
                Look.El("span", "qkg-note", "Qalun from Nafiʿ · the text follows the reciter".t())));
            listen.appendChild(lrow);
            Look.OnClick(listen, () =>
            {
                QuranLinks.ListenOnArrival = true;
                QuranLinks.Go(last > 0 ? last : 1, last > 0 ? ayah : 0);
            });

            var themes = Defer(async () =>
            {
                var result = await QuranApi.ThemesAsync();
                var card   = Look.El("div", "qkg-card qkg-way");
                card.appendChild(Look.El("span", "qkg-kicker", "Stories and themes".t()));
                if (!result.Built)
                {
                    card.appendChild(Look.El("span", "qkg-note", "The themes have not been built yet (`just clusters`).".t()));
                    return Raw(card);
                }
                var list = result.Themes ?? new QK.ThemeRow[0];
                card.appendChild(Look.El("span", "qkg-way-title qkg-way-title-sm", string.Format("{0} themes the surahs share".t(), list.Length)));
                var chips = Look.El("div", "qkg-way-chips");
                foreach (var t in list.Where(x => !string.IsNullOrEmpty(x.LabelArabic)).Skip(3).Take(2))
                {
                    var chip = Look.El("span", "qkg-chip");
                    chip.appendChild(Look.Ar(t.LabelArabic));
                    chips.appendChild(chip);
                }
                card.appendChild(chips);
                Look.OnClick(card, () => Router.Navigate(ThemesView.Route));
                return Raw(card);
            });

            return Look.Columns(300, Raw(cont), Raw(listen), themes);
        }

        // -----------------------------------------------------------------------------------

        /// <summary>The 114 surahs as cards, by the order of the book or of revelation, either way round.</summary>
        private static IComponent SurahIndex(QK.SurahStats[] surahs)
        {
            var grid  = Look.El("div", "qkg-surah-grid");
            var order = 0;
            var ascending = true;

            void Draw()
            {
                grid.innerHTML = "";
                var rows = order == 1 ? surahs.OrderBy(s => s.RevelationOrder) : surahs.OrderBy(s => s.Number);
                foreach (var s in ascending ? rows.ToArray() : rows.Reverse().ToArray()) grid.appendChild(SurahCard(s, order == 1));
            }

            HTMLElement direction = null;
            direction = Look.Button("Ascending".t(), () =>
            {
                ascending = !ascending;
                direction.querySelector("span:last-child").As<HTMLElement>().textContent = ascending ? "Ascending".t() : "Descending".t();
                Draw();
            }, "ghost", "↕");

            var tools = Look.El("div", "qkg-surah-tools",
                Look.Segmented(new[] { "Surah".t(), "Revelation order".t() }, 0, i => { order = i; Draw(); }, "List the surahs by".t()),
                direction);

            Draw();

            var section = VStack().WS().Gap(20.px()).Children(
                Look.Heading("The 114 surahs".t(), null, Raw(tools)),
                Raw(grid).WS());
            section.Render().id = "qkg-surahs";
            return section;
        }

        private static HTMLElement SurahCard(QK.SurahStats s, bool revelation)
        {
            var card = Look.El("div", "qkg-card qkg-surah-card");
            card.appendChild(Look.SurahNumber(revelation ? s.RevelationOrder : s.Number));
            card.appendChild(Look.El("div", "qkg-surah-card-text",
                Look.El("span", "qkg-surah-card-name", (revelation ? s.Number + ". " : "") + s.Name),
                Look.El("span", "qkg-note", s.NameEnglish)));

            var side = Look.El("div", "qkg-surah-card-side");
            side.appendChild(Look.Ar(s.NameArabic, "qkg-arw qkg-surah-card-ar"));
            var meta = Look.El("span", "qkg-surah-card-meta");
            var dot  = Look.El("span", "qkg-dot");
            dot.style.background = s.Place == QK.MEDINAN ? "var(--qkg-medinan)" : "var(--qkg-meccan)";
            dot.title = s.Place;
            meta.appendChild(dot);
            meta.appendChild(Look.El("span", "", string.Format("{0} verses".t(), s.Verses)));
            side.appendChild(meta);
            card.appendChild(side);

            card.title = s.Number + ". " + s.Name + " — " + s.Place + (revelation ? " · " + string.Format("revealed #{0}".t(), s.RevelationOrder) : "");
            var number = s.Number;
            return Look.OnClick(card, () => QuranLinks.Go(number));
        }

        // -----------------------------------------------------------------------------------

        /// <summary>The text's numbers, read live; each opens the Patterns page.</summary>
        private static IComponent Numbers(QK.OverviewDto o)
        {
            void Open() => Router.Navigate(PatternDashboard.Route);

            var panel = VStack().WS().Gap(20.px()).Class("qkg-panel").Children(
                Look.Heading("Patterns in the text".t(), null, Raw(Look.OnClick(Look.El("span", "qkg-link", "Every pattern →".t()), Open))),
                Look.Columns(150,
                    Look.Stat(Panels.Number(o.Surahs),        "surahs".t(),              Open),
                    Look.Stat(Panels.Number(o.Verses),        "verses".t(),              Open),
                    Look.Stat(Panels.Number(o.Words),         "words".t(),               Open),
                    Look.Stat(Panels.Number(o.DistinctForms), "distinct word forms".t(), Open),
                    Look.Stat(Panels.Number(o.Lemmas),        "lemmas".t(),              Open),
                    Look.Stat(Panels.Number(o.Roots),         "roots".t(),               Open)),
                Raw(Look.El("p", "qkg-note", "Counted live from the graph: the Arabic of every verse, and the root, lemma and part of speech of every word.".t())));
            return panel;
        }

        // -----------------------------------------------------------------------------------

        private static bool _slashBound;

        /// <summary>"/" anywhere outside a text field puts the cursor in the home page's search box.</summary>
        private static void EnsureSetup()
        {
            if (_slashBound) return;
            _slashBound = true;
            document.addEventListener("keydown", e =>
            {
                var key = e.As<KeyboardEvent>();
                if (key.key != "/" || key.ctrlKey || key.metaKey || key.altKey) return;
                var active = document.activeElement;
                var tag    = active is null ? "" : (active.tagName ?? "").ToLowerInvariant();
                if (tag == "input" || tag == "textarea" || (active is object && active.As<HTMLElement>().isContentEditable)) return;
                var box = document.querySelector("[data-qkg-search]");
                if (box is null) return;
                e.preventDefault();
                box.As<HTMLElement>().focus();
            });

            var style = document.createElement("style");
            style.textContent = CSS.Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        private const string CSS = @"
.qkg-way { display: flex; flex-direction: column; gap: 14px; min-height: 100%; }
.qkg-way-row { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
.qkg-way-text { display: flex; flex-direction: column; min-width: 0; flex: 1 1 auto; }
.qkg-way-title { font-size: 20px; font-weight: 600; }
.qkg-way-title-sm { font-size: 17px; }
.qkg-way-ar { font-size: 34px; color: var(--qkg-teal-strong); }
.qkg-way-play { display: inline-flex; align-items: center; justify-content: center; width: 48px; height: 48px; flex: none; border-radius: 50%; background: var(--qkg-teal); color: var(--qkg-on-teal); font-size: 16px; order: -1; }
.qkg-way-chips { display: flex; flex-wrap: wrap; gap: 8px; }
.qkg-surah-tools { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.qkg-surah-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(320px, 100%), 1fr)); gap: 12px; }
.qkg-surah-card { display: flex; align-items: center; gap: 14px; padding: 14px 18px; }
.qkg-surah-card-text { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; }
.qkg-surah-card-name { font-weight: 600; }
.qkg-surah-card-side { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
.qkg-surah-card-ar { font-size: 22px; line-height: 1.9; display: block; margin-top: -10px; }
.qkg-surah-card-meta { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--qkg-ink-3); }
";
    }
}
