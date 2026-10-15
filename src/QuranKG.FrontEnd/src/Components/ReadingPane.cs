using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly IComponent    _content;
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
            _settings = settings;
            _options  = ReaderOptions.Load(settings.OptionsKey ?? "default", settings.KeyWordsByDefault);

            _scroll = Div(Att("qkg-reader-scroll"));
            _scroll.style.flex      = "1 1 auto";
            _scroll.style.overflowY = "auto";
            _scroll.style.minHeight = "0";
            _scroll.style.width     = "100%";

            _details = VStack().W(420.px()).H(100.percent()).ScrollY().NoShrink()
                               .Style(s => s.setProperty("border-left", "1px solid var(--tss-default-border-color)"));
            _details.Collapse();

            _content = Defer(async () =>
            {
                _text = await QuranApi.ReadTextAsync(settings.Uid);
                foreach (var section in _text.Sections ?? new QK.ReadingSection[0])
                    foreach (var verse in section.Verses ?? new QK.ReadingVerse[0])
                        _byUid[verse.Uid.ToString()] = (verse, section);

                if (_text.Kind == "Surah")
                    _player = new RecitationPlayer(new RecitationPlayer.Settings
                    {
                        Surah          = settings.Uid,
                        VerseKeyPrefix = (_text.Sections ?? new QK.ReadingSection[0]).Select(x => x.SurahNumber + ":").FirstOrDefault() ?? "",
                        OnVerse        = Recited,
                    });

                var left = VStack().S().Grow().Style(s => s.setProperty("min-width", "0"));
                if (settings.Header is object) left.Add(settings.Header(_text));

                // The toolbar and the strip depend on the layout: rebuilt with the text when it changes.
                var toolbar = VStack().WS();
                var strip   = VStack().WS();
                Rebuild = () =>
                {
                    toolbar.Clear();
                    toolbar.Add(Toolbar());
                    strip.Clear();
                    if (_text.PassagesBuilt && (_text.Sections ?? new QK.ReadingSection[0]).Length > 1 && _options.Layout != ReaderOptions.PAGES) strip.Add(Strip().PL(16).PR(16).PB(6));
                };
                Rebuild();
                left.Add(toolbar);
                left.Add(strip);
                left.Add(Raw(_scroll));
                if (_player is object) left.Add(_player);

                _scroll.onscroll = _ => Locate();

                Draw();

                if (settings.ListenOnOpen && _player is object) _player.PlayFrom(settings.InitialAyah);

                if (settings.InitialAyah > 0)
                {
                    var target = _byUid.Values.FirstOrDefault(x => x.Verse.Ayah == settings.InitialAyah);
                    if (target.Verse is object)
                        DomObserver.WhenMounted(_scroll, () => window.setTimeout(_ => Select(target.Verse.Uid, scroll: true), 150));
                }

                return HStack().S().Children(left, _details);
            }, TextBlock("Opening the text…".t()).Secondary().P(24)).S();
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
            _details.Add(VerseDetails.Create(v, s, CloseDetails, GoToVerse, _settings.ReadInQuran, _player is null ? null : (Action<QK.ReadingVerse>)(x => _player.PlayFrom(x.Ayah))));
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
            var smaller = Button().SetIcon(UIcons.Minus).Compact().Tooltip("Smaller text".t());
            var larger  = Button().SetIcon(UIcons.Plus).Compact().Tooltip("Larger text".t());
            smaller.OnClick(() => { _options.FontSize = Math.Max(ReaderOptions.MIN_SIZE, _options.FontSize - 2); Changed(); });
            larger.OnClick(() =>  { _options.FontSize = Math.Min(ReaderOptions.MAX_SIZE, _options.FontSize + 2); Changed(); });

            var bar = HStack().WS().AlignItemsCenter().Gap(16.px()).Wrap().PL(16).PR(16).PT(8).PB(8).Children(
                HStack().AlignItemsCenter().Gap(4.px()).Children(smaller, TextBlock("Text".t()).Small().Secondary(), larger),
                Layouts());

            if (_player is object)
                bar.Add(Button("Listen".t()).SetIcon(UIcons.PlayAlt).Compact()
                              .Tooltip("The surah recited, the verse being recited marked and followed — from the verse selected, or from the start".t())
                              .OnClick(() =>
                              {
                                  var from = UID128.IsNotNull(_selected) && _byUid.TryGetValue(_selected.ToString(), out var sel) ? sel.Verse.Ayah : 0;
                                  _player.PlayFrom(from);
                              }));

            var pages = _options.Layout == ReaderOptions.PAGES;
            if (_text.PassagesBuilt && !pages)
                bar.Add(Option("Passages".t(), "Mark where the clusters build cuts the text, with each passage's words and theme".t(), _options.Passages, on => _options.Passages = on));

            if (!pages)
                bar.Add(Option("English".t(), _options.Layout == ReaderOptions.VERSES ? "Saheeh International under each verse".t() : "Saheeh International beside the Arabic, passage by passage".t(), _options.Translation, on => _options.Translation = on));

            if ((_text.Sections ?? new QK.ReadingSection[0]).Any(s => (s.Verses ?? new QK.ReadingVerse[0]).Any(v => (v.Highlights ?? new int[0]).Length > 0)))
                bar.Add(Option("Key words".t(),
                               _text.Kind == "Surah" ? "Mark each passage's most distinctive words — what sets it apart, and why it was cut there".t()
                                                     : "Mark the words these verses use far more than the rest of the Quran — why they were grouped".t(),
                               _options.KeyWords, on => _options.KeyWords = on));

            var keys = _text.KeyLemmas ?? new QK.Counted[0];
            if (keys.Length > 0)
            {
                var legend = HStack().AlignItemsCenter().Gap(10.px()).Wrap();
                foreach (var k in keys.Take(8))
                {
                    var captured = k;
                    legend.Add(Arabic.Inline(captured.Label, 17)
                                     .Style(s => { s.setProperty("color", "#c2410c"); s.setProperty("cursor", "pointer"); })
                                     .Tooltip(captured.Detail)
                                     .OnClick((_, __) => Lists.Open(captured.Uid)));
                }
                bar.Add(legend);
            }

            _location = TextBlock("").Small().Secondary().NoWrap();
            bar.Add(_location.Grow().Style(st => st.setProperty("text-align", "right")).Tooltip("Where the top of the text is in the Madinah mushaf".t()));

            return bar.Style(s => s.setProperty("border-bottom", "1px solid var(--tss-default-border-color)"));
        }

        /// <summary>The three ways to lay the text out, as a segmented switch.</summary>
        private IComponent Layouts()
        {
            var group = HStack().AlignItemsCenter().Gap(0.px());
            void Add(string layout, string label, UIcons icon, string tooltip)
            {
                var button = Button(label).SetIcon(icon).Compact().Tooltip(tooltip);
                if (_options.Layout == layout) button.Primary();
                button.OnClick(() =>
                {
                    if (_options.Layout == layout) return;
                    _options.Layout = layout;
                    _options.Save();
                    Rebuild?.Invoke();
                    Draw();
                });
                group.Add(button);
            }
            Add(ReaderOptions.FLOW,   "Flowing".t(),           UIcons.AlignJustify,  "The verses flowing one after another, passage by passage".t());
            Add(ReaderOptions.VERSES, "Verse by verse".t(), UIcons.RectangleList,        "One verse to a row, with its key — and its translation if English is on".t());
            Add(ReaderOptions.PAGES,  "Mushaf".t(),         UIcons.BookOpenCover, "The pages of the Madinah mushaf (604), each closed by its number".t());
            return group;
        }

        /// <summary>Set by the pane: rebuilds the toolbar and the text when the layout changes.</summary>
        private Action Rebuild;

        private IComponent Option(string label, string tooltip, bool on, Action<bool> set)
        {
            var toggle = Toggle(label).Checked(on);
            toggle.OnChange((s, __) => { set(s.IsChecked); Changed(); });
            return toggle.Tooltip(tooltip);
        }

        private void Changed()
        {
            _options.Save();
            Draw();
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
