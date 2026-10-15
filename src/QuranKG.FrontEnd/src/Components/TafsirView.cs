using System;
using System.Collections.Generic;
using System.Linq;
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
    /// What the tafsirs say on a verse, in Arabic and in English: al-Mukhtasar (the same short
    /// explanation in both languages), al-Jalalayn and Ibn Kathir, one at a time — the reader's
    /// choice is kept. A commentary that explains several verses together says so. A long one (Ibn
    /// Kathir) shows its opening, and opens whole in two columns, the Arabic beside the English.
    ///
    /// <para>For people only: qkg/verse/tafsir is no AI tool (Ibn Kathir's text is where QurSim, the
    /// similarity answer key, was drawn from).</para>
    /// </summary>
    internal static class TafsirView
    {
        private const string STORAGE_KEY  = "qkg-tafsir-work";
        private const string DEFAULT_WORK = "mukhtasar";
        /// <summary>Above this many characters in either language, the panel shows the opening and offers the whole.</summary>
        private const int    CLIP_AT      = 1400;

        private static bool _styled;

        /// <summary>The tafsir of a verse; <paramref name="heading"/> false where the page titles it already.</summary>
        internal static IComponent Create(UID128 verse, bool heading = true)
        {
            EnsureStyles();
            Arabic.EnsureFonts();

            var host = VStack().WS().Gap(8.px());

            void Show(string work)
            {
                Remember(work);
                host.Clear();
                host.Add(Defer(async () => Render(await QuranApi.VerseTafsirAsync(verse, work), Show, heading),
                               VStack().WS().Gap(6.px()).Children(Heading(heading), TextBlock("Opening the tafsir…".t()).Small().Secondary())));
            }

            Show(Saved());
            return host;
        }

        // -----------------------------------------------------------------------------------

        private static IComponent Render(QK.VerseTafsirDto dto, Action<string> show, bool heading)
        {
            var works = dto.Works ?? new QK.TafsirWork[0];
            if (works.Length == 0)
                return VStack().WS().Gap(6.px()).Children(
                    Heading(heading),
                    TextBlock("No tafsir in the graph yet: `just fetch`, then `just ingest local --only tafsir`.".t()).Small().Secondary());

            var current = works.FirstOrDefault(w => w.Work == dto.Work) ?? works[0];

            var picker = Look.Segmented(works.Select(w => w.Name).ToArray(), Array.IndexOf(works, current), i => show(works[i].Work), "Tafsir".t());
            var pickerButtons = picker.querySelectorAll("button");
            for (var i = 0; i < pickerButtons.length && i < works.Length; i++) pickerButtons[i].As<HTMLElement>().title = works[i].NameArabic + " — " + works[i].Author;

            var who = Look.El("div", "qkg-tafsir-who");
            who.appendChild(Look.Ar(current.NameArabic, "qkg-arw qkg-tafsir-who-ar"));
            who.appendChild(Look.El("span", "qkg-note", current.Author));

            var section = VStack().WS().Gap(12.px()).Children(
                HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(Heading(heading).Grow(), Raw(picker)),
                Raw(who));

            if (current.Arabic is null && current.English is null)
            {
                section.Add(TextBlock(string.Format("{0} says nothing on this verse — it passes over verses it holds need no explanation.".t(), current.Name)).Small().Secondary());
                return section;
            }

            var range = Together(current.Arabic) ?? Together(current.English);
            if (range is object)
                section.Add(TextBlock(string.Format("Explains {0} together.".t(), range)).Tiny().Secondary());

            var arabic  = current.Arabic?.Text  ?? "";
            var english = current.English?.Text ?? "";
            var clip    = arabic.Length > CLIP_AT || english.Length > CLIP_AT;

            var pair = Look.El("div", "qkg-tafsir-pair");
            pair.appendChild(Block(current.Arabic,  "ar", clip, current.Name));
            pair.appendChild(Block(current.English, "en", clip, current.Name));
            section.Add(Raw(pair).WS());

            if (clip)
                section.Add(Raw(Look.Button("Read the whole commentary".t(), () => OpenWhole(dto.VerseKey, current), "soft", "☰", "The Arabic and the English side by side".t())));

            return section;
        }

        /// <summary>"2:1–5" when the commentary covers more than this verse, else null.</summary>
        private static string Together(QK.TafsirText t)
            => t is null || t.FirstAyah == t.LastAyah ? null : t.FirstVerseKey + "–" + t.LastAyah;

        private static HTMLElement Block(QK.TafsirText t, string language, bool clip, string workName)
        {
            if (t is null)
                return Text("div", "qkg-tafsir-none",
                    language == "ar" ? string.Format("No Arabic text of {0} on this verse.".t(), workName)
                                     : string.Format("No English text of {0} on this verse.".t(), workName));

            var block = Div(Att("qkg-tafsir qkg-tafsir-" + language));

            var body = Div(Att("qkg-tafsir-body" + (clip ? " qkg-tafsir-clip" : "")));
            Paragraphs(body, t.Text, language);
            // The verses it cites ("[2:255]") open in a reader, as in the chat.
            VerseReferences.LinkifyNow(body);
            block.appendChild(body);
            block.appendChild(Text("div", "qkg-tafsir-source", t.Attribution));
            return block;
        }

        /// <summary>The commentary as paragraphs; the Quran it quotes (﴿…﴾) marked; the English edition's short title lines as headings.</summary>
        private static void Paragraphs(HTMLElement into, string text, string language)
        {
            if (language == "ar") into.setAttribute("dir", "rtl");

            foreach (var raw in (text ?? "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var paragraph = raw.Trim();
                if (paragraph.Length == 0) continue;

                if (language == "en" && IsTitle(paragraph))
                {
                    into.appendChild(Text("div", "qkg-tafsir-title", paragraph));
                    continue;
                }

                // The English edition quotes the Arabic of a verse on a line of its own.
                if (language == "en" && IsArabic(paragraph))
                {
                    var quote = Text("div", "qkg-tafsir-arline", paragraph);
                    quote.setAttribute("dir", "rtl");
                    into.appendChild(quote);
                    continue;
                }

                var p = document.createElement("p");
                var at = 0;
                while (at < paragraph.Length)
                {
                    var open = paragraph.IndexOf('﴿', at);
                    var close = open < 0 ? -1 : paragraph.IndexOf('﴾', open + 1);
                    if (open < 0 || close < 0)
                    {
                        p.appendChild(document.createTextNode(paragraph.Substring(at)));
                        break;
                    }
                    if (open > at) p.appendChild(document.createTextNode(paragraph.Substring(at, open - at)));
                    p.appendChild(Text("span", "qkg-tafsir-quran", paragraph.Substring(open, close - open + 1)));
                    at = close + 1;
                }
                into.appendChild(p);
            }
        }

        /// <summary>A paragraph whose letters are mostly Arabic.</summary>
        private static bool IsArabic(string paragraph)
        {
            int arabic = 0, latin = 0;
            foreach (var c in paragraph)
            {
                if (c >= '\u0600' && c <= '\u06FF') arabic++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            }
            return arabic > 0 && arabic > 3 * latin;
        }

        /// <summary>"The Virtue of Ayat Al-Kursi": short, one line, no closing punctuation.</summary>
        private static bool IsTitle(string paragraph)
        {
            if (paragraph.Length > 90 || paragraph.IndexOf('\n') >= 0) return false;
            var last = paragraph[paragraph.Length - 1];
            return char.IsLetter(last) && char.IsUpper(paragraph[0]);
        }

        /// <summary>The whole commentary, the Arabic beside the English, each column scrolling on its own.</summary>
        private static void OpenWhole(string verseKey, QK.TafsirWork work)
        {
            var range = Together(work.Arabic) ?? Together(work.English) ?? verseKey;

            var header = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(
                Arabic.Inline(work.NameArabic, 22),
                TextBlock(work.Name).SemiBold(),
                Badge(range).Foreground(Theme.Colors.Blue700).Outline(),
                TextBlock(work.Author).Small().Secondary());

            HTMLElement Column(QK.TafsirText t, string language)
            {
                var column = Div(Att("qkg-tafsir-column"));
                column.appendChild(Block(t, language, clip: false, workName: work.Name));
                return column;
            }

            var columns = Div(Att("qkg-tafsir-columns"));
            columns.appendChild(Column(work.Arabic,  "ar"));
            columns.appendChild(Column(work.English, "en"));

            var modal = Modal(header).Width(92.vw()).Height(88.vh()).LightDismiss().ShowCloseButton();
            modal.Content = VStack().WS().H(76.vh()).Children(Raw(columns).S());
            modal.Show();
        }

        // -----------------------------------------------------------------------------------

        private static IComponent Heading(bool shown) => shown ? Raw(Look.El("h3", "qkg-h3", "Tafsir".t())) : Raw(Look.El("span", ""));

        private static string Saved()
        {
            try
            {
                var saved = localStorage.getItem(STORAGE_KEY);
                return string.IsNullOrEmpty(saved) ? DEFAULT_WORK : saved;
            }
            catch { return DEFAULT_WORK; }
        }

        private static void Remember(string work)
        {
            try { localStorage.setItem(STORAGE_KEY, work); }
            catch { /* nothing to keep it in */ }
        }

        private static HTMLElement Text(string tag, string className, string text)
        {
            var el = document.createElement(tag);
            el.className   = className;
            el.textContent = text ?? "";
            return el;
        }

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;

            var style = document.createElement("style");
            style.textContent = @"
.qkg-tafsir-who { display: flex; flex-wrap: wrap; align-items: baseline; gap: 10px; }
.qkg-tafsir-who-ar { font-size: 20px; font-weight: 700; }
.qkg-tafsir-pair { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(360px, 100%), 1fr)); gap: 16px; align-items: start; }
.qkg-tafsir { background: var(--qkg-raised); border: 1px solid var(--qkg-line); border-radius: 16px; padding: 18px 20px; color: var(--qkg-ink); }
.qkg-tafsir-ar { order: -1; }
.qkg-tafsir-body p { margin: 0 0 10px 0; }
.qkg-tafsir-ar .qkg-tafsir-body {
  font-family: 'Amiri', 'Noto Naskh Arabic', 'Geeza Pro', serif;
  font-size: 20px;
  line-height: 2;
  text-align: justify;
}
.qkg-tafsir-en .qkg-tafsir-body { font-size: 15px; line-height: 1.7; color: var(--qkg-ink-2); }
.qkg-tafsir-quran { color: var(--qkg-teal-strong); }
.qkg-tafsir-arline { font-family: 'Amiri', 'Noto Naskh Arabic', 'Geeza Pro', serif; font-size: 20px; line-height: 2; color: var(--qkg-teal-strong); margin: 4px 0 10px 0; }
.qkg-tafsir-title { font-weight: 600; font-size: 14px; margin: 14px 0 6px 0; color: var(--qkg-ink); }
.qkg-tafsir-source, .qkg-tafsir-none { font-size: 12px; color: var(--qkg-ink-3); }
.qkg-tafsir-source { padding-top: 6px; }
.qkg-tafsir-none { font-size: 13px; padding: 12px 0; }
.qkg-tafsir-clip {
  max-height: 300px;
  overflow: hidden;
  -webkit-mask-image: linear-gradient(to bottom, black 70%, transparent);
  mask-image: linear-gradient(to bottom, black 70%, transparent);
}
.qkg-tafsir-columns { display: flex; flex-direction: row-reverse; gap: 24px; height: 100%; width: 100%; }
.qkg-tafsir-column { flex: 1 1 0; min-width: 0; overflow-y: auto; padding: 0 8px; }
.qkg-tafsir-column .qkg-tafsir { border: 0; padding: 0; background: transparent; }
@media (max-width: 800px) { .qkg-tafsir-columns { flex-direction: column; overflow-y: auto; } .qkg-tafsir-column { overflow: visible; } }
";
            document.head.appendChild(style);
        }
    }
}
