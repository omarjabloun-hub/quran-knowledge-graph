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
    /// A text to read with what a reader needs around it: a toolbar (text size, passages, English,
    /// key words), a strip of the passages to jump between them, the text itself (<see cref="QuranReader"/>),
    /// and the panel a clicked verse opens on the right (<see cref="VerseDetails"/>). The Quran View,
    /// a surah's Read tab, a passage and a theme all read through it, so they read alike.
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

                var left = VStack().S().Grow().Style(s => s.setProperty("min-width", "0"));
                if (settings.Header is object) left.Add(settings.Header(_text));
                left.Add(Toolbar());
                if (_text.PassagesBuilt && (_text.Sections ?? new QK.ReadingSection[0]).Length > 1) left.Add(Strip().PL(16).PR(16).PB(6));
                left.Add(Raw(_scroll));

                Draw();

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

        // -----------------------------------------------------------------------------------

        private void Draw()
        {
            var top = _scroll.scrollTop;
            _scroll.innerHTML = "";
            _reader = new QuranReader(_text, _options, new QuranReader.Callbacks
            {
                OnVerse = (v, _) => Select(v.Uid, scroll: false),
                OnSurah = _text.Kind == "Surah" ? null : (Action<QK.ReadingSection>)(s => QuranLinks.Go(s.SurahNumber, (s.Verses ?? new QK.ReadingVerse[0]).Select(x => x.Ayah).FirstOrDefault())),
            });
            _scroll.appendChild(_reader.Render());

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
            _details.Add(VerseDetails.Create(v, s, CloseDetails, GoToVerse, _settings.ReadInQuran));
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
                HStack().AlignItemsCenter().Gap(4.px()).Children(smaller, TextBlock("Text".t()).Small().Secondary(), larger));

            if (_text.PassagesBuilt)
                bar.Add(Option("Passages".t(), "Mark where the clusters build cuts the text, with each passage's words and theme".t(), _options.Passages, on => _options.Passages = on));

            bar.Add(Option("English".t(), "Saheeh International beside the Arabic, passage by passage".t(), _options.Translation, on => _options.Translation = on));

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

            return bar.Style(s => s.setProperty("border-bottom", "1px solid var(--tss-default-border-color)"));
        }

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
                if (section.PassageUid == default(UID128)) continue;
                var captured = section;
                var words    = (section.Verses ?? new QK.ReadingVerse[0]).Sum(v => Math.Max(1, v.Words));
                var seg      = Div(Att("qkg-strip-seg"));
                seg.style.flex       = words + " 1 0";
                seg.style.background = Clusters.ColorOf(section.ThemeOrdinal);
                seg.title            = (_text.Kind == "Theme" ? section.SurahName + " · " : "") + QuranReader.Range(section) + " · " + section.LabelArabic
                                     + (section.ThemeUid != default(UID128) ? " — " + section.ThemeLabelArabic : "");
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
