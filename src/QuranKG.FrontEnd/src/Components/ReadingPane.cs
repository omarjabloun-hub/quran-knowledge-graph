using System;
using System.Collections.Generic;
using System.Linq;
using Mosaik;
using QuranKG.FrontEnd.Api;
using Tesserae;
using Transpose;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// A text to read with what a reader needs around it: a toolbar (text size, how the text is laid
    /// out — flowing, verse by verse, or as the mushaf's pages —, passages, English, key words, and
    /// where the reader is: page, juz, hizb), a strip of the passages to jump between them, the text
    /// itself (<see cref="QuranReader"/>), the panel a clicked verse opens on the right
    /// (<see cref="VerseDetails"/>) and, for a surah, its recitation (<see cref="RecitationPlayer"/>),
    /// which marks the verse being recited and keeps it in view. The Quran View, a surah's Read tab,
    /// a passage and a theme all read through it, so they read alike.
    ///
    /// <para>In a READING (riwaya): Qalun, the main one, or Hafs, one switch away (<see cref="Riwayat"/>).
    /// In Qalun the verses are Qalun's own — its numbering, its mushaf's pages — and each opens the
    /// panel on the Hafs verse its words are in, where the graph's knowledge sits. Switching keeps the
    /// reader's place: the verse at the top is found again through its Hafs verse.</para>
    /// </summary>
    internal sealed class ReadingPane : IComponent
    {
        internal sealed class Settings
        {
            /// <summary>The Surah or Cluster to read.</summary>
            internal UID128 Uid               { get; set; }
            /// <summary>Which saved options to use: "quran", "passage", "theme".</summary>
            internal string OptionsKey        { get; set; }
            internal bool   KeyWordsByDefault { get; set; }
            /// <summary>Drawn above the toolbar, once the text has loaded.</summary>
            internal Func<QK.ReadingDto, IComponent> Header { get; set; }
            /// <summary>The verse to open on (its ayah, in a surah), 0 for the top.</summary>
            internal int    InitialAyah       { get; set; }
            /// <summary>Where a related verse leads when it is not in this text; null opens its page.</summary>
            internal Action<UID128, string> GoToVerseElsewhere { get; set; }
            /// <summary>Offer "Read in the Quran" on a verse (everywhere but the Quran View).</summary>
            internal bool   ReadInQuran       { get; set; }
            /// <summary>Start the surah's recitation as soon as the text is drawn, at <see cref="InitialAyah"/>.</summary>
            internal bool   ListenOnOpen      { get; set; }
            /// <summary>Called with the verse selected, or null when the panel closes.</summary>
            internal Action<QK.ReadingVerse> OnSelected { get; set; }
        }

        private readonly Settings      _settings;
        private readonly ReaderOptions _options;
        private readonly Stack         _content;
        private readonly HTMLElement   _scroll;
        private readonly Stack         _details;
        private readonly Dictionary<string, (QK.ReadingVerse Verse, QK.ReadingSection Section)> _byUid = new Dictionary<string, (QK.ReadingVerse, QK.ReadingSection)>();

        private QK.ReadingDto _text;
        private QuranReader   _reader;
        private UID128        _selected;
        private UID128        _playing;
        private RecitationPlayer _player;
        private TextBlock     _location;

        public ReadingPane(Settings settings)
        {
            EnsureStyles();
            _settings = settings;
            _options  = ReaderOptions.Load(settings.OptionsKey ?? "default", settings.KeyWordsByDefault);

            Look.EnsureStyles();
            _scroll = Div(Att("qkg-reader-scroll"));
            ApplyTheme();
            _scroll.style.flex      = "1 1 auto";
            _scroll.style.overflowY = "auto";
            _scroll.style.minHeight = "0";
            _scroll.style.width     = "100%";

            _details = VStack().W(480.px()).MaxWidth(46.vw()).H(100.percent()).ScrollY().NoShrink().Class("qkg-ui qkg-study")
                               .Style(s => s.setProperty("border-left", "1px solid var(--qkg-line)"));
            _details.Collapse();

            _content = VStack().S();
            _content.Add(Build(text => settings.InitialAyah > 0 && text.Kind == "Surah"
                                           ? (text.Sections ?? new QK.ReadingSection[0]).Select(x => x.SurahNumber).FirstOrDefault() + ":" + settings.InitialAyah
                                           : null,
                               settings.ListenOnOpen));
        }

        /// <summary>The text in the reading chosen, opened on the verse holding <paramref name="target"/> (a Hafs key; null for the top).</summary>
        private IComponent Build(Func<QK.ReadingDto, string> target, bool listen)
        {
            var settings = _settings;
            return Defer(async () =>
            {
                _text = await QuranApi.ReadTextAsync(settings.Uid, Riwayat.Saved());
                foreach (var section in _text.Sections ?? new QK.ReadingSection[0])
                    foreach (var verse in section.Verses ?? new QK.ReadingVerse[0])
                        _byUid[verse.Uid.ToString()] = (verse, section);

                if (_text.Kind == "Surah")
                    _player = new RecitationPlayer(new RecitationPlayer.Settings
                    {
                        Surah          = settings.Uid,
                        VerseKeyPrefix = (_text.Sections ?? new QK.ReadingSection[0]).Select(x => x.SurahNumber + ":").FirstOrDefault() ?? "",
                        Riwaya         = _text.Riwaya,
                        OnVerse        = Recited,
                    });

                var left = VStack().S().Grow().Class("qkg-ui").Style(s => s.setProperty("min-width", "0"));
                if (settings.Header is object) left.Add(settings.Header(_text));

                // The toolbar and the strip depend on the layout: rebuilt with the text when it changes.
                var toolbar = VStack().WS();
                var strip   = VStack().WS().Style(st => st.setProperty("border-bottom", "1px solid var(--qkg-line)"));
                Rebuild = () =>
                {
                    toolbar.Clear();
                    toolbar.Add(Toolbar());
                    strip.Clear();
                    if (_text.PassagesBuilt && (_text.Sections ?? new QK.ReadingSection[0]).Length > 1 && _options.Layout != ReaderOptions.PAGES) strip.Add(Strip().PL(32).PR(32).PB(10).PT(2));
                };
                Rebuild();
                left.Add(toolbar);
                left.Add(strip);
                left.Add(Raw(_scroll));
                if (_player is object) left.Add(_player);

                _scroll.onscroll = _ => Locate();

                Draw();

                // The verse to open on: the one of this reading holding the Hafs verse asked for.
                var key   = target(_text);
                var found = key is null ? default((QK.ReadingVerse Verse, QK.ReadingSection Section))
                                        : _byUid.Values.FirstOrDefault(x => (x.Verse.HafsKeys ?? new string[0]).Contains(key));

                if (listen && _player is object) _player.PlayFrom(found.Verse?.Ayah ?? 0);

                if (found.Verse is object)
                    DomObserver.WhenMounted(_scroll, () => window.setTimeout(_ => Select(found.Verse.Uid, scroll: true), 150));

                return HStack().S().Children(left, _details);
            }, TextBlock("Opening the text…".t()).Secondary().P(24)).S();
        }

        /// <summary>Reads the text again in another reading, where the reader was.</summary>
        private void SwitchTo(string riwaya)
        {
            var view  = _scroll.getBoundingClientRect().As<DOMRect>();
            var here  = UID128.IsNotNull(_selected) && _byUid.TryGetValue(_selected.ToString(), out var sel) ? sel.Verse : _reader?.VerseAt(view.top + 24);
            var key   = here is null ? null : Riwayat.HafsKey(here);

            Riwayat.Save(riwaya);
            _player?.Close();
            _byUid.Clear();
            _selected = default(UID128);
            _playing  = default(UID128);
            _details.Clear();
            _details.Collapse();
            _settings.OnSelected?.Invoke(null);

            _content.Clear();
            _content.Add(Build(_ => key, listen: false));
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>Selects a verse of this text, opens its panel and, if asked, scrolls it into view.</summary>
        internal void Select(UID128 verse, bool scroll)
        {
            if (!_byUid.TryGetValue(verse.ToString(), out var found)) return;

            _selected = verse;
            _reader?.Select(verse);
            ShowDetails(found.Verse, found.Section);

            if (scroll) ScrollTo(_reader?.ElementOf(verse));
        }

        internal bool Contains(UID128 verse) => _byUid.ContainsKey(verse.ToString());

        /// <summary>Starts the surah's recitation at a verse (0: from the start) — a surah's text only.</summary>
        internal void Listen(int ayah) => _player?.PlayFrom(ayah);

        /// <summary>The verse being recited: marked in the text and, if the player follows, brought into view.</summary>
        private void Recited(QK.RecitedVerseRow row)
        {
            _playing = row is null ? default(UID128) : row.Uid;
            _reader?.MarkPlaying(_playing);
            if (row is null || _player is null || !_player.Follow) return;

            var el = _reader?.ElementOf(row.Uid);
            if (el is null) return;
            var rect = el.getBoundingClientRect().As<DOMRect>();
            var view = _scroll.getBoundingClientRect().As<DOMRect>();
            if (rect.top < view.top + 20 || rect.bottom > view.bottom - 20) ScrollTo(el);
        }

        /// <summary>Where the reader is: the page, juz and hizb of the verse at the top of the view.</summary>
        private void Locate()
        {
            if (_location is null || _reader is null) return;
            var view  = _scroll.getBoundingClientRect().As<DOMRect>();
            var verse = _reader.VerseAt(view.top + 24);
            _location.Text = verse is null || verse.Page == 0 ? "" : string.Format("Page {0} · Juz {1} · Hizb {2}".t(), verse.Page, verse.Juz, verse.Hizb);
        }

        // -----------------------------------------------------------------------------------

        private void Draw()
        {
            var top = _scroll.scrollTop;
            _scroll.innerHTML = "";
            _reader = new QuranReader(_text, _options, new QuranReader.Callbacks
            {
                OnVerse = (v, _) => Select(v.Uid, scroll: false),
                OnListen = _player is null ? null : (Action<QK.ReadingVerse>)(v => _player.PlayFrom(v.Ayah)),
                OnSurah = _text.Kind == "Surah" ? null : (Action<QK.ReadingSection>)(s => QuranLinks.Go(s.SurahNumber, (s.Verses ?? new QK.ReadingVerse[0]).Select(x => x.Ayah).FirstOrDefault())),
            });
            _scroll.appendChild(_reader.Render());
            if (UID128.IsNotNull(_playing)) _reader.MarkPlaying(_playing);
            window.setTimeout(_ => Locate(), 60);

            if (_selected != default(UID128))
            {
                _reader.Select(_selected);
                window.setTimeout(_ => ScrollTo(_reader.ElementOf(_selected)), 30);
            }
            else
            {
                _scroll.scrollTop = top;
            }
        }

        private void ShowDetails(QK.ReadingVerse v, QK.ReadingSection s)
        {
            _details.Clear();
            _details.Add(VerseDetails.Create(v, s, CloseDetails, GoToVerse, _settings.ReadInQuran, _player is null ? null : (Action<QK.ReadingVerse>)(x => _player.PlayFrom(x.Ayah)),
                                             Riwayat.IsOther(_text) ? _text.Riwaya : null));
            _details.Show();
            _settings.OnSelected?.Invoke(v);
        }

        private void CloseDetails()
        {
            _selected = default(UID128);
            _reader?.Select(default(UID128));
            _details.Collapse();
            _settings.OnSelected?.Invoke(null);
        }

        /// <summary>A related verse: in this text, select it here; elsewhere, wherever the page says.</summary>
        private void GoToVerse(UID128 uid, string key)
        {
            if (Contains(uid)) { Select(uid, scroll: true); return; }
            if (_settings.GoToVerseElsewhere is object) _settings.GoToVerseElsewhere(uid, key);
            else Lists.Open(uid);
        }

        private void ScrollTo(HTMLElement el)
        {
            if (el is null) return;
            var offset = el.getBoundingClientRect().As<DOMRect>().top - _scroll.getBoundingClientRect().As<DOMRect>().top;
            _scroll.scrollTop = Math.Max(0, _scroll.scrollTop + offset - _scroll.clientHeight / 3);
        }

        private IComponent Toolbar()
        {
            var bar   = Look.El("div", "qkg-toolbar");
            var pages = _options.Layout == ReaderOptions.PAGES;

            bar.appendChild(Layouts());
            var readings = Readings();
            if (readings is object) bar.appendChild(readings);

            var toggles = Look.El("div", "qkg-toolbar-group");
            if (_text.PassagesBuilt && !pages)
                toggles.appendChild(Option("Passages".t(), "Mark where the clusters build cuts the text, with each passage's words and theme".t(), _options.Passages, on => _options.Passages = on));
            if (!pages)
                toggles.appendChild(Option("English".t(), _options.Layout == ReaderOptions.VERSES ? "Saheeh International under each verse".t() : "Saheeh International beside the Arabic, passage by passage".t(), _options.Translation, on => _options.Translation = on));
            // Tajweed: in a reading whose text carries it (Qalun's), the letters coloured as the mushaf of Tunisia colours them.
            var hasTajweed = (_text.Sections ?? new QK.ReadingSection[0]).Any(s => (s.Verses ?? new QK.ReadingVerse[0]).Any(v => !string.IsNullOrEmpty(v.Tajweed)));
            if (hasTajweed)
                toggles.appendChild(Option("Tajweed".t(), "Colour each letter by its rule of tajweed, as the Dar al-Maarifah mushaf of Qalun does".t(), _options.Tajweed, on => _options.Tajweed = on, tajweed: true));
            if ((_text.Sections ?? new QK.ReadingSection[0]).Any(s => (s.Verses ?? new QK.ReadingVerse[0]).Any(v => (v.Highlights ?? new int[0]).Length > 0)))
                toggles.appendChild(Option("Key words".t(),
                               _text.Kind == "Surah" ? "Mark each passage's most distinctive words — what sets it apart, and why it was cut there".t()
                                                     : "Mark the words these verses use far more than the rest of the Quran — why they were grouped".t(),
                               _options.KeyWords, on => _options.KeyWords = on, marked: true));
            bar.appendChild(toggles);

            var spacer = Look.El("span", "qkg-toolbar-spacer");
            bar.appendChild(spacer);

            _location = TextBlock("").Small().NoWrap().Class("qkg-toolbar-where");
            bar.appendChild(_location.Tooltip(Riwayat.IsOther(_text) ? string.Format("Where the top of the text is in the mushaf of {0} (King Fahd Complex)".t(), Riwayat.Of(_text)?.Name)
                                                                      : "Where the top of the text is in the Madinah mushaf".t()).Render());

            if (_player is object)
                bar.appendChild(Look.Button("Listen".t(), () =>
                {
                    var from = UID128.IsNotNull(_selected) && _byUid.TryGetValue(_selected.ToString(), out var sel) ? sel.Verse.Ayah : 0;
                    _player.PlayFrom(from);
                }, "soft", "▶", "The surah recited, the verse being recited marked and followed — from the verse selected, or from the start".t()));

            var size = Look.El("div", "qkg-toolbar-group qkg-toolbar-size");
            size.appendChild(Look.IconButton("A−", "Smaller text".t(), () => { _options.FontSize = Math.Max(ReaderOptions.MIN_SIZE, _options.FontSize - 2); Changed(); }));
            size.appendChild(Look.IconButton("A+", "Larger text".t(), () => { _options.FontSize = Math.Min(ReaderOptions.MAX_SIZE, _options.FontSize + 2); Changed(); }));
            bar.appendChild(size);

            bar.appendChild(Themes());

            if (hasTajweed && _options.Tajweed) bar.appendChild(TajweedKey.Legend(() => Router.Navigate(TajweedPage.TajweedView.Route)));

            var keys = _text.KeyLemmas ?? new QK.Counted[0];
            if (keys.Length > 0)
            {
                var legend = Look.El("div", "qkg-toolbar-keys");
                legend.appendChild(Look.El("span", "qkg-note", "Key words".t()));
                foreach (var k in keys.Take(8))
                {
                    var captured = k;
                    var word = Look.Ar(captured.Label, "qkg-arw qkg-toolbar-key");
                    word.title = captured.Detail ?? "";
                    legend.appendChild(Look.OnClick(word, () => Lists.Open(captured.Uid)));
                }
                bar.appendChild(legend);
            }

            return Raw(bar).WS();
        }

        /// <summary>The three ways to lay the text out, as a segmented switch.</summary>
        private HTMLElement Layouts()
        {
            var layouts = new[] { ReaderOptions.VERSES, ReaderOptions.FLOW, ReaderOptions.PAGES };
            var seg = Look.Segmented(new[] { "Verse by verse".t(), "Reading".t(), "Mushaf".t() },
                                     Array.IndexOf(layouts, _options.Layout),
                                     i =>
                                     {
                                         if (_options.Layout == layouts[i]) return;
                                         _options.Layout = layouts[i];
                                         _options.Save();
                                         Rebuild?.Invoke();
                                         Draw();
                                     }, "Layout".t());
            var tips = new[]
            {
                "One verse to a row, with its key — and its translation if English is on".t(),
                "The verses flowing one after another, passage by passage".t(),
                Riwayat.IsOther(_text) ? string.Format("The pages of the mushaf of {0} (604), each closed by its number".t(), Riwayat.Of(_text)?.Name)
                                       : "The pages of the Madinah mushaf (604), each closed by its number".t(),
            };
            var buttons = seg.querySelectorAll("button");
            for (var i = 0; i < buttons.length; i++) buttons[i].As<HTMLElement>().title = tips[i];
            return seg;
        }

        /// <summary>The readings the text can be read in, as a segmented switch, the main one first; null with only one.</summary>
        private HTMLElement Readings()
        {
            var riwayat = _text.Riwayat ?? new QK.RiwayaOption[0];
            if (riwayat.Length < 2) return null;
            var current = Array.FindIndex(riwayat, r => r.Id == _text.Riwaya);
            var seg = Look.Segmented(riwayat.Select(Riwayat.Short).ToArray(), current, i =>
            {
                if (riwayat[i].Id == _text.Riwaya) return;
                SwitchTo(riwayat[i].IsMain ? "" : riwayat[i].Id);
            }, "Reading".t());
            seg.classList.add("qkg-readings");
            var buttons = seg.querySelectorAll("button");
            for (var i = 0; i < buttons.length && i < riwayat.Length; i++)
                buttons[i].As<HTMLElement>().title = riwayat[i].NameArabic + " — " + riwayat[i].Name + " · " + string.Format("{0:N0} verses".t(), riwayat[i].Verses)
                                                   + (riwayat[i].IsMain ? " · " + "the main reading".t() : "") + "\n" + riwayat[i].Description;
            return seg;
        }

        /// <summary>The page the text is set on: light, sepia or dark.</summary>
        private HTMLElement Themes()
        {
            var group   = Look.El("div", "qkg-toolbar-group qkg-swatches");
            group.setAttribute("role", "radiogroup");
            group.setAttribute("aria-label", "Reading theme".t());
            var current = string.IsNullOrEmpty(_options.Theme) ? (document.body.classList.contains("tss-dark-mode") ? "dark" : "light") : _options.Theme;
            foreach (var (theme, label) in new[] { ("light", "Light".t()), ("sepia", "Sepia".t()), ("dark", "Dark".t()) })
            {
                var captured = theme;
                var swatch = Look.El("button", "qkg-swatch qkg-swatch-" + theme + (theme == current ? " qkg-on" : ""));
                swatch.setAttribute("type", "button");
                swatch.setAttribute("role", "radio");
                swatch.setAttribute("aria-checked", theme == current ? "true" : "false");
                swatch.setAttribute("aria-label", label);
                swatch.title = label;
                swatch.onclick = _ =>
                {
                    _options.Theme = captured;
                    _options.Save();
                    ApplyTheme();
                    Rebuild?.Invoke();
                };
                group.appendChild(swatch);
            }
            return group;
        }

        private void ApplyTheme()
        {
            _scroll.classList.remove("qkg-reading-light");
            _scroll.classList.remove("qkg-reading-sepia");
            _scroll.classList.remove("qkg-reading-dark");
            if (!string.IsNullOrEmpty(_options.Theme)) _scroll.classList.add("qkg-reading-" + _options.Theme);
        }

        /// <summary>Set by the pane: rebuilds the toolbar and the text when the layout changes.</summary>
        private Action Rebuild;

        private HTMLElement Option(string label, string tooltip, bool on, Action<bool> set, bool marked = false, bool tajweed = false)
        {
            var chip = Look.Chip("", null, on);
            chip.setAttribute("aria-pressed", on ? "true" : "false");
            chip.appendChild(Look.El("span", tajweed ? "qkg-chip-swatch qkg-chip-tajweed" : marked ? "qkg-chip-swatch qkg-chip-mark" : "qkg-chip-swatch"));
            chip.appendChild(Look.El("span", "", label));
            chip.title   = tooltip;
            chip.onclick = _ => { set(!on); Changed(); Rebuild?.Invoke(); };
            return chip;
        }

        private void Changed()
        {
            _options.Save();
            Draw();
        }

        private static bool _styled;

        internal static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 14px; padding: 10px 32px 12px 32px; }
.qkg-toolbar-group { display: inline-flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.qkg-toolbar-where { color: var(--qkg-ink-3); }
.qkg-readings button { font-family: {FONT}; font-size: 17px; }
.qkg-toolbar-size .qkg-iconbtn { width: 32px; height: 32px; font-size: 13px; font-weight: 600; }
.qkg-chip-swatch { width: 10px; height: 10px; border-radius: 3px; border: 1.5px solid currentColor; box-sizing: border-box; opacity: .7; }
.qkg-chip.qkg-on .qkg-chip-swatch { background: currentColor; opacity: 1; }
.qkg-chip-mark { border-color: var(--qkg-mark); }
.qkg-chip.qkg-on .qkg-chip-mark { background: var(--qkg-mark); }
.qkg-chip-tajweed { border: 0; opacity: 1; background: conic-gradient(#8b1c1c 0 25%, #12923c 0 50%, #1e9bd7 0 75%, #ef7d00 0); }
.qkg-swatch { width: 24px; height: 24px; border-radius: 50%; border: 1px solid var(--qkg-line-2); cursor: pointer; padding: 0; }
.qkg-swatch.qkg-on { box-shadow: 0 0 0 2px var(--qkg-bg), 0 0 0 4px var(--qkg-teal); }
.qkg-swatch-light { background: #ffffff; }
.qkg-swatch-sepia { background: #f1e2c3; }
.qkg-swatch-dark { background: #1a1f1e; }
.qkg-toolbar-keys { flex: 1 1 100%; display: flex; flex-wrap: wrap; align-items: center; gap: 6px 12px; }
.qkg-toolbar-key { font-size: 18px; color: var(--qkg-mark); }
.qkg-reader-header { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 16px; padding: 14px 32px 4px 32px; }
.qkg-reader-header-name { display: flex; flex-direction: column; line-height: 1.25; }
.qkg-reader-header-ar { font-size: 28px; color: var(--qkg-teal-strong); }
.qkg-study { background: var(--qkg-bg); }
@media (max-width: 700px) { .qkg-toolbar, .qkg-reader-header { padding-left: 16px; padding-right: 16px; } }
".Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        /// <summary>The text's passages as a strip, each as wide as its words and in its theme's colour: click to go there.</summary>
        private IComponent Strip()
        {
            var strip = Div(Att("qkg-strip"));
            foreach (var section in _text.Sections ?? new QK.ReadingSection[0])
            {
                if (UID128.IsNull(section.PassageUid)) continue;
                var captured = section;
                var words    = (section.Verses ?? new QK.ReadingVerse[0]).Sum(v => Math.Max(1, v.Words));
                var seg      = Div(Att("qkg-strip-seg"));
                seg.style.flex       = words + " 1 0";
                seg.style.background = Clusters.ColorOf(section.ThemeOrdinal);
                seg.title            = (_text.Kind == "Theme" ? section.SurahName + " · " : "") + QuranReader.Range(section) + " · " + section.LabelArabic
                                     + (UID128.IsNotNull(section.ThemeUid) ? " — " + section.ThemeLabelArabic : "");
                seg.onclick          = _ =>
                {
                    if (!_options.Passages) { _options.Passages = true; Changed(); }
                    ScrollTo(_reader?.SectionOf(captured.PassageUid));
                };
                strip.appendChild(seg);
            }
            return Raw(strip).WS();
        }
    }
}
