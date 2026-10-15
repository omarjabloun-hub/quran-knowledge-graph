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
            Arabic.EnsureFonts();

            var hero = Div(Att("qkg-hero"));

            var title = Div(Att("qkg-hero-ar"));
            title.setAttribute("dir", "rtl");
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
            var input = document.createElement("input").As<HTMLInputElement>();
            input.className   = "qkg-search-input";
            input.placeholder = "Search the Quran — 2:255, Al-Kahf, الرحمن, page 50, mercy, Bukhari 6018…".t();
            input.setAttribute("autocomplete", "off");
            input.setAttribute("spellcheck", "false");
            var clear = Span(Att("qkg-search-clear"));
            clear.textContent = "✕";
            clear.title       = "Clear".t();
            field.appendChild(icon);
            field.appendChild(input);
            field.appendChild(clear);
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
                    var kind = Span(Att("qkg-sr-kind"));
                    kind.textContent = KindLabel(t.Kind);
                    row.appendChild(kind);
                    var label = Span(Att("qkg-sr-label"));
                    label.textContent = t.Label;
                    row.appendChild(label);
                    if (!string.IsNullOrEmpty(t.LabelArabic))
                    {
                        var ar = Span(Att("qkg-sr-ar"));
                        ar.setAttribute("dir", "rtl");
                        ar.textContent = t.LabelArabic;
                        row.appendChild(ar);
                    }
                    var detail = Span(Att("qkg-sr-detail"));
                    detail.textContent = t.Detail ?? "";
                    row.appendChild(detail);
                    Item(row, () => Open(captured));
                }
            }

            var verses = dto?.Verses ?? new QK.QuickVerse[0];
            if (verses.Length > 0)
            {
                into.appendChild(Label(dto.Total > verses.Length ? string.Format("Verses — the first {0} of {1}".t(), verses.Length, dto.Total) : string.Format("Verses — {0}".t(), verses.Length)));
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

                    var ar = Div(Att("qkg-sr-vtext"));
                    ar.setAttribute("dir", "rtl");
                    ar.textContent = v.Text;
                    row.appendChild(ar);

                    var en = Div(Att("qkg-sr-ven"));
                    en.textContent = v.Translation;
                    row.appendChild(en);

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

            chips.appendChild(Chip("☰ " + "Surah list".t(), "All 114 surahs, to read".t(), true, () => QuranLinks.Go(1)));

            var (surah, ayah) = QuranLinks.LastRead();
            if (surah > 0)
                chips.appendChild(Chip("↻ " + string.Format("Continue reading · {0}".t(), ayah > 0 ? surah + ":" + ayah : "surah " + surah),
                                       "Where you left off".t(), false, () => QuranLinks.Go(surah, ayah)));

            chips.appendChild(Chip("▶ " + "Listen · Sheikh Latrech".t(), "The recitation of Sheikh Mohamed (Hamadi) Latrech, Qalun from Nafi' — the text follows the verse being recited".t(), false, () =>
            {
                QuranLinks.ListenOnArrival = true;
                QuranLinks.Go(surah > 0 ? surah : 1, surah > 0 ? ayah : 0);
            }));

            var popular = Div(Att("qkg-hero-popular"));
            var label   = Span(Att("qkg-hero-popular-label"));
            label.textContent = "Often read:".t();
            popular.appendChild(label);
            foreach (var p in Popular)
            {
                var captured = p;
                var link = Span(Att("qkg-hero-link"));
                link.textContent = p.Label;
                link.onclick = _ => QuranLinks.Go(captured.Surah, captured.Ayah);
                popular.appendChild(link);
            }

            var wrap = Div(Att("qkg-hero-ways"));
            wrap.appendChild(chips);
            wrap.appendChild(popular);
            return wrap;
        }

        private static HTMLElement Chip(string text, string title, bool primary, Action onClick)
        {
            var chip = Span(Att(primary ? "qkg-chip qkg-chip-primary" : "qkg-chip"));
            chip.textContent = text;
            chip.title       = title;
            chip.onclick     = _ => onClick();
            return chip;
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
.qkg-hero { display: flex; flex-direction: column; align-items: center; padding: 34px 16px 30px 16px; background: linear-gradient(180deg, rgba(14, 116, 144, .10), rgba(14, 116, 144, .02)); border-bottom: 1px solid var(--tss-default-border-color); }
.qkg-hero-ar { font-family: {FONT}; font-size: 64px; line-height: 1.9; color: #0e7490; opacity: .85; }
.qkg-hero-sub { font-size: 14px; color: var(--tss-secondary-foreground-color); margin: 4px 0 20px 0; }
.qkg-search { position: relative; width: min(720px, 100%); }
.qkg-search-field { display: flex; align-items: center; gap: 10px; height: 52px; padding: 0 18px; border-radius: 26px; background: var(--tss-default-background-color); border: 1px solid var(--tss-default-border-color); box-shadow: 0 2px 10px rgba(0,0,0,.06); }
.qkg-search-field:focus-within { border-color: #0e7490; box-shadow: 0 2px 14px rgba(14, 116, 144, .18); }
.qkg-search-icon { font-size: 22px; color: var(--tss-secondary-foreground-color); }
.qkg-search-input { flex: 1 1 auto; border: none; outline: none; background: transparent; font-size: 16px; color: var(--tss-default-foreground-color); min-width: 0; }
.qkg-search-clear { cursor: pointer; color: var(--tss-secondary-foreground-color); font-size: 14px; }
.qkg-search-results { position: absolute; z-index: 50; top: 58px; left: 0; right: 0; max-height: 62vh; overflow-y: auto; padding: 8px; border-radius: 16px; background: var(--tss-default-background-color); border: 1px solid var(--tss-default-border-color); box-shadow: 0 10px 30px rgba(0,0,0,.14); }
.qkg-sr-group { font-size: 11px; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; color: var(--tss-secondary-foreground-color); padding: 8px 10px 4px 10px; }
.qkg-sr-item { cursor: pointer; border-radius: 10px; padding: 8px 10px; }
.qkg-sr-item:hover, .qkg-sr-item.qkg-active { background: var(--tss-default-background-hover-color); }
.qkg-sr-target { display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap; }
.qkg-sr-kind { font-size: 11px; font-weight: 600; color: #0e7490; min-width: 44px; }
.qkg-sr-label { font-weight: 600; }
.qkg-sr-ar { font-family: {FONT}; font-size: 18px; line-height: 1.4; }
.qkg-sr-detail { font-size: 12px; color: var(--tss-secondary-foreground-color); }
.qkg-sr-vhead { display: flex; gap: 10px; align-items: baseline; }
.qkg-sr-key { font-weight: 600; font-size: 13px; }
.qkg-sr-vtext { font-family: {FONT}; font-size: 21px; line-height: 1.9; text-align: right; }
.qkg-sr-ven { font-size: 13px; color: var(--tss-secondary-foreground-color); line-height: 1.5; }
.qkg-sr-none { padding: 12px; font-size: 13px; color: var(--tss-secondary-foreground-color); }
.qkg-hero-ways { display: flex; flex-direction: column; align-items: center; gap: 12px; margin-top: 18px; }
.qkg-hero-chips { display: flex; flex-wrap: wrap; justify-content: center; gap: 10px; }
.qkg-chip { cursor: pointer; padding: 8px 16px; border-radius: 10px; font-size: 14px; background: var(--tss-default-background-color); border: 1px solid var(--tss-default-border-color); }
.qkg-chip:hover { border-color: #0e7490; }
.qkg-chip-primary { background: #0e7490; border-color: #0e7490; color: #fff; }
.qkg-hero-popular { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px 14px; font-size: 13px; }
.qkg-hero-popular-label { color: var(--tss-secondary-foreground-color); }
.qkg-hero-link { cursor: pointer; color: #0e7490; }
.qkg-hero-link:hover { text-decoration: underline; }
";
    }
}
