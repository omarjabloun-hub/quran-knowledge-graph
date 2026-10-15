using System;
using System.Linq;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Home
{
    /// <summary>
    /// The top of the home page: the Quran's name, one search box in the middle and the ways in
    /// under it — the surahs, where the reader left off, the recitation, the surahs most read.
    ///
    /// <para>The box answers as one types (qkg/search/quick): first what the query names — a verse
    /// ("2:255"), a surah ("Kahf", "الكهف", "18"), a page or a juz of the mushaf ("page 50", "juz
    /// 30"), a hadith ("Bukhari 6018") — then the verses whose words match, in the Arabic (any
    /// spelling) or in the English. ↑ ↓ choose, Enter opens, Esc clears.</para>
    /// </summary>
    internal static class HomeSearch
    {
        private const int TAKE = 8;

        /// <summary>The surahs people open most, as quick links: (surah, verse, label).</summary>
        private static readonly (int Surah, int Ayah, string Label)[] Popular =
        {
            (18, 0, "Al-Kahf"), (36, 0, "Ya-Sin"), (67, 0, "Al-Mulk"), (2, 255, "Ayat al-Kursi"), (55, 0, "Ar-Rahman"), (56, 0, "Al-Waqi'ah"),
        };

        internal static IComponent Hero()
        {
            EnsureStyles();
            Look.EnsureStyles();

            var hero = Div(Att("qkg-hero"));

            var title = Div(Att("qkg-hero-ar"));
            title.setAttribute("dir", "rtl");
            title.setAttribute("lang", "ar");
            title.textContent = "القرآن الكريم";
            hero.appendChild(title);

            var sub = Div(Att("qkg-hero-sub"));
            sub.textContent = "Read, listen and study the Quran — Arabic first".t();
            hero.appendChild(sub);

            hero.appendChild(SearchBox());
            hero.appendChild(Chips());

            return Raw(hero).WS();
        }

        // -----------------------------------------------------------------------------------

        private static HTMLElement SearchBox()
        {
            var box   = Div(Att("qkg-search"));
            var field = Div(Att("qkg-search-field"));
            var icon  = Span(Att("qkg-search-icon"));
            icon.textContent = "⌕";
            icon.setAttribute("aria-hidden", "true");
            var input = document.createElement("input").As<HTMLInputElement>();
            input.className   = "qkg-search-input";
            input.placeholder = "2:255, Al-Kahf, الرحمن, page 50, mercy, Bukhari 6018…".t();
            input.setAttribute("aria-label", "Search the Quran".t());
            input.setAttribute("autocomplete", "off");
            input.setAttribute("spellcheck", "false");
            var clear = Span(Att("qkg-search-clear"));
            clear.textContent = "✕";
            clear.title       = "Clear".t();
            field.appendChild(icon);
            field.appendChild(input);
            field.appendChild(clear);
            var hint = Span(Att("qkg-search-kbd"));
            hint.textContent = "/";
            hint.title = "Press / to search".t();
            field.appendChild(hint);
            box.appendChild(field);

            var results = Div(Att("qkg-search-results"));
            results.style.display = "none";
            box.appendChild(results);

            var generation = 0;
            Action[] actions = new Action[0];
            var active = -1;

            void Highlight(int index)
            {
                var items = results.querySelectorAll(".qkg-sr-item");
                for (var i = 0; i < items.length; i++)
                {
                    var el = items[i].As<HTMLElement>();
                    if (i == index) el.classList.add("qkg-active"); else el.classList.remove("qkg-active");
                }
                active = index;
            }

            void Hide() { results.style.display = "none"; active = -1; }

            async void Run(int gen, string query)
            {
                QK.QuickSearchDto dto;
                try { dto = await QuranApi.QuickSearchAsync(query, TAKE); }
                catch { return; }
                if (gen != generation) return;
                actions = Draw(results, dto, query);
                results.style.display = actions.Length > 0 || query.Length > 0 ? "block" : "none";
                Highlight(actions.Length > 0 ? 0 : -1);
            }

            input.oninput = _ =>
            {
                var query = (input.value ?? "").Trim();
                var gen   = ++generation;
                clear.style.visibility = query.Length > 0 ? "visible" : "hidden";
                if (query.Length == 0) { Hide(); return; }
                window.setTimeout(__ => { if (gen == generation) Run(gen, query); }, 180);
            };

            input.onkeydown = e =>
            {
                var key = e.As<KeyboardEvent>().key;
                if (key == "ArrowDown" && actions.Length > 0) { e.preventDefault(); Highlight((active + 1) % actions.Length); }
                else if (key == "ArrowUp" && actions.Length > 0) { e.preventDefault(); Highlight((active - 1 + actions.Length) % actions.Length); }
                else if (key == "Enter" && active >= 0 && active < actions.Length) { e.preventDefault(); actions[active](); }
                else if (key == "Escape") { input.value = ""; clear.style.visibility = "hidden"; generation++; Hide(); }
            };

            input.onfocus = _ => { if ((input.value ?? "").Trim().Length > 0 && results.childElementCount > 0) results.style.display = "block"; };
            input.onblur  = _ => window.setTimeout(__ => Hide(), 200);

            clear.style.visibility = "hidden";
            clear.onclick = _ => { input.value = ""; clear.style.visibility = "hidden"; generation++; Hide(); input.focus(); };

            DomObserver.WhenMounted(input, () => window.setTimeout(__ => input.focus(), 50));
            input.setAttribute("data-qkg-search", "1");
            return box;
        }

        /// <summary>The results under the box; returns what each item does, in order, for the keyboard.</summary>
        private static Action[] Draw(HTMLElement into, QK.QuickSearchDto dto, string query)
        {
            into.innerHTML = "";
            var actions = new System.Collections.Generic.List<Action>();

            void Item(HTMLElement el, Action open)
            {
                el.classList.add("qkg-sr-item");
                var index = actions.Count;
                el.onmousedown = e => { e.preventDefault(); open(); };
                actions.Add(open);
                into.appendChild(el);
            }

            var targets = dto?.Targets ?? new QK.QuickTarget[0];
            if (targets.Length > 0)
            {
                into.appendChild(Label("Go to".t()));
                foreach (var t in targets)
                {
                    var captured = t;
                    var row  = Div(Att("qkg-sr-target"));
                    if (t.Kind != "hadith" && t.SurahNumber > 0) row.appendChild(Look.SurahNumber(t.SurahNumber));
                    var text  = Div(Att("qkg-sr-text"));
                    var label = Span(Att("qkg-sr-label"));
                    label.textContent = t.Label;
                    text.appendChild(label);
                    var detail = Span(Att("qkg-sr-detail"));
                    detail.textContent = KindLabel(t.Kind) + (string.IsNullOrEmpty(t.Detail) ? "" : " · " + t.Detail);
                    text.appendChild(detail);
                    row.appendChild(text);
                    if (!string.IsNullOrEmpty(t.LabelArabic))
                    {
                        var ar = Span(Att("qkg-sr-ar"));
                        ar.setAttribute("dir", "rtl");
                        ar.textContent = t.LabelArabic;
                        row.appendChild(ar);
                    }
                    Item(row, () => Open(captured));
                }
            }

            var verses = dto?.Verses ?? new QK.QuickVerse[0];
            if (verses.Length > 0)
            {
                into.appendChild(Label(dto.Total > verses.Length ? string.Format("Verses — the first {0} of {1}, in Arabic or English".t(), verses.Length, Panels.Number(dto.Total)) : string.Format("Verses — {0}".t(), verses.Length)));
                foreach (var v in verses)
                {
                    var captured = v;
                    var row  = Div(Att("qkg-sr-verse"));
                    var head = Div(Att("qkg-sr-vhead"));
                    var key  = Span(Att("qkg-sr-key"));
                    key.textContent = v.VerseKey;
                    head.appendChild(key);
                    var surah = Span(Att("qkg-sr-detail"));
                    surah.textContent = v.SurahName;
                    head.appendChild(surah);
                    row.appendChild(head);

                    var body = Div(Att("qkg-sr-vbody"));
                    var ar = Div(Att("qkg-sr-vtext"));
                    ar.setAttribute("dir", "rtl");
                    ar.setAttribute("lang", "ar");
                    ar.textContent = v.Text;
                    body.appendChild(ar);

                    var en = Div(Att("qkg-sr-ven"));
                    en.textContent = v.Translation;
                    body.appendChild(en);
                    row.appendChild(body);

                    Item(row, () => QuranLinks.Go(captured.SurahNumber, captured.Ayah));
                }
            }

            if (actions.Count == 0)
            {
                var none = Div(Att("qkg-sr-none"));
                none.textContent = string.Format("Nothing found for “{0}”. Try a verse (2:255), a surah's name, a page (page 50), or words of a verse in Arabic or English.".t(), query);
                into.appendChild(none);
            }

            return actions.ToArray();
        }

        private static void Open(QK.QuickTarget t)
        {
            switch (t.Kind)
            {
                case "hadith": Lists.Open(t.Uid); break;
                case "page":
                    ReaderOptions.Remember("quran", ReaderOptions.PAGES);
                    QuranLinks.Go(t.SurahNumber, t.Ayah);
                    break;
                default: QuranLinks.Go(t.SurahNumber, t.Kind == "surah" ? 0 : t.Ayah); break;
            }
        }

        private static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "verse":  return "Verse".t();
                case "surah":  return "Surah".t();
                case "page":   return "Page".t();
                case "juz":    return "Juz".t();
                case "hadith": return "Hadith".t();
                default:       return kind;
            }
        }

        private static HTMLElement Label(string text)
        {
            var el = Div(Att("qkg-sr-group"));
            el.textContent = text;
            return el;
        }

        // -----------------------------------------------------------------------------------

        /// <summary>The ways in under the box: the surahs, where the reader left off, the recitation, the surahs most read.</summary>
        private static HTMLElement Chips()
        {
            var chips = Div(Att("qkg-hero-chips"));
            var (surah, ayah) = QuranLinks.LastRead();

            if (surah > 0)
                chips.appendChild(Look.Button(string.Format("Continue reading · {0}".t(), ayah > 0 ? surah + ":" + ayah : "surah " + surah),
                                              () => QuranLinks.Go(surah, ayah), "primary", "↻", "Where you left off".t()));

            chips.appendChild(Look.Button("Listen · Sheikh Latrech".t(), () =>
            {
                QuranLinks.ListenOnArrival = true;
                QuranLinks.Go(surah > 0 ? surah : 1, surah > 0 ? ayah : 0);
            }, surah > 0 ? "plain" : "primary", "▶", "The recitation of Sheikh Mohamed (Hamadi) Latrech, Qalun from Nafi' — the text follows the verse being recited".t()));

            chips.appendChild(Look.Button("Surah list".t(), () =>
            {
                var list = document.getElementById("qkg-surahs");
                if (list is object) list.scrollIntoView(); else QuranLinks.Go(1);
            }, "plain", "☰", "All 114 surahs".t()));

            var popular = Div(Att("qkg-hero-popular"));
            var label   = Span(Att("qkg-hero-popular-label"));
            label.textContent = "Often read:".t();
            popular.appendChild(label);
            foreach (var p in Popular)
            {
                var captured = p;
                var link = Look.OnClick(Span(Att("qkg-link")), () => QuranLinks.Go(captured.Surah, captured.Ayah));
                link.textContent = p.Label;
                popular.appendChild(link);
            }

            var wrap = Div(Att("qkg-hero-ways"));
            wrap.appendChild(chips);
            wrap.appendChild(popular);
            return wrap;
        }

        // -----------------------------------------------------------------------------------

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = CSS.Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        private const string CSS = @"
.qkg-hero { display: flex; flex-direction: column; align-items: center; gap: 18px; padding: 52px 24px 36px 24px; border-radius: 28px; background: var(--qkg-teal-tint); text-align: center; }
.qkg-hero-ar { font-family: {FONT}; font-size: 72px; line-height: 1.6; color: var(--qkg-teal-strong); }
.qkg-hero-sub { font-size: 17px; color: var(--qkg-ink-2); margin-top: -6px; }
@media (max-width: 700px) { .qkg-hero-ar { font-size: 48px; } .qkg-hero { padding: 32px 16px 24px 16px; } }
.qkg-search { position: relative; width: min(720px, 100%); text-align: left; }
.qkg-search-field { display: flex; align-items: center; gap: 12px; height: 60px; padding: 0 22px; border-radius: 999px; background: var(--qkg-raised); border: 1px solid transparent; box-shadow: 0 2px 12px rgba(15, 100, 102, .10); box-sizing: border-box; }
.qkg-search-field:focus-within { border-color: var(--qkg-teal); box-shadow: 0 0 0 3px var(--qkg-teal-tint-2); }
.qkg-search-icon { font-size: 22px; color: var(--qkg-ink-3); }
.qkg-search-input { flex: 1 1 auto; border: none; outline: none; background: transparent; font: inherit; font-size: 17px; color: var(--qkg-ink); min-width: 0; }
.qkg-search-input::placeholder { color: var(--qkg-ink-3); }
.qkg-search-clear { cursor: pointer; color: var(--qkg-ink-3); font-size: 14px; }
.qkg-search-kbd { display: inline-flex; align-items: center; height: 22px; padding: 0 7px; border-radius: 6px; border: 1px solid var(--qkg-line-2); font-size: 12px; color: var(--qkg-ink-3); }
.qkg-search-results { position: absolute; z-index: 50; top: 68px; left: 0; right: 0; max-height: 64vh; overflow-y: auto; padding: 8px; border-radius: 18px; background: var(--qkg-raised); border: 1px solid var(--qkg-line); box-shadow: 0 24px 64px rgba(10, 20, 18, .22); }
.qkg-sr-group { font-size: 12px; font-weight: 600; letter-spacing: .06em; text-transform: uppercase; color: var(--qkg-ink-3); padding: 12px 12px 6px 12px; }
.qkg-sr-item { cursor: pointer; border-radius: 12px; padding: 10px 12px; color: var(--qkg-ink); }
.qkg-sr-item:hover, .qkg-sr-item.qkg-active { background: var(--qkg-teal-tint); }
.qkg-sr-target { display: flex; align-items: center; gap: 14px; }
.qkg-sr-text { display: flex; flex-direction: column; flex: 1 1 auto; min-width: 0; }
.qkg-sr-label { font-weight: 600; }
.qkg-sr-ar { font-family: {FONT}; font-size: 24px; line-height: 1.4; color: var(--qkg-teal-strong); }
.qkg-sr-detail { font-size: 13px; color: var(--qkg-ink-3); }
.qkg-sr-verse { display: grid; grid-template-columns: 72px minmax(0, 1fr); gap: 12px; }
.qkg-sr-vhead { display: flex; flex-direction: column; gap: 2px; }
.qkg-sr-key { font-weight: 600; font-size: 14px; color: var(--qkg-teal-strong); }
.qkg-sr-vbody { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.qkg-sr-vtext { font-family: {FONT}; font-size: 22px; line-height: 1.9; text-align: right; }
.qkg-sr-ven { font-size: 14px; color: var(--qkg-ink-2); line-height: 1.5; }
.qkg-sr-none { padding: 14px; font-size: 14px; color: var(--qkg-ink-3); }
.qkg-hero-ways { display: flex; flex-direction: column; align-items: center; gap: 14px; }
.qkg-hero-chips { display: flex; flex-wrap: wrap; justify-content: center; gap: 10px; }
.qkg-hero-popular { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px 16px; font-size: 14px; }
.qkg-hero-popular-label { color: var(--qkg-ink-3); }
";
    }
}
