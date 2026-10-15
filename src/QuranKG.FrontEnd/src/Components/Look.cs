using System;
using Tesserae;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// The application's look, in one place: a white page, one teal accent, gold for the mushaf's
    /// ornaments (the verse marks, a page's frame), amber for Meccan and blue for Medinan. The
    /// colours are CSS variables (<c>--qkg-*</c>) with a dark variant under the workspace's dark
    /// mode, so every page, the reader and the cards of a node page agree.
    ///
    /// <para>The pieces every page is built from — a page column, a title and its lead, a card, a
    /// chip, a badge, a segmented switch, a stat — are here as small factories returning Tesserae
    /// components. Plain elements carry the classes; <see cref="Raw"/> wraps them.</para>
    ///
    /// <para>No class name may contain <c>ec-</c>: the icon font's rule <c>span[class*="ec-"]</c>
    /// matches it and sets the span in monospace.</para>
    /// </summary>
    internal static class Look
    {
        // Concrete colours, for charts that draw into SVG attributes (where a CSS variable is not read).
        internal const string TEAL          = "#157a7c";
        internal const string MECCAN_CHART  = "#d4914a";
        internal const string MEDINAN_CHART = "#2c5aa0";

        private static bool _styled;

        internal static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            Arabic.EnsureFonts();

            var style = document.createElement("style");
            style.textContent = CSS.Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        // -----------------------------------------------------------------------------------
        // elements
        // -----------------------------------------------------------------------------------

        /// <summary>A plain element with classes and, optionally, text.</summary>
        internal static HTMLElement El(string tag, string className, string text = null)
        {
            var el = document.createElement(tag);
            if (!string.IsNullOrEmpty(className)) el.className = className;
            if (text is object) el.textContent = text;
            return el;
        }

        /// <summary>A plain element holding other elements.</summary>
        internal static HTMLElement El(string tag, string className, params HTMLElement[] children)
        {
            var el = El(tag, className);
            foreach (var child in children) if (child is object) el.appendChild(child);
            return el;
        }

        /// <summary>Arabic inside a left-to-right line: isolated, in Amiri.</summary>
        internal static HTMLElement Ar(string text, string className = "qkg-arw")
        {
            var el = El("span", className, text ?? "");
            el.setAttribute("dir", "rtl");
            el.setAttribute("lang", "ar");
            return el;
        }

        /// <summary>Makes an element a target: a click, the pointer, and Enter or Space from the keyboard.</summary>
        internal static HTMLElement OnClick(HTMLElement el, Action action)
        {
            if (action is null) return el;
            el.classList.add("qkg-click");
            el.setAttribute("tabindex", "0");
            el.onclick = e => { e.stopPropagation(); action(); };
            el.onkeydown = e =>
            {
                var key = e.As<KeyboardEvent>().key;
                if (key == "Enter" || key == " ") { e.preventDefault(); action(); }
            };
            return el;
        }

        // -----------------------------------------------------------------------------------
        // page furniture
        // -----------------------------------------------------------------------------------

        /// <summary>A page: the whole view scrolls, its content in a centred column.</summary>
        internal static IComponent Page(IComponent content, int maxWidth = 1120)
        {
            EnsureStyles();
            return VStack().S().ScrollY().Class("qkg-ui").Children(
                VStack().WS().MaxWidth(maxWidth.px()).Class("qkg-column").Children(content));
        }

        /// <summary>A page's title, the sentence under it, and whatever sits on its right.</summary>
        internal static IComponent Title(string title, string lead = null, IComponent aside = null, string kicker = null)
        {
            var text = VStack().Grow().Gap(8.px()).Style(s => s.setProperty("min-width", "0"));
            if (!string.IsNullOrEmpty(kicker)) text.Add(Raw(El("span", "qkg-kicker", kicker)));
            text.Add(Raw(El("h1", "qkg-h1", title)));
            if (!string.IsNullOrEmpty(lead)) text.Add(Raw(El("p", "qkg-lead", lead)));

            var row = HStack().WS().Wrap().AlignItems(ItemAlign.End).Gap(16.px()).Children(text);
            if (aside is object) row.Add(aside);
            return row;
        }

        /// <summary>A section title with an optional note and a link on the right.</summary>
        internal static IComponent Heading(string title, string note = null, IComponent aside = null)
        {
            var text = VStack().Grow().Gap(4.px()).Children(Raw(El("h2", "qkg-h2", title)));
            if (!string.IsNullOrEmpty(note)) text.Add(Raw(El("p", "qkg-note", note)));
            var row = HStack().WS().Wrap().AlignItems(ItemAlign.End).Gap(12.px()).Children(text);
            if (aside is object) row.Add(aside);
            return row;
        }

        /// <summary>A white card with a hairline border; clickable when given an action.</summary>
        internal static Stack Card(Action onClick = null, params IComponent[] children)
        {
            var card = VStack().WS().Gap(12.px()).Class("qkg-card").Children(children);
            if (onClick is object) OnClick(card.Render(), onClick);
            return card;
        }

        /// <summary>A titled card: its title, one sentence on how to read it, then the content.</summary>
        internal static Stack Panel(string title, string note, IComponent content)
        {
            var card = Card(null, Raw(El("h3", "qkg-h3", title)));
            if (!string.IsNullOrEmpty(note)) card.Add(Raw(El("p", "qkg-note", note)));
            card.Add(content.WS());
            return card;
        }

        /// <summary>Cards side by side, wrapping under each other when there is no room.</summary>
        internal static IComponent Columns(int minWidth, params IComponent[] items)
        {
            var grid = El("div", "qkg-grid");
            grid.style.setProperty("grid-template-columns", "repeat(auto-fit, minmax(min(" + minWidth + "px, 100%), 1fr))");
            var host = Raw(grid).WS();
            foreach (var item in items) if (item is object) grid.appendChild(item.Render());
            return host;
        }

        /// <summary>A big number, what it counts, and an optional icon tile; opens what it counted.</summary>
        internal static IComponent Stat(string value, string label, Action open = null, string iconText = null, string tint = null, string ink = null)
        {
            var card = El("div", "qkg-card qkg-stat");
            if (iconText is object)
            {
                var tile = El("span", "qkg-stat-tile", iconText);
                if (tint is object) tile.style.background = tint;
                if (ink is object) tile.style.color = ink;
                card.appendChild(tile);
            }
            card.appendChild(El("span", "qkg-stat-value qkg-num", value));
            card.appendChild(El("span", "qkg-note", label));
            if (open is object) OnClick(card, open);
            return Raw(card);
        }

        /// <summary>A small rounded label: Meccan, Medinan, a count.</summary>
        internal static HTMLElement Badge(string text, string kind = "plain") => El("span", "qkg-badge qkg-badge-" + kind, text);

        internal static HTMLElement PlaceBadge(string place) => Badge(place, place == Api.QK.MEDINAN ? "medinan" : "meccan");

        /// <summary>A pill: a filter, a way in, a word. <paramref name="on"/> marks it chosen.</summary>
        internal static HTMLElement Chip(string text, Action onClick = null, bool on = false, string extraClass = null)
        {
            var chip = El("button", "qkg-chip" + (on ? " qkg-on" : "") + (extraClass is null ? "" : " " + extraClass), text);
            chip.setAttribute("type", "button");
            if (onClick is object) chip.onclick = e => { e.stopPropagation(); onClick(); };
            return chip;
        }

        /// <summary>A pill button. Kinds: plain, primary, soft, ghost.</summary>
        internal static HTMLElement Button(string text, Action onClick, string kind = "plain", string glyph = null, string title = null)
        {
            var b = El("button", "qkg-btn qkg-btn-" + kind);
            b.setAttribute("type", "button");
            if (glyph is object) b.appendChild(El("span", "qkg-glyph", glyph));
            if (!string.IsNullOrEmpty(text)) b.appendChild(El("span", "", text));
            if (title is object) { b.title = title; b.setAttribute("aria-label", title); }
            if (onClick is object) b.onclick = e => { e.stopPropagation(); onClick(); };
            return b;
        }

        /// <summary>A round icon button; <paramref name="label"/> is its accessible name and tooltip.</summary>
        internal static HTMLElement IconButton(string glyph, string label, Action onClick)
        {
            var b = El("button", "qkg-iconbtn", glyph);
            b.setAttribute("type", "button");
            b.setAttribute("aria-label", label);
            b.title = label;
            if (onClick is object) b.onclick = e => { e.stopPropagation(); onClick(); };
            return b;
        }

        /// <summary>A segmented switch: one option lit; picking another calls <paramref name="pick"/> with its index.</summary>
        internal static HTMLElement Segmented(string[] labels, int selected, Action<int> pick, string ariaLabel = null)
        {
            var seg = El("div", "qkg-seg");
            seg.setAttribute("role", "tablist");
            if (ariaLabel is object) seg.setAttribute("aria-label", ariaLabel);
            var buttons = new HTMLElement[labels.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var b = El("button", i == selected ? "qkg-on" : "", labels[i]);
                b.setAttribute("type", "button");
                b.setAttribute("role", "tab");
                b.setAttribute("aria-selected", i == selected ? "true" : "false");
                b.onclick = _ =>
                {
                    for (var j = 0; j < buttons.Length; j++)
                    {
                        if (j == index) buttons[j].classList.add("qkg-on"); else buttons[j].classList.remove("qkg-on");
                        buttons[j].setAttribute("aria-selected", j == index ? "true" : "false");
                    }
                    pick(index);
                };
                buttons[i] = b;
                seg.appendChild(b);
            }
            return seg;
        }

        /// <summary>A surah's number in the mushaf's octagon.</summary>
        internal static HTMLElement SurahNumber(int number, bool on = false, string size = null)
        {
            var el = El("span", "qkg-snum" + (on ? " qkg-on" : "") + (size is null ? "" : " qkg-snum-" + size));
            el.appendChild(El("span", "", number.ToString()));
            return el;
        }

        /// <summary>A thin bar, filled to <paramref name="share"/> (0–1).</summary>
        internal static HTMLElement Track(double share, string color = null, string label = null)
        {
            var track = El("span", "qkg-track");
            var fill  = El("i", "");
            fill.style.width = (100 * Math.Max(0, Math.Min(1, share))).ToString("0.##") + "%";
            if (color is object) fill.style.background = color;
            track.appendChild(fill);
            if (label is object) { track.setAttribute("role", "img"); track.setAttribute("aria-label", label); }
            return track;
        }

        /// <summary>A bar of parts side by side, each as wide as its value.</summary>
        internal static HTMLElement Parts(string label, params (double Value, string Color)[] parts)
        {
            var bar = El("span", "qkg-parts");
            bar.setAttribute("role", "img");
            bar.setAttribute("aria-label", label);
            foreach (var p in parts)
            {
                var part = El("i", "");
                part.style.flex       = Math.Max(0, p.Value).ToString("0.###") + " 1 0";
                part.style.background = p.Color;
                bar.appendChild(part);
            }
            return bar;
        }

        /// <summary>A legend entry: a dot and its label.</summary>
        internal static HTMLElement Key(string color, string label, string value = null)
        {
            var el  = El("span", "qkg-key");
            var dot = El("span", "qkg-dot");
            dot.style.background = color;
            el.appendChild(dot);
            el.appendChild(El("span", "", label));
            if (value is object) el.appendChild(El("strong", "qkg-num", value));
            return el;
        }

        // -----------------------------------------------------------------------------------

        private const string CSS = @"
:root {
  --qkg-bg: #ffffff; --qkg-surface: #f6f7f6; --qkg-surface-2: #eef1f0; --qkg-raised: #ffffff;
  --qkg-line: #e4e8e6; --qkg-line-2: #d3d9d6;
  --qkg-ink: #1b2422; --qkg-ink-2: #44514d; --qkg-ink-3: #66726e;
  --qkg-teal: #157a7c; --qkg-teal-strong: #0f6466; --qkg-teal-tint: #e4f2f1; --qkg-teal-tint-2: #cfe7e5; --qkg-on-teal: #ffffff;
  --qkg-gold: #a9802f; --qkg-gold-tint: #f6eedb; --qkg-paper: #fcf7ea; --qkg-paper-ink: #2a2214; --qkg-paper-band: #f4ead0;
  --qkg-meccan: #a8561a; --qkg-meccan-tint: #fbeee3; --qkg-medinan: #2c5aa0; --qkg-medinan-tint: #e7eef9;
  --qkg-root: #8a6410; --qkg-root-tint: #f6efdc; --qkg-lemma: #7339a8; --qkg-lemma-tint: #efe6f7;
  --qkg-mark: #b0461b; --qkg-shadow: rgba(20, 30, 28, .08);
}
body.tss-dark-mode {
  --qkg-bg: #121615; --qkg-surface: #1a1f1e; --qkg-surface-2: #232928; --qkg-raised: #1a1f1e;
  --qkg-line: #2b3331; --qkg-line-2: #3a4442;
  --qkg-ink: #e7eceb; --qkg-ink-2: #b9c3c0; --qkg-ink-3: #95a19d;
  --qkg-teal: #5cc2c2; --qkg-teal-strong: #7fd4d3; --qkg-teal-tint: #173433; --qkg-teal-tint-2: #1f4443; --qkg-on-teal: #0b1414;
  --qkg-gold: #d2ad5f; --qkg-gold-tint: #2c2617; --qkg-paper: #1d1a14; --qkg-paper-ink: #efe6d2; --qkg-paper-band: #2a2417;
  --qkg-meccan: #e39a5c; --qkg-meccan-tint: #33241a; --qkg-medinan: #86aee8; --qkg-medinan-tint: #1c2636;
  --qkg-root: #d7b25a; --qkg-root-tint: #2b2516; --qkg-lemma: #b892e3; --qkg-lemma-tint: #271d33;
  --qkg-mark: #ef8f62; --qkg-shadow: rgba(0, 0, 0, .3);
}
.qkg-ui { background: var(--qkg-bg); color: var(--qkg-ink); }
.qkg-column { margin: 0 auto; padding: 32px 40px 72px 40px; gap: 28px; box-sizing: border-box; }
@media (max-width: 700px) { .qkg-column { padding: 20px 16px 48px 16px; } }
.qkg-ui h1, .qkg-ui h2, .qkg-ui h3, .qkg-ui p { margin: 0; }
.qkg-h1 { font-size: 32px; line-height: 1.2; font-weight: 600; letter-spacing: -.01em; color: var(--qkg-ink); }
.qkg-h2 { font-size: 20px; line-height: 1.3; font-weight: 600; color: var(--qkg-ink); }
.qkg-h3 { font-size: 16px; line-height: 1.3; font-weight: 600; color: var(--qkg-ink); }
.qkg-lead { font-size: 16px; line-height: 1.6; color: var(--qkg-ink-2); max-width: 780px; }
.qkg-note { font-size: 13px; line-height: 1.5; color: var(--qkg-ink-3); }
.qkg-kicker { font-size: 12px; font-weight: 600; letter-spacing: .06em; text-transform: uppercase; color: var(--qkg-ink-3); }
.qkg-num { font-variant-numeric: normal; }
.qkg-arw { font-family: {FONT}; unicode-bidi: isolate; line-height: 1.5; }
.qkg-click { cursor: pointer; }
.qkg-click:focus-visible, .qkg-chip:focus-visible, .qkg-btn:focus-visible, .qkg-iconbtn:focus-visible, .qkg-seg button:focus-visible { outline: 2px solid var(--qkg-teal); outline-offset: 2px; }
.qkg-link { color: var(--qkg-teal); cursor: pointer; }
.qkg-link:hover { color: var(--qkg-teal-strong); text-decoration: underline; }

.qkg-card { background: var(--qkg-raised); border: 1px solid var(--qkg-line); border-radius: 16px; padding: 20px 22px; box-sizing: border-box; color: var(--qkg-ink); }
.qkg-card.qkg-click:hover { border-color: var(--qkg-line-2); box-shadow: 0 2px 10px var(--qkg-shadow); }
.qkg-card.qkg-lit { border-color: var(--qkg-teal); background: var(--qkg-teal-tint); }
.qkg-panel { background: var(--qkg-surface); border-radius: 16px; padding: 20px 22px; box-sizing: border-box; }
.qkg-grid { display: grid; gap: 14px; align-items: start; width: 100%; }

.qkg-stat { display: flex; flex-direction: column; gap: 2px; padding: 16px 18px; }
.qkg-stat-tile { display: inline-flex; align-items: center; justify-content: center; width: 34px; height: 34px; border-radius: 10px; margin-bottom: 8px; background: var(--qkg-teal-tint); color: var(--qkg-teal-strong); font-size: 15px; font-weight: 600; }
.qkg-stat-value { font-size: 28px; line-height: 1.15; font-weight: 600; letter-spacing: -.02em; }

.qkg-btn { display: inline-flex; align-items: center; justify-content: center; gap: 8px; min-height: 40px; padding: 0 16px; border-radius: 999px; border: 1px solid var(--qkg-line-2); background: var(--qkg-raised); color: var(--qkg-ink); font: inherit; font-size: 14px; font-weight: 500; cursor: pointer; white-space: nowrap; }
.qkg-btn:hover { border-color: var(--qkg-ink-3); }
.qkg-btn-primary { background: var(--qkg-teal); border-color: var(--qkg-teal); color: var(--qkg-on-teal); }
.qkg-btn-primary:hover { background: var(--qkg-teal-strong); border-color: var(--qkg-teal-strong); }
.qkg-btn-soft { background: var(--qkg-teal-tint); border-color: transparent; color: var(--qkg-teal-strong); }
.qkg-btn-soft:hover { border-color: var(--qkg-teal-tint-2); }
.qkg-btn-ghost { background: transparent; border-color: transparent; color: var(--qkg-ink-2); }
.qkg-btn-ghost:hover { background: var(--qkg-surface-2); border-color: transparent; }
.qkg-btn:disabled { opacity: .45; cursor: default; }
.qkg-glyph { font-size: 13px; line-height: 1; }
.qkg-iconbtn { display: inline-flex; align-items: center; justify-content: center; width: 36px; height: 36px; flex: none; border-radius: 999px; border: 0; background: transparent; color: var(--qkg-ink-2); font-size: 15px; cursor: pointer; }
.qkg-iconbtn:hover { background: var(--qkg-surface-2); color: var(--qkg-ink); }
.qkg-iconbtn:disabled { opacity: .35; cursor: default; }
.qkg-iconbtn.qkg-on { background: var(--qkg-teal); color: var(--qkg-on-teal); }

.qkg-chip { display: inline-flex; align-items: center; gap: 6px; min-height: 30px; padding: 0 12px; border-radius: 999px; border: 1px solid transparent; background: var(--qkg-surface-2); color: var(--qkg-ink-2); font: inherit; font-size: 13px; font-weight: 500; white-space: nowrap; cursor: pointer; }
.qkg-chip:hover { color: var(--qkg-ink); border-color: var(--qkg-line-2); }
.qkg-chip.qkg-on { background: var(--qkg-teal-tint); color: var(--qkg-teal-strong); border-color: var(--qkg-teal-tint-2); }
.qkg-chip-out { background: transparent; border-color: var(--qkg-line-2); }
.qkg-chip .qkg-arw { font-size: 17px; line-height: 1.2; }
.qkg-badge { display: inline-flex; align-items: center; gap: 5px; height: 22px; padding: 0 9px; border-radius: 999px; font-size: 12px; font-weight: 600; white-space: nowrap; }
.qkg-badge-plain { background: var(--qkg-surface-2); color: var(--qkg-ink-2); }
.qkg-badge-teal { background: var(--qkg-teal-tint); color: var(--qkg-teal-strong); }
.qkg-badge-gold { background: var(--qkg-gold-tint); color: var(--qkg-gold); }
.qkg-badge-meccan { background: var(--qkg-meccan-tint); color: var(--qkg-meccan); }
.qkg-badge-medinan { background: var(--qkg-medinan-tint); color: var(--qkg-medinan); }
.qkg-badge-warn { background: #fdf0d5; color: #8a5a00; }
.qkg-badge-info { background: var(--qkg-medinan-tint); color: var(--qkg-medinan); }
.qkg-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; flex: none; }
.qkg-key { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; color: var(--qkg-ink-2); }
.qkg-key strong { color: var(--qkg-ink); font-weight: 600; }
.qkg-keys { display: flex; flex-wrap: wrap; gap: 8px 18px; }

.qkg-seg { display: inline-flex; flex-wrap: wrap; padding: 4px; gap: 2px; border-radius: 999px; background: var(--qkg-surface-2); }
.qkg-seg button { display: inline-flex; align-items: center; gap: 6px; min-height: 32px; padding: 0 14px; border-radius: 999px; border: 0; background: transparent; color: var(--qkg-ink-2); font: inherit; font-size: 14px; font-weight: 500; cursor: pointer; white-space: nowrap; }
.qkg-seg button:hover { color: var(--qkg-ink); }
.qkg-seg button.qkg-on { background: var(--qkg-raised); color: var(--qkg-ink); box-shadow: 0 1px 3px rgba(20, 30, 28, .14); }

.qkg-field { display: flex; align-items: center; gap: 10px; height: 46px; padding: 0 18px; border-radius: 999px; border: 1px solid var(--qkg-line-2); background: var(--qkg-raised); color: var(--qkg-ink-3); box-sizing: border-box; }
.qkg-field input { flex: 1 1 auto; min-width: 0; border: 0; outline: 0; background: transparent; font: inherit; font-size: 15px; color: var(--qkg-ink); }
.qkg-field input::placeholder { color: var(--qkg-ink-3); }
.qkg-field:focus-within { border-color: var(--qkg-teal); box-shadow: 0 0 0 3px var(--qkg-teal-tint); }

.qkg-snum { position: relative; display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px; flex: none; font-size: 14px; font-weight: 600; color: var(--qkg-ink); }
.qkg-snum::before { content: ''; position: absolute; inset: 7px; border-radius: 7px; background: var(--qkg-surface-2); border: 1px solid var(--qkg-line-2); transform: rotate(45deg); transition: background-color .15s; }
.qkg-snum > span { position: relative; }
.qkg-snum.qkg-on::before, .qkg-click:hover > .qkg-snum::before { background: var(--qkg-teal); border-color: var(--qkg-teal); }
.qkg-snum.qkg-on > span, .qkg-click:hover > .qkg-snum > span { color: var(--qkg-on-teal); }
.qkg-snum-sm { width: 32px; height: 32px; font-size: 12px; }
.qkg-snum-lg { width: 56px; height: 56px; font-size: 18px; }

.qkg-track { position: relative; display: block; height: 8px; border-radius: 999px; background: var(--qkg-surface-2); overflow: hidden; }
.qkg-track > i { position: absolute; inset: 0 auto 0 0; border-radius: 999px; background: var(--qkg-teal); }
.qkg-parts { display: flex; gap: 2px; height: 10px; border-radius: 999px; overflow: hidden; }
.qkg-parts > i { display: block; min-width: 2px; }

.qkg-bars { display: flex; flex-direction: column; gap: 6px; }
.qkg-bar-row { display: grid; grid-template-columns: var(--qkg-label, 72px) minmax(0, 1fr) 48px; gap: 10px; align-items: center; color: var(--qkg-ink); }
.qkg-bar-label { font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.qkg-bar-label.qkg-arw { font-size: 18px; text-align: right; }
.qkg-bar-value { font-size: 13px; text-align: right; color: var(--qkg-ink-2); }
.qkg-bar-row .qkg-track { height: 10px; }
.qkg-bar-row.qkg-click:hover .qkg-bar-label { color: var(--qkg-teal-strong); }

.qkg-columns-chart { display: grid; grid-template-columns: 32px minmax(0, 1fr); gap: 8px; }
.qkg-yaxis { display: flex; flex-direction: column; justify-content: space-between; font-size: 11px; color: var(--qkg-ink-3); text-align: right; }
.qkg-cols { position: relative; display: flex; align-items: flex-end; gap: 2px; border-bottom: 1px solid var(--qkg-line-2); }
.qkg-cols > i { flex: 1 1 0; min-width: 1px; border-radius: 2px 2px 0 0; display: block; }
.qkg-cols > i.qkg-click:hover { opacity: .75; }
.qkg-cols-grid { position: absolute; inset: 0; pointer-events: none; display: flex; flex-direction: column; justify-content: space-between; }
.qkg-cols-grid > span { border-top: 1px solid var(--qkg-line); }
.qkg-node-body { padding: 24px 28px 48px 28px; box-sizing: border-box; background: var(--qkg-bg); color: var(--qkg-ink); min-height: 100%; }
.qkg-node-ar { font-family: {FONT}; font-size: 34px; line-height: 2.1; text-align: right; color: var(--qkg-ink); margin: 0; }
.qkg-node-translit { font-style: italic; font-size: 14px; color: var(--qkg-ink-3); margin: 0; }
.qkg-xaxis { display: flex; justify-content: space-between; font-size: 11px; color: var(--qkg-ink-3); }
";
    }
}
