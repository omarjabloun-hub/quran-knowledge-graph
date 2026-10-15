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
        internal string Layout    { get; set; } = FLOW;

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

        internal void Save()
        {
            try { localStorage.setItem(_key, FontSize + "|" + (Passages ? 1 : 0) + "|" + (Translation ? 1 : 0) + "|" + (KeyWords ? 1 : 0) + "|" + Layout); }
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
        private string _selected;
        private string _playing;
        private readonly List<(HTMLElement Element, QK.ReadingVerse Verse)> _order = new List<(HTMLElement, QK.ReadingVerse)>();

        public QuranReader(QK.ReadingDto text, ReaderOptions options, Callbacks callbacks)
        {
            EnsureStyles();
            Arabic.EnsureFonts();

            _root = Div(Att("qkg-reader qkg-layout-" + options.Layout));
            _root.style.setProperty("--qkg-size", options.FontSize + "px");
            if (options.KeyWords) _root.classList.add("qkg-keywords");

            var sections = text.Sections ?? new QK.ReadingSection[0];
            var isSurah  = text.Kind == "Surah";

            if (isSurah && sections.Length > 0)
            {
                var s     = sections[0];
                var title = Div(Att("qkg-surah-title"));
                title.appendChild(Text("div", "qkg-surah-name", "سورة " + s.SurahNameArabic, rtl: true));
                title.appendChild(Text("div", "qkg-surah-latin", s.SurahNumber + ". " + s.SurahName));
                // Tanzil prefixes the Bismillah to verse 1 of every surah but 1 (where it IS verse 1)
                // and 9 (which has none); the connector strips it, so it is drawn here as the heading.
                if (s.SurahNumber != 1 && s.SurahNumber != 9) title.appendChild(Text("div", "qkg-bismillah", BISMILLAH, rtl: true));
                _root.appendChild(title);
            }

            if (options.Layout == ReaderOptions.PAGES)
            {
                Pages(sections, isSurah, callbacks);
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
                if (marked) block.appendChild(Header(section, callbacks));

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
                        sheet.appendChild(heading);
                        text = null;
                    }

                    if (text is null)
                    {
                        text = Div(Att("qkg-ar qkg-page-text"));
                        text.setAttribute("dir", "rtl");
                        text.setAttribute("lang", "ar");
                        sheet.appendChild(text);
                    }

                    text.appendChild(VerseArabic(verse, section, callbacks));
                    text.appendChild(document.createTextNode(" "));
                }

                var first = page.First().Verse;
                var foot  = Div(Att("qkg-page-foot"));
                foot.appendChild(Text("span", "qkg-page-n", Panels.ArabicDigits(page.Key), rtl: true));
                foot.appendChild(Text("span", "qkg-page-where", string.Format("Page {0} · Juz {1} · Hizb {2}".t(), page.Key, first.Juz, first.Hizb)));
                sheet.appendChild(foot);

                _root.appendChild(sheet);
            }
        }

        /// <summary>One verse as a row: its key, a way to listen from it, the Arabic, and — if asked — its translation.</summary>
        private HTMLElement VerseRow(QK.ReadingVerse v, QK.ReadingSection s, Callbacks callbacks, bool translation)
        {
            var row  = Div(Att("qkg-row"));
            var head = Div(Att("qkg-row-head"));
            head.appendChild(Text("span", "qkg-row-key", v.VerseKey));
            if (!string.IsNullOrEmpty(v.Sajda)) head.appendChild(Text("span", "qkg-row-sajda", string.Format("prostration ({0})".t(), v.Sajda)));
            if (callbacks.OnListen is object)
            {
                var listen = Text("span", "qkg-row-listen qkg-clickable", "▶ " + "Listen".t());
                listen.title   = "Listen from this verse".t();
                listen.onclick = e => { e.stopPropagation(); callbacks.OnListen(v); };
                head.appendChild(listen);
            }
            row.appendChild(head);

            var arabic = Div(Att("qkg-row-ar"));
            arabic.setAttribute("dir", "rtl");
            arabic.setAttribute("lang", "ar");
            AppendWords(arabic, v);
            arabic.appendChild(Text("span", "qkg-ayah", "۝" + Panels.ArabicDigits(v.Ayah)));
            row.appendChild(arabic);

            if (translation) row.appendChild(Text("div", "qkg-row-en", v.Translation ?? ""));

            Wire(row, v, s, callbacks);
            return row;
        }

        /// <summary>The line a passage opens with: where it is, its distinctive words, and the theme it shares.</summary>
        private static HTMLElement Header(QK.ReadingSection s, Callbacks callbacks)
        {
            var head = Div(Att("qkg-pas-head"));

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
            AppendWords(span, v);

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
            if (!_verses.TryGetValue(key, out var els))
            {
                _verses[key] = els = new List<HTMLElement>();
                _order.Add((span, v));
            }
            els.Add(span);

            span.title        = v.VerseKey;
            span.onclick      = _ => callbacks.OnVerse?.Invoke(v, s);
            span.onmouseenter = _ => { foreach (var el in els) el.classList.add("qkg-hover"); };
            span.onmouseleave = _ => { foreach (var el in els) el.classList.remove("qkg-hover"); };
        }

        /// <summary>
        /// The verse's text, its key words wrapped so they can be marked. The Uthmani text's words
        /// are the morphology's words in order once the pause marks (ۚ ۖ ۗ …) and the hizb sign
        /// are skipped; where the counts disagree (four verses, e.g. إِلْ يَاسِينَ) nothing is marked.
        /// </summary>
        private static void AppendWords(HTMLElement into, QK.ReadingVerse v)
        {
            var tokens     = (v.Text ?? "").Split(' ');
            var highlights = v.Highlights ?? new int[0];
            var marks      = highlights.Length > 0 && tokens.Count(IsWord) == v.Words ? new HashSet<int>(highlights) : null;

            var plain    = new StringBuilder();
            var position = 0;
            foreach (var token in tokens)
            {
                var isWord = IsWord(token);
                if (isWord) position++;

                if (marks is object && isWord && marks.Contains(position))
                {
                    if (plain.Length > 0) { into.appendChild(document.createTextNode(plain.ToString())); plain.Clear(); }
                    into.appendChild(Text("span", "qkg-kw", token));
                    plain.Append(' ');
                }
                else
                {
                    plain.Append(token).Append(' ');
                }
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
            style.textContent = CSS.Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        // No class name may contain "ec-": the icon font's rule span[class*="ec-"] matches it and
        // sets the span in monospace ("qkg-sec-head" did — hence "qkg-pas-"). Marks are colours, not
        // backgrounds: Amiri Quran's line box is twice the letters' height, and so would a background be.
        private const string CSS = @"
.qkg-reader { max-width: 980px; margin: 0 auto; padding: 8px 28px 120px 28px; color: var(--tss-default-foreground-color); }
.qkg-surah-title { text-align: center; padding: 8px 0 18px 0; }
.qkg-surah-name { font-family: {FONT}; font-size: calc(var(--qkg-size) * 1.25); line-height: 1.8; }
.qkg-surah-latin { font-size: 13px; color: var(--tss-secondary-foreground-color); }
.qkg-bismillah { font-family: {FONT}; font-size: calc(var(--qkg-size) * 0.95); line-height: 2; padding-top: 10px; }
.qkg-surah-heading { display: flex; align-items: baseline; gap: 12px; justify-content: center; margin: 34px 0 6px 0; padding-bottom: 6px; border-bottom: 1px solid var(--tss-default-border-color); }
.qkg-surah-heading-ar { font-family: {FONT}; font-size: calc(var(--qkg-size) * 0.85); }
.qkg-surah-heading-latin { font-size: 13px; color: var(--tss-secondary-foreground-color); }
.qkg-pas { --qkg-theme: var(--tss-default-border-color); }
.qkg-pas-head { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 14px; margin: 26px 0 4px 0; padding: 6px 0 6px 12px; border-left: 3px solid var(--qkg-theme); font-size: 13px; color: var(--tss-secondary-foreground-color); }
.qkg-pas-where { font-weight: 600; color: var(--tss-default-foreground-color); }
.qkg-pas-label { font-family: {FONT}; font-size: 19px; line-height: 1.4; unicode-bidi: isolate; color: var(--tss-default-foreground-color); }
.qkg-pas-theme { display: inline-flex; align-items: center; gap: 6px; padding: 1px 10px; border-radius: 12px; border: 1px solid var(--qkg-chip); }
.qkg-pas-theme .qkg-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--qkg-chip); }
.qkg-pas-group { border-style: dashed; }
.qkg-pas-group .qkg-dot { border-radius: 2px; }
.qkg-pas-theme-ar { font-family: {FONT}; font-size: 17px; line-height: 1.5; unicode-bidi: isolate; color: var(--tss-default-foreground-color); }
.qkg-clickable { cursor: pointer; }
.qkg-clickable:hover { text-decoration: underline; }
.qkg-body { display: flex; flex-direction: row-reverse; flex-wrap: wrap; gap: 8px 36px; }
.qkg-ar { flex: 1 1 100%; font-family: {FONT}; font-size: var(--qkg-size); line-height: 2.35; text-align: justify; text-align-last: right; word-spacing: 2px; }
.qkg-two .qkg-ar { flex: 1 1 380px; }
.qkg-en { flex: 1 1 320px; font-size: 15px; line-height: 1.75; color: var(--tss-default-foreground-color); padding-top: 8px; }
.qkg-en-n { font-size: 10px; font-weight: 600; color: var(--tss-secondary-foreground-color); margin-right: 3px; }
.qkg-v { cursor: pointer; border-radius: 6px; padding: 2px 0; transition: background-color .12s; -webkit-box-decoration-break: clone; box-decoration-break: clone; }
.qkg-v.qkg-hover { background-color: var(--tss-default-background-hover-color); }
.qkg-v.qkg-on { background-color: rgba(59, 130, 246, .16); box-shadow: 0 0 0 2px rgba(59, 130, 246, .16); }
.qkg-ayah { font-family: {FONT}; color: #15803d; padding: 0 4px; white-space: nowrap; }
.qkg-v.qkg-play { color: #0369a1; }
.qkg-v.qkg-play .qkg-ayah { color: #0369a1; }
.qkg-row { padding: 14px 12px 16px 12px; border-bottom: 1px solid var(--tss-default-border-color); border-radius: 6px; }
.qkg-row-head { display: flex; align-items: center; gap: 14px; font-size: 13px; color: var(--tss-secondary-foreground-color); }
.qkg-row-key { font-weight: 600; min-width: 48px; }
.qkg-row-sajda { color: #15803d; }
.qkg-row-listen { color: var(--tss-primary-background-color); }
.qkg-row-ar { font-family: {FONT}; font-size: var(--qkg-size); line-height: 2.2; text-align: right; padding: 6px 0 2px 0; }
.qkg-row-en { font-size: 15px; line-height: 1.7; padding-top: 4px; }
.qkg-row { cursor: pointer; transition: background-color .12s; }
.qkg-row.qkg-hover { background-color: var(--tss-default-background-hover-color); }
.qkg-row.qkg-on { background-color: rgba(59, 130, 246, .10); }
.qkg-row.qkg-play { box-shadow: inset 3px 0 0 #0369a1; }
.qkg-row.qkg-play .qkg-row-ar, .qkg-row.qkg-play .qkg-ayah { color: #0369a1; }
.qkg-layout-pages { max-width: 760px; }
.qkg-page { margin: 18px auto 26px auto; padding: 18px 34px 10px 34px; border: 1px solid var(--tss-default-border-color); border-radius: 10px; box-shadow: 0 1px 3px rgba(0,0,0,.06); background: var(--tss-default-background-color); }
.qkg-page-text { text-align: justify; text-align-last: center; line-height: 2.25; }
.qkg-page .qkg-surah-heading { margin: 10px 0 6px 0; }
.qkg-page-foot { display: flex; justify-content: center; align-items: baseline; gap: 12px; padding: 10px 0 2px 0; margin-top: 8px; border-top: 1px dashed var(--tss-default-border-color); font-size: 12px; color: var(--tss-secondary-foreground-color); }
.qkg-page-n { font-family: {FONT}; font-size: 18px; color: var(--tss-default-foreground-color); }
.qkg-sajda { color: #15803d; padding-left: 2px; }
.qkg-keywords .qkg-kw { color: #c2410c; }
.qkg-strip { display: flex; gap: 2px; height: 14px; width: 100%; flex: 0 0 auto; }
.qkg-strip-seg { border-radius: 3px; cursor: pointer; min-width: 3px; opacity: .9; }
.qkg-strip-seg:hover { opacity: 1; outline: 2px solid var(--tss-default-foreground-color); outline-offset: 1px; }
.qkg-map { display: grid; grid-template-columns: 150px 1fr; gap: 3px 10px; align-items: center; }
.qkg-map-label { font-size: 11px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: var(--tss-secondary-foreground-color); cursor: pointer; display: flex; gap: 6px; align-items: baseline; }
.qkg-map-label:hover { color: var(--tss-default-foreground-color); }
.qkg-map-label-ar { font-family: {FONT}; font-size: 14px; line-height: 1.2; unicode-bidi: isolate; }
.qkg-map .qkg-strip { height: 12px; }
.qkg-map.qkg-focus .qkg-strip-seg { opacity: .14; }
.qkg-map.qkg-focus .qkg-strip-seg.qkg-lit { opacity: 1; }
.qkg-map.qkg-focus .qkg-map-label { opacity: .45; }
.qkg-map.qkg-focus .qkg-map-label.qkg-lit { opacity: 1; color: var(--tss-default-foreground-color); font-weight: 600; }
";
    }
}
