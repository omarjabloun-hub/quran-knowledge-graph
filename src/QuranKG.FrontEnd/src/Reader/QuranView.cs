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
                        window.history.replaceState(null, "", QuranLinks.For(_surah, v?.Ayah ?? 0));
                        if (v is object) QuranLinks.Remember(_surah, v.Ayah);
                    },
                });

                QuranLinks.Remember(_surah, ayah);

                return HStack().S().Children(Index(surahs), pane.S().Grow().Style(st => st.setProperty("min-width", "0")));
            }, TextBlock("Opening the Quran…".t()).Secondary().P(24)).S();
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>The surah's name and place, the way to the surahs either side, and to its own page.</summary>
        private IComponent Header(QK.SurahStats s, QK.ReadingDto text)
        {
            var previous = Button().SetIcon(UIcons.AngleLeft).Compact().Tooltip("Previous surah".t()).OnClick(() => QuranLinks.Go(_surah - 1));
            var next     = Button().SetIcon(UIcons.AngleRight).Compact().Tooltip("Next surah".t()).OnClick(() => QuranLinks.Go(_surah + 1));
            if (_surah <= 1)   previous.Disabled();
            if (_surah >= 114) next.Disabled();

            var passages = (text.Sections ?? new QK.ReadingSection[0]).Count(x => UID128.IsNotNull(x.PassageUid));

            return HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().PL(16).PR(16).PT(12).Children(
                previous,
                Arabic.Inline(s.NameArabic, 24),
                TextBlock(s.Number + ". " + s.Name).SemiBold(),
                TextBlock(s.NameEnglish).Small().Secondary(),
                Badge(s.Place).Foreground(Palette.ForPlace(s.Place)).Outline(),
                TextBlock(passages > 0 ? string.Format("{0} verses · {1} passages".t(), s.Verses, passages) : string.Format("{0} verses".t(), s.Verses)).Small().Secondary().Grow(),
                Button("Surah page".t()).SetIcon(UIcons.ArrowUpRightFromSquare).Compact().Tooltip("Its profile, passages and similar surahs".t()).OnClick(() => Lists.Open(s.Uid)),
                next);
        }

        /// <summary>The 114 surahs, filterable by number, name or Arabic name; the one being read lit and in view.</summary>
        private IComponent Index(QK.SurahStats[] surahs)
        {
            var list = VStack().WS();
            var rows = surahs.Select(s =>
            {
                var row = HStack().WS().AlignItemsCenter().Gap(8.px()).PL(12).PR(12).PT(4).PB(4).Children(
                    TextBlock(s.Number.ToString()).Tiny().Secondary().W(24.px()).NoShrink(),
                    VStack().Grow().Style(st => st.setProperty("min-width", "0")).Children(
                        TextBlock(s.Name).Small().NoWrap().Ellipsis(),
                        TextBlock(string.Format("{0} verses".t(), s.Verses)).Tiny().Secondary()),
                    Arabic.Inline(s.NameArabic, 18).Foreground(Palette.ForPlace(s.Place)))
                   .Style(st => { st.setProperty("cursor", "pointer"); st.setProperty("border-radius", "6px"); });

                if (s.Number == _surah) row.Background(Theme.Colors.Blue100);
                var number = s.Number;
                row.Render().onclick = _ => QuranLinks.Go(number);
                return (Surah: s, Row: row);
            }).ToArray();

            foreach (var r in rows) list.Add(r.Row);

            var search = SearchBox("Surah".t()).SearchAsYouType().WS();
            search.OnSearch((_, query) =>
            {
                var q = (query ?? "").Trim();
                foreach (var r in rows)
                {
                    var match = q.Length == 0
                             || r.Surah.Number.ToString() == q
                             || (r.Surah.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.Surah.NameEnglish ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.Surah.NameArabic ?? "").Contains(q);
                    if (match) r.Row.Show(); else r.Row.Collapse();
                }
            });

            var scroll = VStack().WS().Grow().ScrollY().Children(list);
            var current = rows.First(r => r.Surah.Number == _surah).Row.Render();
            DomObserver.WhenMounted(current, () => window.setTimeout(_ => current.scrollIntoView(), 50));

            return VStack().W(250.px()).H(100.percent()).NoShrink()
                           .Style(st => st.setProperty("border-right", "1px solid var(--tss-default-border-color)"))
                           .Children(VStack().WS().P(10).Children(search), scroll);
        }
    }
}
