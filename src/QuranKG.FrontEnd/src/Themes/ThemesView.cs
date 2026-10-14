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
    /// The stories and subjects the Quran returns to. First the map of the passages — every surah in
    /// its passages, coloured by group (<see cref="ClusterMap"/>) — then every group, the widest
    /// first, each with the surahs it spans. Two groupings, switched at the top: the THEMES of
    /// qkg/clusters/build (shared distinctive words) and the SIMILARITY GROUPS of
    /// qkg/clusters/similar-build (Arabic meaning + lemmas + roots). The filter lights the groups it
    /// matches on the map. A card opens the group (read it there, passage after passage).
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
                    var mapTask = QuranApi.ClustersMapAsync();
                    var result  = await QuranApi.ThemesAsync();
                    var page    = VStack().WS().P(24).Gap(12.px());

                    page.Add(TextBlock("Stories and themes".t()).XLarge().Bold());

                    if (!result.Built)
                        return page.Children(Banner("The themes have not been built yet. An administrator builds them with `just clusters` (the qkg/clusters/build endpoint); it takes a few seconds.".t()).Warning());

                    var themes = result.Themes ?? new QK.ThemeRow[0];

                    page.Add(TextBlock(string.Format("Every surah was cut into passages where its vocabulary changes — {0} passages in all — and passages of different surahs that lean on the same distinctive words were grouped. {1} groups reach across surahs: the stories the Quran tells more than once and the subjects it returns to. {2} of the {3} verses fall in one.".t(),
                                                     Panels.Number(result.Passages), themes.Length, Panels.Number(result.VersesInThemes), Panels.Number(result.Verses))).Small());
                    page.Add(TextBlock(result.Method + " " + "Built".t() + " " + result.RunId + ".").Tiny().Secondary());

                    // Two groupings of the same passages: the themes (this page's own, shared distinctive
                    // words) and the similarity groups of qkg/clusters/similar-build (Arabic meaning +
                    // lemmas + roots). The switch redraws the map and the list for the one chosen.
                    var map    = await mapTask;
                    var host   = VStack().WS().Gap(12.px());
                    var groups = (map.Groups ?? new QK.MapGroup[0]).Length > 0;

                    void Show(ClusterMap.Colouring colouring)
                    {
                        host.Clear();
                        host.Add(Grouping(map, colouring));
                    }

                    if (groups)
                    {
                        var byThemes = Button("Themes — shared distinctive words".t()).Primary();
                        var byGroups = Button("Similarity groups — meaning, lemmas and roots".t());
                        byThemes.OnClick(() => { byThemes.IsPrimary = true; byGroups.IsPrimary = false; Show(ClusterMap.Colouring.Themes); });
                        byGroups.OnClick(() => { byGroups.IsPrimary = true; byThemes.IsPrimary = false; Show(ClusterMap.Colouring.Groups); });
                        page.Add(HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().PT(8).Children(
                            TextBlock("Group the passages by".t()).SemiBold().PR(4), byThemes, byGroups));
                    }

                    Show(ClusterMap.Colouring.Themes);
                    page.Add(host);
                    return page;
                }, TextBlock("Grouping the passages…".t()).Secondary().P(24)).WS());
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>One grouping: its map, then its groups widest first, with a filter that also lights the map.</summary>
        private static IComponent Grouping(QK.ClusterMapDto data, ClusterMap.Colouring colouring)
        {
            var isThemes = colouring == ClusterMap.Colouring.Themes;
            var groups   = ((isThemes ? data.Themes : data.Groups) ?? new QK.MapGroup[0])
                              .OrderByDescending(g => (g.SurahNumbers ?? new int[0]).Length).ThenByDescending(g => g.Passages).ThenBy(g => g.Ordinal).ToArray();
            var surahs   = (data.Surahs ?? new QK.MapSurah[0]).ToDictionary(x => x.Number);
            var map      = new ClusterMap(data, colouring);
            var section  = VStack().WS().Gap(12.px());

            if (!isThemes)
                section.Add(TextBlock(data.GroupsMethod + " " + "Built".t() + " " + data.GroupsRunId + ". " +
                                      "Scored against Ibn Kathir's related verses (QurSim) at the same share of verses grouped, the two groupings come out alike — see tools/cluster-benchmark.".t()).Tiny().Secondary());

            section.Add(Panels.Panel("The Quran in passages".t(), null, map));
            section.Add(Panels.Heading(isThemes ? string.Format("All {0} themes".t(), groups.Length) : string.Format("All {0} similarity groups".t(), groups.Length),
                                       "Widest first. The filter lights the ones it matches on the map.".t()));

            // The cards are built once; the filter shows and collapses them in place.
            var cards  = groups.Select(g => GroupCard(g, surahs, map.ColorOf(g.Ordinal))).ToArray();
            var search = SearchBox("Filter: Moses, موسى, a surah…".t()).SearchAsYouType();
            search.OnSearch((_, query) =>
            {
                var q       = (query ?? "").Trim();
                var matched = new HashSet<int>();
                for (var i = 0; i < groups.Length; i++)
                {
                    var g     = groups[i];
                    var match = q.Length == 0
                             || (g.Label ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                             || (g.LabelArabic ?? "").Contains(q)
                             || (g.SurahNumbers ?? new int[0]).Any(n => surahs.TryGetValue(n, out var s) && ((s.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (s.NameArabic ?? "").Contains(q)));
                    if (match) { cards[i].Show(); matched.Add(g.Ordinal); } else cards[i].Collapse();
                }
                map.Filter(q.Length == 0 ? null : matched);
            });

            section.Add(search.WS().MaxWidth(420.px()));
            foreach (var card in cards) section.Add(card);
            return section;
        }

        /// <summary>A theme or a similarity group: its labels, how far it reaches, and the surahs it spans.</summary>
        private static IComponent GroupCard(QK.MapGroup g, Dictionary<int, QK.MapSurah> surahs, string color)
        {
            var header = HStack().WS().AlignItemsCenter().Gap(12.px()).Wrap().Children(
                Raw(ClusterMap.Swatch(color)),
                Arabic.Inline(g.LabelArabic, 24),
                TextBlock(g.Kind == QK.THEME ? g.Label : "").SemiBold(),
                Badge(string.Format("{0} surahs".t(), (g.SurahNumbers ?? new int[0]).Length)).Success().Outline(),
                Badge(string.Format("{0} passages · {1} verses".t(), g.Passages, g.Verses)).Neutral());

            var row = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().PT(6);
            foreach (var n in g.SurahNumbers ?? new int[0])
            {
                if (!surahs.TryGetValue(n, out var s)) continue;
                var uid = s.Uid;
                row.Add(TextBlock(n + ". " + s.Name).Small()
                           .OnClick((_, e) => { e.stopPropagation(); Lists.Open(uid); })
                           .Style(st => st.setProperty("cursor", "pointer")));
            }

            return Card(VStack().WS().Children(header, row)).OnClick(() => Lists.Open(g.Uid)).MB(8);
        }

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
                Raw(ClusterMap.Swatch(Clusters.ColorOf(t.Ordinal))),
                Arabic.Inline(t.LabelArabic, 24),
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
