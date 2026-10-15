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
    /// Verse references in running text — "21:30", "2:255–257", "(٢١:٣٠)" — made links: clicking one
    /// opens its surah in a reader over the page, on that verse, with its translation and related
    /// verses beside it. The chat's answers read through it (Chat/QuranChatView.cs), so the assistant
    /// can cite and the reader read in one click.
    ///
    /// <para>A reference is taken only when the surah exists and has that verse — the counts come
    /// from the corpus — so "12:45" in prose is a link only if it is a verse. Text in code blocks and
    /// links is left alone. The text is watched, not read once: the chat renders its markdown after
    /// handing the message over.</para>
    /// </summary>
    internal static class VerseReferences
    {
        private static Dictionary<int, QK.SurahStats> _surahs;
        private static Modal _open;
        private static bool  _styled;

        /// <summary>Links the verse references in this component's text, now and as it changes.</summary>
        internal static IComponent Linkify(IComponent content)
        {
            EnsureStyles();

            var host = Div(Att("qkg-vref-host"));
            host.appendChild(content.Render());

            var pending = false;
            void Schedule()
            {
                if (pending) return;
                pending = true;
                window.setTimeout(_ => { pending = false; ScanAsync(host).FireAndForget(); }, 0);
            }

            new MutationObserver((records, observer) => Schedule())
               .observe(host, new MutationObserverInit { childList = true, subtree = true, characterData = true });
            Schedule();

            return Raw(host);
        }

        /// <summary>Links the verse references in an element whose text will not change.</summary>
        internal static void LinkifyNow(HTMLElement element)
        {
            EnsureStyles();
            ScanAsync(element).FireAndForget();
        }

        /// <summary>"21:30" → the reader on that verse; false when it names no verse.</summary>
        internal static bool Open(string verseKey)
        {
            var parts = (verseKey ?? "").Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var surah) || !int.TryParse(parts[1], out var ayah)) return false;
            OpenAsync(surah, ayah, ayah).FireAndForget();
            return true;
        }

        /// <summary>Closes the reader if one is open — before the page underneath navigates.</summary>
        internal static void Close()
        {
            var open = _open;
            _open = null;
            open?.Hide();
        }

        // -----------------------------------------------------------------------------------

        private static async System.Threading.Tasks.Task<Dictionary<int, QK.SurahStats>> SurahsAsync()
        {
            if (_surahs is null)
            {
                var corpus = await QuranApi.CorpusAsync();
                _surahs = (corpus.Overview.SurahRows ?? new QK.SurahStats[0]).ToDictionary(s => s.Number);
            }
            return _surahs;
        }

        private static async System.Threading.Tasks.Task OpenAsync(int surah, int ayah, int last)
        {
            var surahs = await SurahsAsync();
            if (!surahs.TryGetValue(surah, out var s)) return;

            Close();

            var reference = Reference(surah, ayah, last);
            var header = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(
                Arabic.Inline(s.NameArabic, 24),
                TextBlock(s.Number + ". " + s.Name).SemiBold(),
                TextBlock(s.NameEnglish).Small().Secondary(),
                Badge(reference).Foreground(Theme.Colors.Blue700).Outline(),
                TextBlock(string.Format("{0} verses".t(), s.Verses)).Small().Secondary().Grow(),
                Button("Open in the Quran View".t()).SetIcon(UIcons.BookQuran).Compact()
                   .Tooltip("Leave this page and read the surah in the Quran View".t())
                   .OnClick(() => QuranLinks.Go(surah, ayah)));

            var pane = new ReadingPane(new ReadingPane.Settings
            {
                Uid                = s.Uid,
                OptionsKey         = "quran",
                KeyWordsByDefault  = false,
                InitialAyah        = ayah,
                ReadInQuran        = true,
                // A related verse in another surah: this reader moves there, the page underneath stays.
                GoToVerseElsewhere = (uid, key) => Open(key),
            });

            var modal = Modal(header).Width(92.vw()).Height(88.vh()).LightDismiss().ShowCloseButton();
            modal.Content = VStack().WS().H(76.vh()).Children(pane.S());
            modal.OnHide(m => { if (_open == m) _open = null; });
            _open = modal;
            modal.Show();
        }

        private static string Reference(int surah, int ayah, int last)
            => last > ayah ? surah + ":" + ayah + "–" + last : surah + ":" + ayah;

        private static async System.Threading.Tasks.Task ScanAsync(HTMLElement root)
        {
            var surahs = await SurahsAsync();

            var texts = new List<Text>();
            Collect(root, texts);

            foreach (var node in texts)
            {
                var text  = node.nodeValue ?? "";
                var found = Find(text, surahs);
                if (found.Count == 0) continue;

                var fragment = document.createDocumentFragment();
                var at = 0;
                foreach (var r in found)
                {
                    if (r.Start > at) fragment.appendChild(document.createTextNode(text.Substring(at, r.Start - at)));
                    fragment.appendChild(Link(text.Substring(r.Start, r.End - r.Start), r.Surah, r.Ayah, r.Last, surahs[r.Surah]));
                    at = r.End;
                }
                if (at < text.Length) fragment.appendChild(document.createTextNode(text.Substring(at)));

                node.parentNode?.replaceChild(fragment, node);
            }
        }

        private static readonly HashSet<string> Skipped = new HashSet<string> { "A", "PRE", "CODE", "BUTTON", "TEXTAREA", "INPUT", "SCRIPT", "STYLE", "SVG" };

        /// <summary>The text nodes worth reading: not in a link, a code block, a control or an editor.</summary>
        private static void Collect(Node node, List<Text> into)
        {
            var children = node.childNodes;
            var copy     = new List<Node>();
            for (uint i = 0; i < children.length; i++) copy.Add(children[i]);

            foreach (var child in copy)
            {
                if (child.nodeType == 3)
                {
                    var value = child.nodeValue ?? "";
                    if (value.IndexOf(':') >= 0) into.Add((Text)child);
                }
                else if (child.nodeType == 1)
                {
                    var el = (HTMLElement)child;
                    if (Skipped.Contains((el.tagName ?? "").ToUpperInvariant()) || el.isContentEditable) continue;
                    Collect(child, into);
                }
            }
        }

        private static HTMLElement Link(string label, int surah, int ayah, int last, QK.SurahStats s)
        {
            var a = (HTMLAnchorElement)document.createElement("a");
            a.className   = "qkg-vref";
            a.textContent = label;
            a.href        = QuranLinks.For(surah, ayah);
            a.title       = string.Format("{0} · {1} — read it in its surah".t(), Reference(surah, ayah, last), s.Name);
            a.onclick     = e =>
            {
                // Ctrl/⌘-click keeps the browser's own way: the Quran View in a new tab.
                if (e.ctrlKey || e.metaKey || e.shiftKey) return;
                e.preventDefault();
                e.stopPropagation();
                OpenAsync(surah, ayah, last).FireAndForget();
            };
            return a;
        }

        // -----------------------------------------------------------------------------------

        private struct Match
        {
            internal int Start, End, Surah, Ayah, Last;
        }

        /// <summary>
        /// "S:A" and "S:A–B" with 1–3 digits each, Western or Arabic-Indic, not part of a longer run
        /// ("1:2:3", "3.14:15", "10:30am"), and naming a verse that exists.
        /// </summary>
        private static List<Match> Find(string text, Dictionary<int, QK.SurahStats> surahs)
        {
            var found = new List<Match>();
            var n = text.Length;
            var i = 0;

            while (i < n)
            {
                if (!IsDigit(text[i]) || JoinedBefore(text, i)) { i++; continue; }

                var j = Digits(text, i);
                if (j - i > 3 || j >= n || text[j] != ':') { i = j; continue; }

                var k = j + 1;
                var m = Digits(text, k);
                if (m == k || m - k > 3 || JoinedAfter(text, m)) { i = Math.Max(m, k); continue; }

                var surah = Number(text, i, j);
                var ayah  = Number(text, k, m);
                if (!surahs.TryGetValue(surah, out var s) || ayah < 1 || ayah > s.Verses) { i = m; continue; }

                var end  = m;
                var last = ayah;

                var p = m;
                while (p < n && text[p] == ' ') p++;
                if (p < n && (text[p] == '-' || text[p] == '–' || text[p] == '—'))
                {
                    p++;
                    while (p < n && text[p] == ' ') p++;
                    var q = Digits(text, p);
                    if (q > p && q - p <= 3 && !JoinedAfter(text, q))
                    {
                        var to = Number(text, p, q);
                        if (to > ayah && to <= s.Verses) { last = to; end = q; }
                    }
                }

                found.Add(new Match { Start = i, End = end, Surah = surah, Ayah = ayah, Last = last });
                i = end;
            }

            return found;
        }

        private static bool IsDigit(char c) => (c >= '0' && c <= '9') || (c >= '٠' && c <= '٩');

        /// <summary>Digits at <paramref name="i"/> that continue something longer: "1:2:3", "3.14:15", "1/2:3".</summary>
        private static bool JoinedBefore(string text, int i)
        {
            if (i == 0) return false;
            var c = text[i - 1];
            return IsDigit(c) || c == ':' || ((c == '.' || c == '/') && i >= 2 && IsDigit(text[i - 2]));
        }

        /// <summary>Digits ending before <paramref name="i"/> that go on: "1:2:3", "10:30am", "1:2.5". A comma or a full stop that ends the sentence does not.</summary>
        private static bool JoinedAfter(string text, int i)
        {
            if (i >= text.Length) return false;
            var c = text[i];
            return IsDigit(c) || c == ':' || char.IsLetter(c) || ((c == '.' || c == '/') && i + 1 < text.Length && IsDigit(text[i + 1]));
        }

        private static int Digits(string text, int from)
        {
            var i = from;
            while (i < text.Length && IsDigit(text[i])) i++;
            return i;
        }

        private static int Number(string text, int from, int to)
        {
            var value = 0;
            for (var i = from; i < to; i++) value = value * 10 + (text[i] >= '٠' ? text[i] - '٠' : text[i] - '0');
            return value;
        }

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;

            var style = document.createElement("style");
            style.textContent = @"
.qkg-vref-host { width: 100%; }
a.qkg-vref {
  color: var(--tss-primary-background-color);
  text-decoration: underline dotted;
  text-underline-offset: 3px;
  white-space: nowrap;
  cursor: pointer;
}
a.qkg-vref:hover { text-decoration-style: solid; }
";
            document.head.appendChild(style);
        }
    }
}
