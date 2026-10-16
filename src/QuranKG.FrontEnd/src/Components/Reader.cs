using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuranKG.FrontEnd.Api;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// How a reader likes the text: its size, how it is laid out, whether the passages are marked,
    /// whether the English runs beside the Arabic, whether the key words are marked. Kept per reader
    /// in the browser (the Quran View, a passage, a theme), so each opens the way it was left.
    /// </summary>
    internal sealed class ReaderOptions
    {
        internal const int MIN_SIZE = 20, MAX_SIZE = 48;

        /// <summary>The verses flowing in a paragraph, passage by passage — how the text reads.</summary>
        internal const string FLOW   = "flow";
        /// <summary>One verse to a row, its key and its translation under it — how a verse is studied.</summary>
        internal const string VERSES = "verses";
        /// <summary>The pages of the Madinah mushaf (604), each closed by its number — how the book is held.</summary>
        internal const string PAGES  = "pages";

        private readonly string _key;

        internal int  FontSize    { get; set; } = 30;
        internal bool Passages    { get; set; } = true;
        internal bool Translation { get; set; }
        internal bool KeyWords    { get; set; }
        /// <summary>The tajweed coloured, letter by letter (a reading whose text carries it: Qalun's).</summary>
        internal bool Tajweed     { get; set; }
        internal string Layout    { get; set; } = FLOW;
        /// <summary>The page the text is set on: "" follows the workspace, else light, sepia or dark.</summary>
        internal string Theme     { get; set; } = "";

        private ReaderOptions(string key) => _key = "qkg-reader-" + key;

        internal static ReaderOptions Load(string key, bool keyWordsByDefault)
        {
            var o = new ReaderOptions(key) { KeyWords = keyWordsByDefault };
            try
            {
                var saved = localStorage.getItem(o._key);
                if (!string.IsNullOrEmpty(saved))
                {
                    var parts = saved.Split('|');
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[0], out var size)) o.FontSize = Math.Max(MIN_SIZE, Math.Min(MAX_SIZE, size));
                        o.Passages    = parts[1] == "1";
                        o.Translation = parts[2] == "1";
                        o.KeyWords    = parts[3] == "1";
                        if (parts.Length >= 5 && (parts[4] == VERSES || parts[4] == PAGES)) o.Layout = parts[4];
                        if (parts.Length >= 6 && (parts[5] == "light" || parts[5] == "sepia" || parts[5] == "dark")) o.Theme = parts[5];
                        if (parts.Length >= 7) o.Tajweed = parts[6] == "1";
                    }
                }
            }
            catch { /* private window, storage blocked: the defaults */ }
            return o;
        }

        /// <summary>Sets one reader's layout before it opens: "page 50" on the home page opens the Quran View as the mushaf.</summary>
        internal static void Remember(string key, string layout)
        {
            var o = Load(key, false);
            o.Layout = layout;
            o.Save();
        }

        /// <summary>Turns one reader's tajweed colours on before it opens: the Tajweed page's examples.</summary>
        internal static void RememberTajweed(string key)
        {
            var o = Load(key, false);
            o.Tajweed = true;
            o.Save();
        }

        internal void Save()
        {
            try { localStorage.setItem(_key, FontSize + "|" + (Passages ? 1 : 0) + "|" + (Translation ? 1 : 0) + "|" + (KeyWords ? 1 : 0) + "|" + Layout + "|" + Theme + "|" + (Tajweed ? 1 : 0)); }
            catch { /* nothing to keep it in */ }
        }
    }

    /// <summary>
    /// Quranic text as it is read: verses flowing one after another in a paragraph, each closed by
    /// its number, not one box per verse. A surah, a passage or a theme (qkg/read/text) comes in
    /// runs of consecutive verses; each run opens with what the clusters build says of it — its
    /// place, its distinctive words, the theme it shares with other surahs, in that theme's colour.
    ///
    /// <para>A verse is one span: hovering it marks it, clicking it selects it and tells the page,
    /// which opens its translation and what is related to it. With the translation on, the English
    /// runs beside the Arabic, passage by passage, and a verse is marked on both sides.</para>
    ///
    /// <para>Three layouts (<see cref="ReaderOptions.Layout"/>): the verses flowing in a paragraph;
    /// verse by verse, one row each with its key and translation; or the mushaf's pages, each verse
    /// on the page of the Madinah mushaf it is printed on, the page closed by its number. The page
    /// breaks are the mushaf's; the lines within a page are the browser's.</para>
    ///
    /// <para>While a recitation plays, the verse being recited is marked (<see cref="MarkPlaying"/>).</para>
    ///
    /// <para>Plain DOM rather than a component per verse: Al-Baqarah is 286 verses and 6,100 words,
    /// and the browser lays out one paragraph of spans far faster than 286 cards.</para>
    /// </summary>
    internal sealed class QuranReader : IComponent
    {
        private const string BISMILLAH = "بِسْمِ ٱللَّهِ ٱلرَّحْمَـٰنِ ٱلرَّحِيمِ";
        // The Bismillah as the King Fahd Complex writes Qalun (its Maghrebi wasl marks).
        private const string BISMILLAH_QALUN = "بِسْمِ اِ۬للَّهِ اِ۬لرَّحْمَٰنِ اِ۬لرَّحِيمِ";

        internal sealed class Callbacks
        {
            internal Action<QK.ReadingVerse, QK.ReadingSection> OnVerse   { get; set; }
            /// <summary>A surah heading in a theme: where to read that passage in its surah.</summary>
            internal Action<QK.ReadingSection>                  OnSurah   { get; set; }
            internal Action<UID128>                             OnTheme   { get; set; } = Lists.Open;
            internal Action<UID128>                             OnPassage { get; set; } = Lists.Open;
            /// <summary>Listen from this verse: offered on each verse row when a recitation is there; null hides it.</summary>
            internal Action<QK.ReadingVerse>                    OnListen  { get; set; }
        }

        private readonly HTMLElement                            _root;
        private readonly Dictionary<string, List<HTMLElement>> _verses   = new Dictionary<string, List<HTMLElement>>();
        private readonly Dictionary<string, HTMLElement>       _sections = new Dictionary<string, HTMLElement>();
        private readonly Dictionary<string, UID128>            _uids     = new Dictionary<string, UID128>();
        private string _selected;
        private string _playing;
        private bool   _tajweed;
        private readonly List<(HTMLElement Element, QK.ReadingVerse Verse)> _order = new List<(HTMLElement, QK.ReadingVerse)>();

        public QuranReader(QK.ReadingDto text, ReaderOptions options, Callbacks callbacks)
        {
            EnsureStyles();
            Arabic.EnsureFonts();

            _root = Div(Att("qkg-reader qkg-layout-" + options.Layout + " qkg-riwaya-" + (text.Riwaya ?? "hafs")));
            // A reading other than Hafs: its verses are its own, each word linked to Hafs's.
            var other = (text.Riwayat ?? new QK.RiwayaOption[0]).Any(r => r.Id == text.Riwaya && !r.IsCorpusText);
            _root.style.setProperty("--qkg-size", options.FontSize + "px");
            if (options.KeyWords) _root.classList.add("qkg-keywords");
            _tajweed = options.Tajweed;
            if (_tajweed) { TajweedKey.EnsureStyles(); _root.classList.add("qkg-tajweed"); }

            var sections = text.Sections ?? new QK.ReadingSection[0];
            var isSurah  = text.Kind == "Surah";

            if (isSurah && sections.Length > 0)
            {
                var s     = sections[0];
                var title = Div(Att("qkg-surah-title"));
                var name  = Div(Att("qkg-surah-name-row"));
                name.appendChild(Text("span", "qkg-rule", ""));
                name.appendChild(Text("span", "qkg-surah-name", "سُورَةُ " + s.SurahNameArabic, rtl: true));
                name.appendChild(Text("span", "qkg-rule", ""));
                title.appendChild(name);
                // Tanzil prefixes the Bismillah to verse 1 of every surah but 1 (where it IS verse 1)
                // and 9 (which has none); the connector strips it, so it is drawn here as the heading.
                // Qalun counts no Bismillah as a verse, Al-Fatiha's included: it heads every surah but 9.
                if (s.SurahNumber != 9 && (other || s.SurahNumber != 1))
                    title.appendChild(Text("div", "qkg-bismillah", text.Riwaya == "qalun" ? BISMILLAH_QALUN : BISMILLAH, rtl: true));
                _root.appendChild(title);
            }

            if (options.Layout == ReaderOptions.PAGES)
            {
                Pages(sections, isSurah, callbacks);
                WordTip.Attach(_root, key => _uids.TryGetValue(key, out var uid) ? uid : default(UID128));
                return;
            }

            var byRow = options.Layout == ReaderOptions.VERSES;
            HTMLElement arabic = null, english = null;
            var lastSurah = -1;

            foreach (var section in sections)
            {
                var newSurah = section.SurahNumber != lastSurah;
                lastSurah    = section.SurahNumber;

                var block = Div(Att("qkg-pas"));
                if (UID128.IsNotNull(section.PassageUid)) _sections[section.PassageUid.ToString()] = block;

                if (!isSurah && newSurah)
                {
                    var heading = Text("div", "qkg-surah-heading", "", rtl: false);
                    heading.appendChild(Text("span", "qkg-surah-heading-ar", "سورة " + section.SurahNameArabic, rtl: true));
                    heading.appendChild(Text("span", "qkg-surah-heading-latin", section.SurahNumber + ". " + section.SurahName));
                    if (callbacks.OnSurah is object)
                    {
                        heading.classList.add("qkg-clickable");
                        heading.title   = "Read this passage in its surah".t();
                        var captured    = section;
                        heading.onclick = _ => callbacks.OnSurah(captured);
                    }
                    block.appendChild(heading);
                }

                var marked = options.Passages && UID128.IsNotNull(section.PassageUid);
                // Flowing Arabic alone: the passage is described in the margin beside its text.
                var aside  = marked && !byRow && !options.Translation;
                if (aside) block.classList.add("qkg-pas-side");
                if (marked && !aside) block.appendChild(Header(section, callbacks));

                if (byRow)
                {
                    foreach (var verse in section.Verses ?? new QK.ReadingVerse[0])
                        block.appendChild(VerseRow(verse, section, callbacks, options.Translation));
                    _root.appendChild(block);
                    continue;
                }

                // Without the passage marks a surah reads as one paragraph: a run only breaks the
                // text where it starts a new surah.
                if (marked || newSurah || arabic is null)
                {
                    var body = Div(Att(options.Translation ? "qkg-body qkg-two" : "qkg-body"));
                    arabic = Div(Att("qkg-ar"));
                    arabic.setAttribute("dir", "rtl");
                    arabic.setAttribute("lang", "ar");
                    body.appendChild(arabic);
                    if (options.Translation)
                    {
                        english = Div(Att("qkg-en"));
                        english.setAttribute("dir", "ltr");
                        body.appendChild(english);
                    }
                    block.appendChild(body);
                    if (aside) block.appendChild(Aside(section, callbacks));
                }
                if (section.ThemeOrdinal > 0 && marked) block.style.setProperty("--qkg-theme", Clusters.ColorOf(section.ThemeOrdinal));

                foreach (var verse in section.Verses ?? new QK.ReadingVerse[0])
                {
                    arabic.appendChild(VerseArabic(verse, section, callbacks));
                    arabic.appendChild(document.createTextNode(" "));
                    if (options.Translation)
                    {
                        english.appendChild(VerseEnglish(verse, section, callbacks));
                        english.appendChild(document.createTextNode(" "));
                    }
                }

                _root.appendChild(block);
            }

            WordTip.Attach(_root, key => _uids.TryGetValue(key, out var uid) ? uid : default(UID128));
        }

        public HTMLElement Render() => _root;

        /// <summary>The verse's span in the Arabic, to scroll to; null when it is not in this text.</summary>
        internal HTMLElement ElementOf(UID128 verse)
            => _verses.TryGetValue(verse.ToString(), out var els) ? els.FirstOrDefault() : null;

        /// <summary>The verse at the top of the reader's view: what the page, juz and hizb shown above it are of.</summary>
        internal QK.ReadingVerse VerseAt(double top)
        {
            QK.ReadingVerse last = null;
            foreach (var entry in _order)
            {
                var rect = entry.Element.getBoundingClientRect().As<DOMRect>();
                if (rect.bottom > top) return entry.Verse;
                last = entry.Verse;
            }
            return last;
        }

        /// <summary>Marks the verse being recited (on both sides), unmarking the last; default unmarks.</summary>
        internal void MarkPlaying(UID128 verse)
        {
            if (_playing is object && _verses.TryGetValue(_playing, out var previous))
                foreach (var el in previous) el.classList.remove("qkg-play");

            _playing = UID128.IsNull(verse) ? null : verse.ToString();

            if (_playing is object && _verses.TryGetValue(_playing, out var current))
                foreach (var el in current) el.classList.add("qkg-play");
        }

        internal HTMLElement SectionOf(UID128 passage)
            => _sections.TryGetValue(passage.ToString(), out var el) ? el : null;

        /// <summary>Marks one verse as selected (on both sides), unmarking the last; default unmarks.</summary>
        internal void Select(UID128 verse)
        {
            if (_selected is object && _verses.TryGetValue(_selected, out var previous))
                foreach (var el in previous) el.classList.remove("qkg-on");

            _selected = UID128.IsNull(verse) ? null : verse.ToString();

            if (_selected is object && _verses.TryGetValue(_selected, out var current))
                foreach (var el in current) el.classList.add("qkg-on");
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The text as the mushaf's pages: the verses of each page in one justified block, closed by
        /// the page's number and its juz and hizb. A theme's verses keep their surah headings.
        /// </summary>
        private void Pages(QK.ReadingSection[] sections, bool isSurah, Callbacks callbacks)
        {
            var verses = sections.SelectMany(s => (s.Verses ?? new QK.ReadingVerse[0]).Select(v => (Verse: v, Section: s))).ToList();
            var lastSurah = isSurah && sections.Length > 0 ? sections[0].SurahNumber : -1;

            foreach (var page in verses.GroupBy(x => x.Verse.Page))
            {
                var sheet = Div(Att("qkg-page"));
                var frame = Div(Att("qkg-page-frame"));
                sheet.appendChild(frame);
                var firstOf = page.First();
                var top = Div(Att("qkg-page-top"));
                top.setAttribute("dir", "rtl");
                top.appendChild(Text("span", "", firstOf.Section.SurahNameArabic, rtl: true));
                top.appendChild(Text("span", "", "الجزء " + Panels.ArabicDigits(firstOf.Verse.Juz), rtl: true));
                frame.appendChild(top);
                HTMLElement text = null;

                foreach (var entry in page)
                {
                    var verse   = entry.Verse;
                    var section = entry.Section;
                    if (section.SurahNumber != lastSurah)
                    {
                        lastSurah = section.SurahNumber;
                        var heading = Text("div", "qkg-surah-heading", "", rtl: false);
                        heading.appendChild(Text("span", "qkg-surah-heading-ar", "سورة " + section.SurahNameArabic, rtl: true));
                        heading.appendChild(Text("span", "qkg-surah-heading-latin", section.SurahNumber + ". " + section.SurahName));
                        if (callbacks.OnSurah is object)
                        {
                            heading.classList.add("qkg-clickable");
                            var captured = section;
                            heading.onclick = _ => callbacks.OnSurah(captured);
                        }
                        frame.appendChild(heading);
                        text = null;
                    }

                    if (text is null)
                    {
                        text = Div(Att("qkg-ar qkg-page-text"));
                        text.setAttribute("dir", "rtl");
                        text.setAttribute("lang", "ar");
                        frame.appendChild(text);
                    }

                    text.appendChild(VerseArabic(verse, section, callbacks));
                    text.appendChild(document.createTextNode(" "));
                }

                var first = page.First().Verse;
                var foot  = Div(Att("qkg-page-foot"));
                foot.appendChild(Text("span", "qkg-page-n", Panels.ArabicDigits(page.Key), rtl: true));
                foot.title = string.Format("Page {0} · Juz {1} · Hizb {2}".t(), page.Key, first.Juz, first.Hizb);
                frame.appendChild(foot);

                _root.appendChild(sheet);
            }
        }

        /// <summary>
        /// One verse as a row: on the left its key and what can be done with it (listen from it, copy
        /// it, study it); on the right the Arabic and — if asked — its translation.
        /// </summary>
        private HTMLElement VerseRow(QK.ReadingVerse v, QK.ReadingSection s, Callbacks callbacks, bool translation)
        {
            var row   = Div(Att("qkg-row"));
            var tools = Div(Att("qkg-row-tools"));
            tools.appendChild(Text("span", "qkg-row-key", v.VerseKey));

            if (callbacks.OnListen is object)
                tools.appendChild(Tool(UIcons.PlayAlt, string.Format("Listen from {0}".t(), v.VerseKey), () => callbacks.OnListen(v)));
            tools.appendChild(Tool(UIcons.Copy, string.Format("Copy {0}".t(), v.VerseKey), () => Copy(v)));
            tools.appendChild(Tool(UIcons.BookOpenReader, string.Format("Study {0}: its words, tafsir, hadith and related verses".t(), v.VerseKey), () => callbacks.OnVerse?.Invoke(v, s)));
            if (!string.IsNullOrEmpty(v.Sajda)) tools.appendChild(Text("span", "qkg-row-sajda", "۩")).setAttribute("title", string.Format("A verse of prostration ({0})".t(), v.Sajda));
            row.appendChild(tools);

            var body   = Div(Att("qkg-row-body"));
            var arabic = Div(Att("qkg-row-ar"));
            arabic.setAttribute("dir", "rtl");
            arabic.setAttribute("lang", "ar");
            AppendWords(arabic, v, _tajweed);
            arabic.appendChild(Text("span", "qkg-ayah", "۝" + Panels.ArabicDigits(v.Ayah)));
            body.appendChild(arabic);

            if (translation)
            {
                body.appendChild(Text("div", "qkg-row-en", v.Translation ?? ""));
                body.appendChild(Text("div", "qkg-row-src", "Saheeh International"));
            }
            row.appendChild(body);

            Wire(row, v, s, callbacks);
            return row;
        }

        /// <summary>A small round button in a verse's tools; its click does not also select the row.</summary>
        private static HTMLElement Tool(UIcons icon, string label, Action action)
        {
            var button = Text("button", "qkg-iconbtn qkg-row-tool", "");
            button.setAttribute("type", "button");
            button.setAttribute("aria-label", label);
            button.title = label;
            button.appendChild(Icon(icon).Render());
            button.onclick = e => { e.stopPropagation(); action(); };
            return button;
        }

        /// <summary>Copies the verse — its Arabic, its translation and its key — to the clipboard.</summary>
        private static void Copy(QK.ReadingVerse v)
        {
            var text = (v.Text ?? "") + "\n" + (v.Translation ?? "") + "\n— " + v.VerseKey;
            try { navigator.clipboard.writeText(text); Toast().Success(string.Format("{0} copied".t(), v.VerseKey)); }
            catch { /* no clipboard in this context */ }
        }

        /// <summary>The margin note a flowing passage carries: its number and range, its distinctive words, its theme.</summary>
        private static HTMLElement Aside(QK.ReadingSection s, Callbacks callbacks)
        {
            var aside = Div(Att("qkg-pas-aside"));
            aside.appendChild(Text("span", "qkg-kicker", string.Format("Passage {0}".t(), s.Ordinal)));

            var where = Text("span", "qkg-pas-aside-range qkg-clickable", Range(s));
            where.title   = "Open the passage: its key words, the passages either side".t();
            where.onclick = e => { e.stopPropagation(); callbacks.OnPassage(s.PassageUid); };
            aside.appendChild(where);

            if (!string.IsNullOrEmpty(s.LabelArabic))
            {
                var label = Text("span", "qkg-pas-aside-ar", s.LabelArabic, rtl: true);
                label.title = "The words this passage uses far more than the rest of the Quran".t();
                aside.appendChild(label);
            }
            var english = (s.Label ?? "").Split(new[] { " · " }, StringSplitOptions.None);
            if (english.Length > 1) aside.appendChild(Text("span", "qkg-note", string.Join(" · ", english.Skip(1))));

            if (UID128.IsNotNull(s.ThemeUid))
            {
                var chip = Text("span", "qkg-pas-theme qkg-clickable", "");
                chip.style.setProperty("--qkg-chip", Clusters.ColorOf(s.ThemeOrdinal));
                chip.appendChild(Text("span", "qkg-dot", ""));
                chip.appendChild(Text("span", "qkg-pas-theme-ar", s.ThemeLabelArabic, rtl: true));
                chip.appendChild(Text("span", "qkg-pas-theme-n", string.Format("{0} surahs".t(), s.ThemeSurahs)));
                chip.title   = string.Format("Theme shared with {0} surahs: {1}. Open it to read them together.".t(), s.ThemeSurahs, s.ThemeLabel);
                chip.onclick = e => { e.stopPropagation(); callbacks.OnTheme(s.ThemeUid); };
                aside.appendChild(chip);
            }
            else
            {
                aside.appendChild(Text("span", "qkg-note", "No theme shared with other surahs".t()));
            }

            if (UID128.IsNotNull(s.GroupUid))
            {
                var chip = Text("span", "qkg-pas-theme qkg-pas-group qkg-clickable", "");
                chip.style.setProperty("--qkg-chip", Clusters.GroupColorOf(s.GroupOrdinal));
                chip.appendChild(Text("span", "qkg-dot", ""));
                chip.appendChild(Text("span", "qkg-pas-theme-ar", s.GroupLabelArabic, rtl: true));
                chip.appendChild(Text("span", "qkg-pas-theme-n", string.Format("similar · {0} surahs".t(), s.GroupSurahs)));
                chip.onclick = e => { e.stopPropagation(); callbacks.OnTheme(s.GroupUid); };
                aside.appendChild(chip);
            }
            return aside;
        }

        /// <summary>The line a passage opens with: where it is, its distinctive words, and the theme it shares.</summary>
        private static HTMLElement Header(QK.ReadingSection s, Callbacks callbacks)
        {
            var head = Div(Att("qkg-pas-head"));
            head.appendChild(Text("span", "qkg-pas-glyph", "◇"));

            var where = Text("span", "qkg-pas-where", string.Format("Passage {0} · {1}".t(), s.Ordinal, Range(s)));
            where.classList.add("qkg-clickable");
            where.title   = "Open the passage: its key words, the passages either side".t();
            where.onclick = _ => callbacks.OnPassage(s.PassageUid);
            head.appendChild(where);

            if (!string.IsNullOrEmpty(s.LabelArabic))
            {
                var label = Text("span", "qkg-pas-label", s.LabelArabic, rtl: true);
                label.title = "The words this passage uses far more than the rest of the Quran".t();
                head.appendChild(label);
            }

            if (UID128.IsNotNull(s.ThemeUid))
            {
                var chip = Text("span", "qkg-pas-theme qkg-clickable", "");
                chip.style.setProperty("--qkg-chip", Clusters.ColorOf(s.ThemeOrdinal));
                chip.appendChild(Text("span", "qkg-dot", ""));
                chip.appendChild(Text("span", "qkg-pas-theme-ar", s.ThemeLabelArabic, rtl: true));
                chip.appendChild(Text("span", "qkg-pas-theme-n", string.Format("{0} surahs".t(), s.ThemeSurahs)));
                chip.title   = string.Format("Theme shared with {0} surahs: {1}. Open it to read them together.".t(), s.ThemeSurahs, s.ThemeLabel);
                chip.onclick = _ => callbacks.OnTheme(s.ThemeUid);
                head.appendChild(chip);
            }

            if (UID128.IsNotNull(s.GroupUid))
            {
                var chip = Text("span", "qkg-pas-theme qkg-pas-group qkg-clickable", "");
                chip.style.setProperty("--qkg-chip", Clusters.GroupColorOf(s.GroupOrdinal));
                chip.appendChild(Text("span", "qkg-dot", ""));
                chip.appendChild(Text("span", "qkg-pas-theme-ar", s.GroupLabelArabic, rtl: true));
                chip.appendChild(Text("span", "qkg-pas-theme-n", string.Format("similar · {0} surahs".t(), s.GroupSurahs)));
                chip.title   = string.Format("Similarity group (Arabic meaning, shared lemmas and roots) with passages of {0} surahs. Open it to read them together.".t(), s.GroupSurahs);
                chip.onclick = _ => callbacks.OnTheme(s.GroupUid);
                head.appendChild(chip);
            }

            return head;
        }

        private HTMLElement VerseArabic(QK.ReadingVerse v, QK.ReadingSection s, Callbacks callbacks)
        {
            var span = Span(Att("qkg-v"));
            AppendWords(span, v, _tajweed);

            var mark = Text("span", "qkg-ayah", "۝" + Panels.ArabicDigits(v.Ayah));
            span.appendChild(mark);
            if (!string.IsNullOrEmpty(v.Sajda))
            {
                var sajda = Text("span", "qkg-sajda", "۩");
                sajda.title = string.Format("A verse of prostration ({0})".t(), v.Sajda);
                span.appendChild(sajda);
            }

            Wire(span, v, s, callbacks);
            return span;
        }

        private HTMLElement VerseEnglish(QK.ReadingVerse v, QK.ReadingSection s, Callbacks callbacks)
        {
            var span = Span(Att("qkg-v"));
            span.appendChild(Text("sup", "qkg-en-n", v.Ayah.ToString()));
            span.appendChild(document.createTextNode(v.Translation ?? ""));
            Wire(span, v, s, callbacks);
            return span;
        }

        private void Wire(HTMLElement span, QK.ReadingVerse v, QK.ReadingSection s, Callbacks callbacks)
        {
            var key = v.Uid.ToString();
            _uids[key] = v.Uid;
            span.setAttribute("data-qkg-verse", key);
            if (!_verses.TryGetValue(key, out var els))
            {
                _verses[key] = els = new List<HTMLElement>();
                _order.Add((span, v));
            }
            els.Add(span);

            span.title        = v.VerseKey;
            // The words of a reading other than Hafs name their Hafs verse: the word tip resolves it here.
            foreach (var h in v.HafsVerses ?? new UID128[0]) _uids[h.ToString()] = h;
            span.onclick      = _ => callbacks.OnVerse?.Invoke(v, s);
            span.onmouseenter = _ => { foreach (var el in els) el.classList.add("qkg-hover"); };
            span.onmouseleave = _ => { foreach (var el in els) el.classList.remove("qkg-hover"); };
        }

        /// <summary>
        /// The verse's text, its key words wrapped so they can be marked. The Uthmani text's words
        /// are the morphology's words in order once the pause marks (ۚ ۖ ۗ …) and the hizb sign
        /// are skipped; where the counts disagree (four verses, e.g. إِلْ يَاسِينَ) nothing is marked.
        /// </summary>
        private static void AppendWords(HTMLElement into, QK.ReadingVerse v, bool tajweed = false)
        {
            var tokens     = (v.Text ?? "").Split(' ');
            // The tajweed, by character in the text: each word painted from its own offset.
            var colours    = tajweed && !string.IsNullOrEmpty(v.Tajweed) ? TajweedKey.Parse(v.Tajweed) : null;
            var offset     = 0;
            var highlights = v.Highlights ?? new int[0];
            // The positions are the morphology's only when the counts agree; otherwise no word is
            // marked and none carries a position (so no tooltip names the wrong word).
            var aligned    = tokens.Count(IsWord) == v.Words;
            // A reading other than Hafs: each word's own Hafs word (verse, position), not its place here.
            var linked     = v.WordVerse is object && v.WordPosition is object && v.WordVerse.Length == v.Words && v.HafsVerses is object;
            var marks      = highlights.Length > 0 && aligned ? new HashSet<int>(highlights) : null;

            var plain    = new StringBuilder();
            var position = 0;
            foreach (var token in tokens)
            {
                var start = offset;
                offset += token.Length + 1;
                var isWord = IsWord(token);
                if (!isWord)
                {
                    plain.Append(token).Append(' ');
                    continue;
                }

                position++;
                if (plain.Length > 0) { into.appendChild(document.createTextNode(plain.ToString())); plain.Clear(); }
                var word = Text("span", marks is object && marks.Contains(position) ? "qkg-w qkg-kw" : "qkg-w", colours is object ? "" : token);
                if (colours is object) TajweedKey.Paint(word, token, start, colours);
                if (aligned && linked)
                {
                    var at = v.WordVerse[position - 1];
                    if (at >= 0 && at < v.HafsVerses.Length)
                    {
                        word.setAttribute("data-qkg-v", v.HafsVerses[at].ToString());
                        word.setAttribute("data-qkg-p", v.WordPosition[position - 1].ToString());
                    }
                }
                else if (aligned) word.setAttribute("data-qkg-p", position.ToString());
                into.appendChild(word);
                plain.Append(' ');
            }
            if (plain.Length > 0) into.appendChild(document.createTextNode(plain.ToString()));
        }

        /// <summary>A token with an Arabic letter in it: a word, not a pause mark or the hizb sign.</summary>
        private static bool IsWord(string token)
        {
            foreach (var c in token ?? "")
                if ((c >= 'ء' && c <= 'ي') || (c >= 'ٱ' && c <= 'ۓ') || c == 'ە') return true;
            return false;
        }

        internal static string Range(QK.ReadingSection s)
        {
            var verses = s.Verses ?? new QK.ReadingVerse[0];
            if (verses.Length == 0) return s.SurahNumber.ToString();
            return verses.Length == 1 ? verses[0].VerseKey : verses[0].VerseKey + "–" + verses[verses.Length - 1].Ayah;
        }

        private static HTMLElement Text(string tag, string className, string text, bool rtl = false)
        {
            var el = document.createElement(tag);
            el.className   = className;
            el.textContent = text ?? "";
            if (rtl) el.setAttribute("dir", "rtl");
            return el;
        }

        // -----------------------------------------------------------------------------------
        // the style sheet, once
        // -----------------------------------------------------------------------------------

        private static bool _styled;

        /// <summary>The reader's and the cluster map's style sheet, added to the page once.</summary>
        internal static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;

            var style = document.createElement("style");
            style.textContent = CSS.Replace("{QALUN}", Arabic.QALUN_FONT_FAMILY).Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        // No class name may contain "ec-": the icon font's rule span[class*="ec-"] matches it and
        // sets the span in monospace ("qkg-sec-head" did — hence "qkg-pas-"). Marks are colours, not
        // backgrounds: Amiri Quran's line box is twice the letters' height, and so would a background be.
        private const string CSS = @"
.qkg-reader-scroll { background: var(--qkg-bg); color: var(--qkg-ink); }
.qkg-reading-light { --qkg-bg: #ffffff; --qkg-surface: #f6f7f6; --qkg-surface-2: #eef1f0; --qkg-raised: #ffffff; --qkg-line: #e4e8e6; --qkg-line-2: #d3d9d6; --qkg-ink: #1b2422; --qkg-ink-2: #44514d; --qkg-ink-3: #66726e; --qkg-teal: #157a7c; --qkg-teal-strong: #0f6466; --qkg-teal-tint: #e4f2f1; --qkg-teal-tint-2: #cfe7e5; --qkg-gold: #a9802f; --qkg-mark: #b0461b; --qkg-paper: #fcf7ea; --qkg-paper-ink: #2a2214; --qkg-paper-band: #f4ead0; }
.qkg-reading-sepia { --qkg-bg: #f7efe0; --qkg-surface: #f1e6d1; --qkg-surface-2: #eadcc2; --qkg-raised: #fbf5ea; --qkg-line: #e3d4b8; --qkg-line-2: #d6c3a0; --qkg-ink: #3b2f1e; --qkg-ink-2: #5a4a33; --qkg-ink-3: #6f5d43; --qkg-teal: #157a7c; --qkg-teal-strong: #0f6466; --qkg-teal-tint: #e6e7d6; --qkg-teal-tint-2: #d5dcc6; --qkg-gold: #a9802f; --qkg-mark: #b0461b; --qkg-paper: #fbf3e2; --qkg-paper-ink: #3b2f1e; --qkg-paper-band: #f1e2c3; }
.qkg-reading-dark { --qkg-bg: #121615; --qkg-surface: #1a1f1e; --qkg-surface-2: #232928; --qkg-raised: #1a1f1e; --qkg-line: #2b3331; --qkg-line-2: #3a4442; --qkg-ink: #e7eceb; --qkg-ink-2: #b9c3c0; --qkg-ink-3: #95a19d; --qkg-teal: #5cc2c2; --qkg-teal-strong: #7fd4d3; --qkg-teal-tint: #173433; --qkg-teal-tint-2: #1f4443; --qkg-gold: #d2ad5f; --qkg-mark: #ef8f62; --qkg-paper: #1d1a14; --qkg-paper-ink: #efe6d2; --qkg-paper-band: #2a2417; }
.qkg-reader { max-width: 980px; margin: 0 auto; padding: 12px 32px 120px 32px; color: var(--qkg-ink); box-sizing: border-box; }
@media (max-width: 700px) { .qkg-reader { padding: 8px 16px 96px 16px; } .qkg-rule { display: none; } .qkg-row { grid-template-columns: 44px minmax(0, 1fr); gap: 10px; } }
.qkg-surah-title { text-align: center; padding: 16px 0 24px 0; }
.qkg-surah-name-row { display: flex; align-items: center; justify-content: center; gap: 18px; }
.qkg-rule { flex: 0 1 180px; height: 1px; background: var(--qkg-gold); opacity: .55; }
.qkg-surah-name { font-family: {FONT}; font-size: calc(var(--qkg-size) * 1.4); line-height: 1.7; }
.qkg-bismillah { font-family: {FONT}; font-size: var(--qkg-size); line-height: 2; padding-top: 6px; }
.qkg-surah-heading { display: flex; align-items: baseline; gap: 12px; justify-content: center; margin: 34px 0 6px 0; padding-bottom: 6px; border-bottom: 1px solid var(--qkg-line); }
.qkg-surah-heading-ar { font-family: {FONT}; font-size: calc(var(--qkg-size) * 0.85); }
.qkg-surah-heading-latin { font-size: 13px; color: var(--qkg-ink-3); }
.qkg-pas { --qkg-theme: var(--qkg-line-2); }
.qkg-pas-head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; margin: 22px 0 6px 0; padding: 12px 16px; border-radius: 14px; background: var(--qkg-surface); font-size: 13px; color: var(--qkg-ink-3); box-shadow: inset 3px 0 0 var(--qkg-theme); }
.qkg-pas-glyph { color: var(--qkg-teal); font-size: 14px; }
.qkg-pas-where { font-weight: 600; font-size: 14px; color: var(--qkg-ink); }
.qkg-pas-label { font-family: {FONT}; font-size: 19px; line-height: 1.4; unicode-bidi: isolate; color: var(--qkg-mark); }
.qkg-pas-theme { display: inline-flex; align-items: center; gap: 6px; padding: 2px 10px; border-radius: 999px; background: var(--qkg-raised); border: 1px solid var(--qkg-line-2); align-self: flex-start; }
.qkg-pas-theme .qkg-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--qkg-chip); }
.qkg-pas-group { border-style: dashed; }
.qkg-pas-group .qkg-dot { border-radius: 2px; }
.qkg-pas-theme-ar { font-family: {FONT}; font-size: 17px; line-height: 1.5; unicode-bidi: isolate; color: var(--qkg-ink); }
.qkg-pas-theme-n { font-size: 12px; color: var(--qkg-ink-3); }
.qkg-pas-side { display: grid; grid-template-columns: minmax(0, 1fr) 220px; gap: 32px; align-items: start; padding: 18px 0; border-top: 1px solid var(--qkg-line); }
.qkg-pas-aside { position: sticky; top: 12px; display: flex; flex-direction: column; gap: 8px; padding: 16px; border-radius: 14px; background: var(--qkg-surface); box-shadow: inset 3px 0 0 var(--qkg-theme); font-size: 13px; }
.qkg-pas-aside-range { font-weight: 600; font-size: 14px; color: var(--qkg-ink); }
.qkg-pas-aside-ar { font-family: {FONT}; font-size: 22px; line-height: 1.6; color: var(--qkg-mark); text-align: right; }
@media (max-width: 900px) { .qkg-pas-side { grid-template-columns: minmax(0, 1fr); } .qkg-pas-aside { position: static; order: -1; } }
.qkg-clickable { cursor: pointer; }
.qkg-clickable:hover { text-decoration: underline; }
.qkg-body { display: flex; flex-direction: row-reverse; flex-wrap: wrap; gap: 8px 36px; }
.qkg-ar { flex: 1 1 100%; font-family: {FONT}; font-size: var(--qkg-size); line-height: 2.3; text-align: justify; text-align-last: right; word-spacing: 2px; color: var(--qkg-ink); }
.qkg-two .qkg-ar { flex: 1 1 380px; }
.qkg-en { flex: 1 1 320px; font-size: 16px; line-height: 1.75; color: var(--qkg-ink-2); padding-top: 8px; }
.qkg-en-n { font-size: 10px; font-weight: 600; color: var(--qkg-ink-3); margin-right: 3px; }
.qkg-v { cursor: pointer; border-radius: 6px; padding: 2px 0; transition: background-color .12s; -webkit-box-decoration-break: clone; box-decoration-break: clone; }
.qkg-v.qkg-hover { background-color: var(--qkg-surface-2); }
.qkg-v.qkg-on { background-color: var(--qkg-teal-tint); box-shadow: 0 0 0 2px var(--qkg-teal-tint); }
.qkg-ayah { font-family: {FONT}; color: var(--qkg-gold); padding: 0 4px; white-space: nowrap; }
.qkg-v.qkg-play, .qkg-v.qkg-play .qkg-ayah { color: var(--qkg-teal-strong); }
.qkg-w { border-radius: 4px; }
.qkg-w.qkg-tip { color: var(--qkg-teal-strong); text-decoration: underline; text-decoration-color: var(--qkg-teal); text-underline-offset: .45em; }
.qkg-row { display: grid; grid-template-columns: 64px minmax(0, 1fr); gap: 20px; padding: 26px 12px 26px 0; border-bottom: 1px solid var(--qkg-line); border-radius: 14px; cursor: pointer; transition: background-color .12s; }
.qkg-row-tools { display: flex; flex-direction: column; align-items: center; gap: 2px; color: var(--qkg-ink-3); }
.qkg-row-key { font-size: 14px; font-weight: 600; color: var(--qkg-teal-strong); margin-bottom: 4px; }
.qkg-row-tool { width: 32px; height: 32px; font-size: 16px; opacity: .75; }
.qkg-row-tool > * { font-size: 16px; }
.qkg-row:hover .qkg-row-tool, .qkg-row.qkg-on .qkg-row-tool { opacity: 1; }
.qkg-row-sajda { color: var(--qkg-gold); font-size: 18px; }
.qkg-row-body { min-width: 0; }
.qkg-row-ar { font-family: {FONT}; font-size: var(--qkg-size); line-height: 2.1; text-align: right; color: var(--qkg-ink); }
.qkg-riwaya-qalun .qkg-ar, .qkg-riwaya-qalun .qkg-row-ar, .qkg-riwaya-qalun .qkg-bismillah { font-family: {QALUN}; }
.qkg-row-en { font-size: 17px; line-height: 1.65; color: var(--qkg-ink-2); margin-top: 12px; }
.qkg-row-src { font-size: 12px; color: var(--qkg-ink-3); margin-top: 6px; }
.qkg-row.qkg-hover { background-color: var(--qkg-surface); }
.qkg-row.qkg-on { background-color: var(--qkg-teal-tint); border-bottom-color: transparent; }
.qkg-row.qkg-play { background-color: var(--qkg-teal-tint); box-shadow: inset 3px 0 0 var(--qkg-teal); }
.qkg-row.qkg-play .qkg-row-ar, .qkg-row.qkg-play .qkg-ayah { color: var(--qkg-teal-strong); }
.qkg-layout-pages { max-width: 1160px; display: flex; flex-wrap: wrap; flex-direction: row-reverse; justify-content: center; gap: 0; }
.qkg-layout-pages .qkg-surah-title { flex: 1 1 100%; }
.qkg-page { flex: 1 1 400px; max-width: 540px; box-sizing: border-box; margin: 0 0 24px 0; padding: 20px; background: var(--qkg-paper); color: var(--qkg-paper-ink); box-shadow: 0 10px 30px rgba(60, 40, 10, .14); }
.qkg-layout-pages .qkg-page:nth-of-type(odd) { border-radius: 4px 14px 14px 4px; }
.qkg-layout-pages .qkg-page:nth-of-type(even) { border-radius: 14px 4px 4px 14px; }
.qkg-page-frame { height: 100%; box-sizing: border-box; border: 2px solid var(--qkg-gold); outline: 1px solid var(--qkg-gold); outline-offset: -7px; border-radius: 6px; padding: 16px 24px 12px 24px; display: flex; flex-direction: column; gap: 6px; }
.qkg-page-top { display: flex; justify-content: space-between; font-family: 'Amiri', serif; font-size: 15px; color: var(--qkg-gold); }
.qkg-page .qkg-ar { color: var(--qkg-paper-ink); }
.qkg-page-text { flex: 1 1 auto; text-align: justify; text-align-last: center; line-height: 2.3; }
.qkg-page .qkg-surah-heading { margin: 8px 0 4px 0; padding: 4px; border: 1.5px solid var(--qkg-gold); border-radius: 10px; background: var(--qkg-paper-band); justify-content: center; }
.qkg-page .qkg-surah-heading-latin { color: var(--qkg-gold); }
.qkg-page-foot { display: flex; justify-content: center; padding: 8px 0 0 0; }
.qkg-page-n { font-family: 'Amiri', serif; font-size: 16px; color: var(--qkg-gold); border: 1px solid var(--qkg-gold); border-radius: 999px; padding: 0 12px; }
.qkg-sajda { color: var(--qkg-gold); padding-left: 2px; }
.qkg-keywords .qkg-kw { color: var(--qkg-mark); }
.qkg-strip { display: flex; gap: 3px; height: 6px; width: auto !important; flex: 0 0 auto; box-sizing: content-box; }
.qkg-strip-seg { border-radius: 999px; cursor: pointer; min-width: 3px; opacity: .9; }
.qkg-strip-seg:hover { opacity: 1; outline: 2px solid var(--qkg-teal); outline-offset: 2px; }
.qkg-map { display: grid; grid-template-columns: 190px 1fr; gap: 5px 14px; align-items: center; }
.qkg-map-label { font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: var(--qkg-ink-2); cursor: pointer; display: flex; gap: 8px; align-items: baseline; }
.qkg-map-label:hover { color: var(--qkg-teal-strong); }
.qkg-map-label-ar { font-family: {FONT}; font-size: 15px; line-height: 1.2; unicode-bidi: isolate; color: var(--qkg-ink-3); }
.qkg-map .qkg-strip { height: 14px; gap: 2px; box-sizing: border-box; }
.qkg-map .qkg-strip-seg { border-radius: 3px; opacity: .8; }
.qkg-map.qkg-focus .qkg-strip-seg { opacity: .16; }
.qkg-map.qkg-focus .qkg-strip-seg.qkg-lit { opacity: 1; }
.qkg-map.qkg-focus .qkg-map-label { opacity: .45; }
.qkg-map.qkg-focus .qkg-map-label.qkg-lit { opacity: 1; color: var(--qkg-ink); font-weight: 600; }
@media (max-width: 700px) { .qkg-map { grid-template-columns: 110px 1fr; } }
";
    }
}
