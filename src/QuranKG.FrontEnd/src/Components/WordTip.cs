using System;
using System.Collections.Generic;
using System.Linq;
using QuranKG.FrontEnd.Api;
using Tesserae;
using TNT;
using UID;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// A word of the text, hovered: a small card under it with the word, its segments, its root and
    /// its lemma (each opening its node) and its part of speech, from the Quranic Arabic Corpus
    /// (qkg/verse/words, fetched once per verse). The reader wraps each word in a span carrying its
    /// position in the verse (<c>data-qkg-p</c>) inside an element carrying the verse
    /// (<c>data-qkg-verse</c>); one handler on the reader serves every word.
    /// </summary>
    internal static class WordTip
    {
        private const int SHOW_AFTER = 260;
        private const int HIDE_AFTER = 220;

        private static readonly Dictionary<string, QK.WordRow[]> _cache = new Dictionary<string, QK.WordRow[]>();
        private static HTMLElement _tip;
        private static HTMLElement _word;
        private static int _generation;
        private static double _hideTimer;

        /// <summary>Serves every word under <paramref name="root"/>; <paramref name="resolve"/> turns a verse's key into its UID.</summary>
        internal static void Attach(HTMLElement root, Func<string, UID128> resolve)
        {
            Look.EnsureStyles();
            EnsureTip();

            root.addEventListener("mouseover", e =>
            {
                var target = e.target.As<HTMLElement>();
                if (target is null || target.classList is null || !target.classList.contains("qkg-w")) return;
                var position = target.getAttribute("data-qkg-p");
                if (string.IsNullOrEmpty(position)) return;
                // A word of a reading other than Hafs names its Hafs verse; any other word is its verse's.
                var own     = target.getAttribute("data-qkg-v");
                var verseEl = target.closest("[data-qkg-verse]");
                if (string.IsNullOrEmpty(own) && verseEl is null) return;
                var uid = resolve(string.IsNullOrEmpty(own) ? verseEl.getAttribute("data-qkg-verse") : own);
                if (UID128.IsNull(uid)) return;

                window.clearTimeout(_hideTimer);
                var generation = ++_generation;
                window.setTimeout(_ => { if (generation == _generation) Show(target, uid, int.Parse(position), generation); }, SHOW_AFTER);
            });

            root.addEventListener("mouseout", e =>
            {
                var target = e.target.As<HTMLElement>();
                if (target is null || target.classList is null || !target.classList.contains("qkg-w")) return;
                _generation++;
                ScheduleHide();
            });
        }

        // -----------------------------------------------------------------------------------

        private static async void Show(HTMLElement word, UID128 verse, int position, int generation)
        {
            QK.WordRow[] words;
            var key = verse.ToString();
            if (!_cache.TryGetValue(key, out words))
            {
                try { words = (await QuranApi.VerseWordsAsync(verse)).Words ?? new QK.WordRow[0]; }
                catch { return; }
                _cache[key] = words;
            }
            if (generation != _generation || !word.isConnected) return;

            var w = words.FirstOrDefault(x => x.Position == position);
            if (w is null) return;

            Mark(word);
            Fill(w);

            var rect = word.getBoundingClientRect().As<DOMRect>();
            _tip.style.display = "flex";
            var width = _tip.offsetWidth;
            var left  = Math.Max(8, Math.Min(window.innerWidth - width - 8, rect.left + rect.width / 2 - width / 2));
            var top   = rect.bottom + 8;
            if (top + _tip.offsetHeight > window.innerHeight - 8) top = rect.top - _tip.offsetHeight - 8;
            _tip.style.left = left + "px";
            _tip.style.top  = top + "px";
        }

        private static void Fill(QK.WordRow w)
        {
            _tip.innerHTML = "";

            var head = Look.El("div", "qkg-tip-head");
            head.appendChild(Look.El("span", "qkg-tip-pos", w.PartOfSpeech ?? ""));
            head.appendChild(Look.Ar(w.Text, "qkg-arw qkg-tip-word"));
            _tip.appendChild(head);

            if (!string.IsNullOrEmpty(w.Segments)) _tip.appendChild(Look.Ar(w.Segments, "qkg-arw qkg-tip-seg"));

            var grid = Look.El("div", "qkg-tip-grid");
            grid.appendChild(Look.El("span", "qkg-note", "Root".t()));
            grid.appendChild(Link(string.IsNullOrEmpty(w.Root) ? "—" : w.Root, "qkg-tip-root", w.RootUid));
            grid.appendChild(Look.El("span", "qkg-note", "Lemma".t()));
            grid.appendChild(Link(string.IsNullOrEmpty(w.Lemma) ? "—" : w.Lemma, "qkg-tip-lemma", w.LemmaUid));
            if (!string.IsNullOrEmpty(w.Features))
            {
                grid.appendChild(Look.El("span", "qkg-note", "Features".t()));
                grid.appendChild(Look.El("span", "qkg-tip-feat", w.Features));
            }
            _tip.appendChild(grid);

            var open = Look.El("span", "qkg-link qkg-tip-open", "Open the word →".t());
            var uid  = w.Uid;
            Look.OnClick(open, () => { Hide(); Lists.Open(uid); });
            _tip.appendChild(open);
        }

        private static HTMLElement Link(string text, string className, UID128 uid)
        {
            var el = Look.Ar(text, "qkg-arw " + className);
            if (UID128.IsNotNull(uid)) Look.OnClick(el, () => { Hide(); Lists.Open(uid); });
            return el;
        }

        private static void Mark(HTMLElement word)
        {
            if (_word is object) _word.classList.remove("qkg-tip");
            _word = word;
            _word.classList.add("qkg-tip");
        }

        private static void ScheduleHide()
        {
            window.clearTimeout(_hideTimer);
            _hideTimer = window.setTimeout(_ => Hide(), HIDE_AFTER);
        }

        private static void Hide()
        {
            if (_tip is object) _tip.style.display = "none";
            if (_word is object) { _word.classList.remove("qkg-tip"); _word = null; }
        }

        private static void EnsureTip()
        {
            if (_tip is object) return;
            _tip = Look.El("div", "qkg-wordtip");
            _tip.setAttribute("role", "tooltip");
            _tip.style.display = "none";
            _tip.onmouseenter = _ => window.clearTimeout(_hideTimer);
            _tip.onmouseleave = _ => ScheduleHide();
            document.body.appendChild(_tip);

            var style = document.createElement("style");
            style.textContent = @"
.qkg-wordtip { position: fixed; z-index: 2000; width: 260px; flex-direction: column; gap: 8px; padding: 14px 16px; border-radius: 14px; background: var(--qkg-raised); color: var(--qkg-ink); border: 1px solid var(--qkg-line); box-shadow: 0 12px 32px rgba(20, 30, 28, .18); font-size: 13px; }
.qkg-tip-head { display: flex; align-items: baseline; justify-content: space-between; gap: 10px; }
.qkg-tip-pos { font-weight: 600; font-size: 14px; }
.qkg-tip-word { font-size: 28px; line-height: 1.5; }
.qkg-tip-seg { font-size: 17px; color: var(--qkg-ink-3); text-align: right; }
.qkg-tip-grid { display: grid; grid-template-columns: auto 1fr; gap: 4px 12px; align-items: baseline; }
.qkg-tip-grid > :nth-child(even) { text-align: right; }
.qkg-tip-root { font-size: 18px; color: var(--qkg-root); }
.qkg-tip-lemma { font-size: 18px; color: var(--qkg-lemma); }
.qkg-tip-feat { font-size: 12px; color: var(--qkg-ink-2); }
.qkg-tip-open { font-size: 13px; }
";
            document.head.appendChild(style);
        }
    }
}
