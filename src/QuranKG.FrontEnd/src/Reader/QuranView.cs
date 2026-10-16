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

namespace QuranKG.FrontEnd.Reader
{
    /// <summary>
    /// The Quran to read: the 114 surahs on the left, the surah on the right as continuous text in
    /// its passages, and a clicked verse's translation, words and related verses beside it.
    /// #/read/2 opens Al-Baqarah, #/read/2/255 opens it on Ayat al-Kursi; selecting a verse writes
    /// its address in the URL, so it can be shared.
    /// </summary>
    internal sealed class QuranView : IComponent
    {
        internal static void Register()
        {
            Router.Register("qkg-read",       QuranLinks.Route,                    state => App.ShowDefault(new QuranView(state)));
            Router.Register("qkg-read-surah", QuranLinks.Route + "/:surah",        state => App.ShowDefault(new QuranView(state)));
            Router.Register("qkg-read-ayah",  QuranLinks.Route + "/:surah/:ayah",  state => App.ShowDefault(new QuranView(state)));
        }

        /// <summary>Whether the page on screen is this one — what lights its sidebar entry.</summary>
        internal static bool IsShowing() => (window.location.hash ?? "").StartsWith(QuranLinks.Route, StringComparison.Ordinal);

        private readonly IComponent _content;
        private readonly int        _surah;

