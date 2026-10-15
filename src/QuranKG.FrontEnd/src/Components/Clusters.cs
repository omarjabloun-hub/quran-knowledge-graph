using System;
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
    /// How passages and themes are drawn wherever they appear — a surah's Passages tab, a theme's
    /// page, the Themes page — so a passage looks the same in all three. A passage card opens the
    /// passage; the theme named on it opens the theme.
    /// </summary>
    internal static class Clusters
    {
        /// <summary>The Cluster colour of the workspace style (config/schemas/nodes/styles/Cluster.json).</summary>
        internal static string ThemeColor => Look.TEAL;
        internal static string Unthemed   => "#d3d9d6";

        /// <summary>
        /// A theme's own colour, the same on the map, in the reader and on the Themes page: hues a
        /// golden angle apart, so themes next to each other in the list never look alike.
        /// </summary>
        internal static string ColorOf(int themeOrdinal)
            => themeOrdinal <= 0 ? Unthemed : "hsl(" + (int)((themeOrdinal * 137.508) % 360) + ", 58%, 46%)";

        /// <summary>A similarity group's colour (qkg/clusters/similar-build): the same walk round the hues, softer, so it is never read as a theme's.</summary>
        internal static string GroupColorOf(int groupOrdinal)
            => groupOrdinal <= 0 ? Unthemed : "hsl(" + (int)((groupOrdinal * 137.508 + 69) % 360) + ", 42%, 52%)";

        /// <summary>"7:103–126", or "7:103" for a passage of one verse.</summary>
        internal static string Range(QK.PassageRow p)
            => p.FirstVerseKey == p.LastVerseKey ? p.FirstVerseKey : p.FirstVerseKey + "–" + p.LastVerseKey.Split(':').Last();

        /// <summary>A passage: where it is and how long, its distinctive words, its opening verse, and the theme it shares.</summary>
        internal static IComponent PassageCard(QK.PassageRow p, bool showSurah, bool showTheme = true)
        {
            Look.EnsureStyles();
            var card = Look.El("div", "qkg-card qkg-passage");
            var head = Look.El("div", "qkg-passage-head");
            head.appendChild(Look.El("span", "qkg-passage-range", (showSurah ? p.SurahNumber + ". " + p.SurahName + " · " : "") + Range(p)));
            head.appendChild(Look.El("span", "qkg-note", string.Format("{0} verses · {1} words".t(), p.Verses, Panels.Number(p.Words))));
            head.appendChild(Look.Ar(p.LabelArabic, "qkg-arw qkg-passage-label"));
            card.appendChild(head);

            var ar = Look.El("p", "qkg-passage-ar", (p.OpeningText ?? "") + " …");
            ar.setAttribute("dir", "rtl");
            ar.setAttribute("lang", "ar");
            card.appendChild(ar);
            if (!string.IsNullOrEmpty(p.OpeningTranslation)) card.appendChild(Look.El("p", "qkg-passage-en", p.OpeningTranslation));

            if (showTheme && UID128.IsNotNull(p.ThemeUid))
                card.appendChild(Look.El("div", "qkg-keys",
                    Look.El("span", "qkg-note", "Theme shared with other surahs:".t()),
                    ThemeChip(p.ThemeUid, p.ThemeLabelArabic, p.ThemeLabel, p.ThemeSurahs, 0)));

            var uid = p.Uid;
            Look.OnClick(card, () => Lists.Open(uid));
            EnsureStyles();
            return Raw(card).WS();
        }

        /// <summary>A theme as a chip — its colour, its Arabic words, how many surahs tell it. Opens the theme without opening the card it sits on.</summary>
        internal static HTMLElement ThemeChip(UID128 theme, string labelArabic, string label, int surahs, int ordinal)
        {
            var chip = Look.El("span", "qkg-chip qkg-chip-out");
            var dot  = Look.El("span", "qkg-dot");
            dot.style.background = ordinal > 0 ? ColorOf(ordinal) : ThemeColor;
            chip.appendChild(dot);
            chip.appendChild(Look.Ar(labelArabic));
            if (surahs > 0) chip.appendChild(Look.El("span", "qkg-note", string.Format("{0} surahs".t(), surahs)));
            chip.title = label ?? "";
            return Look.OnClick(chip, () => Lists.Open(theme));
        }

        /// <summary>A theme named inline, as a chip.</summary>
        internal static IComponent ThemeLink(UID128 theme, string labelArabic, string label, int surahs)
            => Raw(ThemeChip(theme, labelArabic, label, surahs, 0));

        /// <summary>
        /// A surah at a glance: one segment per passage, as wide as its words, in its theme's colour
        /// where the passage shares a theme with other surahs. Each segment opens its passage.
        /// </summary>
        internal static IComponent Strip(QK.PassageRow[] passages)
        {
            Look.EnsureStyles();
            var strip = Look.El("div", "qkg-passage-strip");
            strip.setAttribute("role", "img");
            strip.setAttribute("aria-label", string.Format("{0} passages; {1} share a theme with other surahs".t(), passages.Length, passages.Count(x => UID128.IsNotNull(x.ThemeUid))));
            foreach (var p in passages)
            {
                var captured = p;
                var seg = Look.El("i", "qkg-click");
                seg.style.flex       = Math.Max(1, p.Words) + " 1 0";
                seg.style.background = UID128.IsNotNull(p.ThemeUid) ? ThemeColor : Unthemed;
                seg.title            = Range(captured) + " · " + captured.Label;
                seg.onclick          = _ => Lists.Open(captured.Uid);
                strip.appendChild(seg);
            }
            EnsureStyles();
            return Raw(strip).WS();
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-passage { display: flex; flex-direction: column; gap: 8px; }
.qkg-passage-head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px 12px; }
.qkg-passage-range { font-weight: 600; }
.qkg-passage-label { font-size: 19px; color: var(--qkg-mark); }
.qkg-passage-ar { font-family: 'Amiri Quran', 'Amiri', serif; font-size: 23px; line-height: 2; text-align: right; color: var(--qkg-ink); margin: 0; }
.qkg-passage-en { font-size: 14px; line-height: 1.6; color: var(--qkg-ink-2); margin: 0; }
.qkg-passage-strip { display: flex; gap: 3px; height: 14px; }
.qkg-passage-strip > i { display: block; border-radius: 3px; min-width: 3px; }
.qkg-passage-strip > i:hover { outline: 2px solid var(--qkg-teal); outline-offset: 1px; }
";
            document.head.appendChild(style);
        }
    }
}
