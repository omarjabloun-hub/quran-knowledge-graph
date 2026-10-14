using System.Linq;
using QuranKG.FrontEnd.Api;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;

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
        internal static string ThemeColor => Theme.Colors.Green700;
        internal static string Unthemed   => Theme.Colors.Neutral300;

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
            var header = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(
                TextBlock((showSurah ? p.SurahNumber + ". " + p.SurahName + " · " : "") + Range(p)).SemiBold(),
                TextBlock(string.Format("{0} verses · {1} words".t(), p.Verses, Panels.Number(p.Words))).Small().Secondary(),
                Arabic.Inline(p.LabelArabic, 17).Foreground(Palette.Lemma));

            var body = VStack().WS().Children(
                header,
                Arabic.Text(p.OpeningText + " …", 22).PT(6),
                TextBlock(p.OpeningTranslation ?? "").Small().Secondary());

            if (showTheme && p.ThemeUid != default(UID128))
                body.Add(HStack().WS().AlignItemsCenter().Gap(8.px()).PT(6).Children(
                    TextBlock("Theme shared with other surahs:".t()).Tiny().Secondary(),
                    ThemeLink(p.ThemeUid, p.ThemeLabelArabic, p.ThemeLabel, p.ThemeSurahs)));

            return Card(body).OnClick(() => Lists.Open(p.Uid)).MB(8);
        }

        /// <summary>
        /// A theme named inline — its Arabic label, then how many surahs it spans when that is
        /// known. Opens the theme without opening the card it sits on.
        /// </summary>
        internal static IComponent ThemeLink(UID128 theme, string labelArabic, string label, int surahs)
        {
            // A Stack takes no click, so each text carries it; stopPropagation keeps the card it
            // sits on from opening too.
            TextBlock Clickable(TextBlock text)
                => text.Foreground(ThemeColor)
                       .OnClick((_, e) => { e.stopPropagation(); Lists.Open(theme); })
                       .Style(s => s.setProperty("cursor", "pointer"));

            var link = HStack().AlignItemsCenter().Gap(6.px()).Children(
                Icon(UIcons.CircleNodes).Foreground(ThemeColor),
                Clickable(Arabic.Inline(labelArabic, 16)));

            if (surahs > 0) link.Add(Clickable(TextBlock(string.Format("{0} surahs".t(), surahs)).Tiny()));
            return link.Tooltip(label);
        }

        /// <summary>
        /// A surah at a glance: one segment per passage, as wide as its words, green where the
        /// passage shares a theme with other surahs. Each segment opens its passage.
        /// </summary>
        internal static IComponent Strip(QK.PassageRow[] passages)
        {
            var strip = HStack().WS().Gap(2.px()).H(22.px());
            foreach (var p in passages)
            {
                var captured = p;
                strip.Add(TextBlock("").H(22.px())
                             .Background(captured.ThemeUid != default(UID128) ? ThemeColor : Unthemed)
                             .Style(s =>
                              {
                                  s.setProperty("flex", captured.Words + " 1 0");
                                  s.setProperty("border-radius", "3px");
                                  s.setProperty("cursor", "pointer");
                              })
                             .OnClick((_, __) => Lists.Open(captured.Uid))
                             .Tooltip(Range(captured) + " · " + captured.Label));
            }
            return strip;
        }
    }
}