        public QuranView(Parameters state)
        {
            _surah  = state is object && state.TryGetValue("surah", out var s) && int.TryParse(s, out var n) && n >= 1 && n <= 114 ? n : 1;
            var ayah = state is object && state.TryGetValue("ayah", out var a) && int.TryParse(a, out var m) ? m : 0;

            _content = Defer(async () =>
            {
                var listen = QuranLinks.ListenOnArrival;
                QuranLinks.ListenOnArrival = false;
                var corpus = await QuranApi.CorpusAsync();
                var surahs = corpus.Overview.SurahRows.OrderBy(r => r.Number).ToArray();
                var current = surahs.First(r => r.Number == _surah);

                IComponent index = null;
                var pane = new ReadingPane(new ReadingPane.Settings
                {
                    Uid                = current.Uid,
                    OptionsKey         = "quran",
                    KeyWordsByDefault  = false,
                    InitialAyah        = ayah,
                    ListenOnOpen       = listen,
                    Header             = text => Header(current, text),
                    GoToVerseElsewhere = (uid, key) => QuranLinks.Go(key),
                    OnSelected         = v =>
                    {
                        if (index is object) { if (v is object) index.Collapse(); else index.Show(); }
                        // Links and the last verse read name the Hafs verse, whatever the reading: #/read/2/255.
                        window.history.replaceState(null, "", QuranLinks.For(_surah, Riwayat.HafsAyah(v)));
                        if (v is object) QuranLinks.Remember(_surah, Riwayat.HafsAyah(v));
                    },
                });

                QuranLinks.Remember(_surah, ayah);

                index = Index(surahs);
                return HStack().S().Class("qkg-ui").Children(index, pane.S().Grow().Style(st => st.setProperty("min-width", "0")));
            }, TextBlock("Opening the Quran…".t()).Secondary().P(24)).S();
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>The surah's name and place, the way to the surahs either side, and to its own page.</summary>
        private IComponent Header(QK.SurahStats s, QK.ReadingDto text)
        {
            var passages = (text.Sections ?? new QK.ReadingSection[0]).Count(x => UID128.IsNotNull(x.PassageUid));

            var previous = Look.IconButton("‹", "Previous surah".t(), () => QuranLinks.Go(_surah - 1));
            var next     = Look.IconButton("›", "Next surah".t(), () => QuranLinks.Go(_surah + 1));
            if (_surah <= 1)   previous.setAttribute("disabled", "disabled");
            if (_surah >= 114) next.setAttribute("disabled", "disabled");

            var header = Look.El("div", "qkg-reader-header");
            header.appendChild(previous);
            header.appendChild(Look.Ar(s.NameArabic, "qkg-arw qkg-reader-header-ar"));
            header.appendChild(Look.El("div", "qkg-reader-header-name",
                Look.El("span", "qkg-h3", s.Number + " · " + s.Name),
                Look.El("span", "qkg-note", s.NameEnglish)));
            header.appendChild(next);
            header.appendChild(Look.PlaceBadge(s.Place));
            // The verses as the reading counts them (Qalun's Al-Baqarah: 285), Hafs's count beside when it differs.
            var reading = Riwayat.Of(text);
            var count   = reading?.SurahVerses is object && reading.SurahVerses.Length >= _surah ? reading.SurahVerses[_surah - 1] : s.Verses;
            var badge   = Look.Badge(string.Format("{0} verses".t(), count));
            if (reading is object && !reading.IsCorpusText)
                badge.title = string.Format("{0} verses in {1}, which counts them the Madani way; {2} in Hafs".t(), count, reading.Name, s.Verses);
            header.appendChild(badge);
            UpdateIndex(reading);
            if (passages > 0) header.appendChild(Look.Badge(string.Format("{0} passages".t(), passages)));
            header.appendChild(Look.Badge(string.Format("revealed #{0}".t(), s.RevelationOrder)));
            header.appendChild(Look.El("span", "qkg-toolbar-spacer"));
            header.appendChild(Look.Button("Surah page".t(), () => Lists.Open(s.Uid), "ghost", "↗", "Its profile, passages and similar surahs".t()));
            return Raw(header).WS();
        }

        private readonly System.Collections.Generic.List<(int Number, HTMLElement Meta)> _indexMeta = new System.Collections.Generic.List<(int, HTMLElement)>();

        /// <summary>The index's verse counts, as the reading being read counts them.</summary>
        private void UpdateIndex(QK.RiwayaOption reading)
        {
            if (reading?.SurahVerses is null) return;
            foreach (var item in _indexMeta)
                if (item.Number >= 1 && item.Number <= reading.SurahVerses.Length) item.Meta.textContent = string.Format("{0} verses".t(), reading.SurahVerses[item.Number - 1]);
        }

        /// <summary>The 114 surahs, filterable by number, name or Arabic name; the one being read lit and in view.</summary>
        private IComponent Index(QK.SurahStats[] surahs)
        {
            var list = Look.El("div", "qkg-index-list");
            var rows = surahs.Select(s =>
            {
                var row = Look.El("div", "qkg-index-row" + (s.Number == _surah ? " qkg-on" : ""));
                row.appendChild(Look.El("span", "qkg-index-n", s.Number.ToString()));
                var meta = Look.El("span", "qkg-index-meta", string.Format("{0} verses".t(), s.Verses));
                _indexMeta.Add((s.Number, meta));
                row.appendChild(Look.El("span", "qkg-index-text",
                    Look.El("span", "qkg-index-name", s.Name),
                    meta));
                var ar = Look.Ar(s.NameArabic, "qkg-arw qkg-index-ar");
                ar.style.color = s.Place == QK.MEDINAN ? "var(--qkg-medinan)" : "var(--qkg-meccan)";
                row.appendChild(ar);
                row.title = s.Number + ". " + s.Name + " — " + s.NameEnglish + " · " + s.Place;
                var number = s.Number;
                Look.OnClick(row, () => QuranLinks.Go(number));
                list.appendChild(row);
                return (Surah: s, Row: row);
            }).ToArray();

            var field = Look.El("label", "qkg-field qkg-index-field");
            field.appendChild(Look.El("span", "", "⌕"));
            var input = document.createElement("input").As<HTMLInputElement>();
            input.placeholder = "Find a surah".t();
            input.setAttribute("aria-label", "Find a surah".t());
            input.type = "search";
            field.appendChild(input);
            input.oninput = _ =>
            {
                var q = (input.value ?? "").Trim();
                foreach (var r in rows)
                {
                    var match = q.Length == 0
                             || r.Surah.Number.ToString() == q
                             || (r.Surah.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.Surah.NameEnglish ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.Surah.NameArabic ?? "").Contains(q);
                    r.Row.style.display = match ? "" : "none";
                }
            };

            var current = rows.First(r => r.Surah.Number == _surah).Row;
            DomObserver.WhenMounted(current, () => window.setTimeout(_ => current.scrollIntoView(new ScrollIntoViewOptions { block = ScrollLogicalPosition.center }), 50));

            var top = Look.El("div", "qkg-index-top");
            top.appendChild(field);

            EnsureStyles();
            return VStack().W(250.px()).H(100.percent()).NoShrink().Class("qkg-ui qkg-index")
                           .Children(Raw(top), VStack().WS().Grow().ScrollY().Children(Raw(list)));
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-index { border-right: 1px solid var(--qkg-line); }
.qkg-index-top { padding: 16px 14px 10px 14px; }
.qkg-index-field { height: 40px; padding: 0 14px; }
.qkg-index-field input { font-size: 14px; }
.qkg-index-list { display: flex; flex-direction: column; padding: 0 8px 16px 8px; }
.qkg-index-row { display: flex; align-items: center; gap: 10px; padding: 7px 10px; border-radius: 10px; color: var(--qkg-ink); }
.qkg-index-row:hover { background: var(--qkg-surface-2); }
.qkg-index-row.qkg-on { background: var(--qkg-teal-tint); }
.qkg-index-row.qkg-on .qkg-index-name { font-weight: 600; }
.qkg-index-n { width: 22px; text-align: right; font-size: 12px; color: var(--qkg-ink-3); flex: none; }
.qkg-index-text { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; line-height: 1.25; }
.qkg-index-name { font-size: 14px; font-weight: 500; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.qkg-index-meta { font-size: 12px; color: var(--qkg-ink-3); }
.qkg-index-ar { font-size: 18px; line-height: 1.4; }
@media (max-width: 900px) { .qkg-index { display: none !important; } }
";
            document.head.appendChild(style);
        }
    }
}
