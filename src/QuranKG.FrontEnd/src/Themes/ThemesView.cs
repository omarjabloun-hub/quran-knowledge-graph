using System;
using System.Collections.Generic;
using System.Linq;
using Mosaik;
using Mosaik.Helpers;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Themes
{
    /// <summary>
    /// The stories and subjects the Quran returns to: every theme the clusters build found
    /// (qkg/clusters/themes), the widest first, each with the surahs that tell it. The search box
    /// filters on the English or the Arabic label. A card opens the theme; a surah opens the surah.
    /// </summary>
    internal sealed class ThemesView : IComponent
    {
        internal const string Route = "#/themes";

        /// <summary>Whether the page on screen is this one — what lights its sidebar entry.</summary>
        internal static bool IsShowing() => (window.location.hash ?? "").StartsWith(Route, StringComparison.Ordinal);

        private readonly IComponent _content;

        public ThemesView(Parameters state)
        {
            _content = VStack().S().ScrollY().Children(
                Defer(async () =>
                {
                    var result = await QuranApi.ThemesAsync();
                    var page   = VStack().WS().P(24).Gap(12.px());

                    page.Add(TextBlock("Stories and themes".t()).XLarge().Bold());

                    if (!result.Built)
                        return page.Children(Banner("The themes have not been built yet. An administrator builds them with `just clusters` (the qkg/clusters/build endpoint); it takes a few seconds.".t()).Warning());

                    var themes = result.Themes ?? new QK.ThemeRow[0];

                    page.Add(TextBlock(string.Format("Every surah was cut into passages where its vocabulary changes — {0} passages in all — and passages of different surahs that lean on the same distinctive words were grouped. {1} groups reach across surahs: the stories the Quran tells more than once and the subjects it returns to. {2} of the {3} verses fall in one.".t(),
                                                     Panels.Number(result.Passages), themes.Length, Panels.Number(result.VersesInThemes), Panels.Number(result.Verses))).Small());
                    page.Add(TextBlock(result.Method + " " + "Built".t() + " " + result.RunId + ".").Tiny().Secondary());

                    // The cards are built once; the filter shows and collapses them in place.
                    var cards  = themes.Select(ThemeCard).ToArray();
                    var search = SearchBox("Filter: Moses, موسى, women…".t()).SearchAsYouType();
                    search.OnSearch((_, query) =>
                    {
                        var q = (query ?? "").Trim();
                        for (var i = 0; i < themes.Length; i++)
                        {
                            var t     = themes[i];
                            var match = q.Length == 0
                                     || (t.Label ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                     || (t.LabelArabic ?? "").Contains(q)
                                     || (t.SurahNames ?? new string[0]).Any(n => n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (match) cards[i].Show(); else cards[i].Collapse();
                        }
                    });

                    page.Add(search.WS().MaxWidth(420.px()));
                    foreach (var card in cards) page.Add(card);
                    return page;
                }, TextBlock("Grouping the passages…".t()).Secondary().P(24)).WS());
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>The widest few themes and a way to the rest — the home page's section.</summary>
        internal static IComponent Preview(int count)
        {
            return Defer(async () =>
            {
                var result = await QuranApi.ThemesAsync();
                if (!result.Built) return TextBlock("The themes have not been built yet (`just clusters`).".t()).Small().Secondary();

                var list = VStack().WS();
                foreach (var t in (result.Themes ?? new QK.ThemeRow[0]).Take(count)) list.Add(ThemeCard(t));
                list.Add(Button(string.Format("All {0} themes".t(), (result.Themes ?? new QK.ThemeRow[0]).Length)).SetIcon(UIcons.CircleNodes).OnClick(() => Router.Navigate(Route)));
                return list;
            }, TextBlock("Grouping the passages…".t()).Small().Secondary());
        }

        /// <summary>A theme: its Arabic and English labels, how far it reaches, and the surahs that tell it.</summary>
        private static IComponent ThemeCard(QK.ThemeRow t)
        {
            var header = HStack().WS().AlignItemsCenter().Gap(12.px()).Wrap().Children(
                Arabic.Inline(t.LabelArabic, 24).Foreground(Clusters.ThemeColor),
                TextBlock(t.Label).SemiBold(),
                Badge(string.Format("{0} surahs".t(), (t.SurahNumbers ?? new int[0]).Length)).Success().Outline(),
                Badge(string.Format("{0} passages · {1} verses".t(), t.Passages, t.Verses)).Neutral());

            var surahs = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().PT(6);
            for (var i = 0; i < (t.SurahUIDs ?? new UID128[0]).Length; i++)
            {
                var uid = t.SurahUIDs[i];
                surahs.Add(TextBlock(t.SurahNumbers[i] + ". " + t.SurahNames[i]).Small()
                              .OnClick((_, e) => { e.stopPropagation(); Lists.Open(uid); })
                              .Style(s => s.setProperty("cursor", "pointer")));
            }

            return Card(VStack().WS().Children(header, surahs)).OnClick(() => Lists.Open(t.Uid)).MB(8);
        }
    }
}
