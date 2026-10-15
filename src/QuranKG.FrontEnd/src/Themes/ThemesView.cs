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
            Look.EnsureStyles();
            EnsureStyles();
            _content = Look.Page(Defer(async () =>
                {
                    var mapTask = QuranApi.ClustersMapAsync();
                    var result  = await QuranApi.ThemesAsync();
                    var page    = VStack().WS().Gap(24.px());

                    if (!result.Built)
                        return page.Children(
                            Look.Title("Stories and themes".t()),
                            Banner("The themes have not been built yet. An administrator builds them with `just clusters` (the qkg/clusters/build endpoint); it takes a few seconds.".t()).Warning());

                    var themes = result.Themes ?? new QK.ThemeRow[0];

                    var lead = Look.El("p", "qkg-lead");
                    AppendLead(lead, string.Format("Every surah is cut into passages where its vocabulary changes — {0} in all. Passages of different surahs that lean on the same distinctive words form a theme: {1} of them, the stories the Quran tells more than once and the subjects it returns to. {2} of the {3} verses fall in one.".t(),
                                                   "§" + Panels.Number(result.Passages) + "§", "§" + themes.Length + "§", "§" + Panels.Number(result.VersesInThemes) + "§", Panels.Number(result.Verses)));

                    page.Add(VStack().WS().Gap(10.px()).Children(
                        Raw(Look.El("h1", "qkg-h1", "Stories and themes".t())),
                        Raw(lead),
                        Raw(Look.El("p", "qkg-note", result.Method + " " + "Built".t() + " " + result.RunId + "."))));

                    var map    = await mapTask;
                    var host   = VStack().WS().Gap(24.px());
                    var groups = (map.Groups ?? new QK.MapGroup[0]).Length > 0;

                    void Show(ClusterMap.Colouring colouring)
                    {
                        host.Clear();
                        host.Add(Grouping(map, colouring, groups));
                    }

                    Show(ClusterMap.Colouring.Themes);
                    page.Add(host);
                    return page;
                }, Raw(Look.El("p", "qkg-note", "Grouping the passages…".t()))));
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>The lead sentence, its numbers (between §) in the ink colour.</summary>
        private static void AppendLead(HTMLElement into, string text)
        {
            var parts = text.Split('§');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1) into.appendChild(Look.El("strong", "qkg-lead-num", parts[i]));
                else into.appendChild(document.createTextNode(parts[i]));
            }
        }

        /// <summary>One grouping: the switch and the filter, its map, then its groups widest first; the filter also lights the map.</summary>
        private static IComponent Grouping(QK.ClusterMapDto data, ClusterMap.Colouring colouring, bool hasGroups)
        {
            var isThemes = colouring == ClusterMap.Colouring.Themes;
            var groups   = ((isThemes ? data.Themes : data.Groups) ?? new QK.MapGroup[0])
                              .OrderByDescending(g => (g.SurahNumbers ?? new int[0]).Length).ThenByDescending(g => g.Passages).ThenBy(g => g.Ordinal).ToArray();
            var surahs   = (data.Surahs ?? new QK.MapSurah[0]).ToDictionary(x => x.Number);
            var map      = new ClusterMap(data, colouring);
            var section  = VStack().WS().Gap(24.px());

            // The cards are built once; the filter shows and hides them in place.
            var cards = groups.Select(g => GroupCard(g, surahs, map.ColorOf(g.Ordinal))).ToArray();

            var field = Look.El("label", "qkg-field qkg-themes-filter");
            field.appendChild(Look.El("span", "", "⌕"));
            var input = document.createElement("input").As<HTMLInputElement>();
            input.type = "search";
            input.placeholder = "Filter: Moses, موسى, a surah…".t();
            input.setAttribute("aria-label", "Filter the themes".t());
            field.appendChild(input);
            var count = Look.El("span", "qkg-note");

            void Filter()
            {
                var q       = (input.value ?? "").Trim();
                var matched = new HashSet<int>();
                var shown   = 0;
                for (var i = 0; i < groups.Length; i++)
                {
                    var g     = groups[i];
                    var match = q.Length == 0
                             || (g.Label ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                             || (g.LabelArabic ?? "").Contains(q)
                             || (g.SurahNumbers ?? new int[0]).Any(n => surahs.TryGetValue(n, out var s) && ((s.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (s.NameArabic ?? "").Contains(q)));
                    cards[i].style.display = match ? "" : "none";
                    if (match) { matched.Add(g.Ordinal); shown++; }
                }
                count.textContent = q.Length == 0 ? "" : string.Format("{0} of {1}".t(), shown, groups.Length);
                map.Filter(q.Length == 0 ? null : matched);
            }
            input.oninput = _ => Filter();

            var controls = Look.El("div", "qkg-themes-controls");
            if (hasGroups)
                controls.appendChild(Look.Segmented(new[] { "Themes · shared distinctive words".t(), "Similarity groups · meaning, lemmas, roots".t() },
                    isThemes ? 0 : 1,
                    i =>
                    {
                        var host = section.Render().parentElement;
                        var next = Grouping(data, i == 0 ? ClusterMap.Colouring.Themes : ClusterMap.Colouring.Groups, hasGroups);
                        if (host is object) { host.innerHTML = ""; host.appendChild(next.Render()); }
                    }, "Group the passages by".t()));
            controls.appendChild(Look.El("span", "qkg-toolbar-spacer"));
            controls.appendChild(count);
            controls.appendChild(field);
            section.Add(Raw(controls).WS());

            if (!isThemes)
                section.Add(Raw(Look.El("p", "qkg-note", data.GroupsMethod + " " + "Built".t() + " " + data.GroupsRunId + ". " +
                                      "Scored against Ibn Kathir's related verses (QurSim) at the same share of verses grouped, the two groupings come out alike — see tools/cluster-benchmark.".t())));

            section.Add(Look.Card(null,
                Look.Heading("The Quran in passages".t(), "Each row a surah, each block a passage as wide as its words.".t()),
                map));

            var grid = Look.El("div", "qkg-theme-grid");
            foreach (var card in cards) grid.appendChild(card);
            section.Add(VStack().WS().Gap(14.px()).Children(
                Look.Heading(isThemes ? string.Format("All {0} themes".t(), groups.Length) : string.Format("All {0} similarity groups".t(), groups.Length),
                             "Widest first. The filter lights the ones it matches on the map.".t()),
                Raw(grid).WS()));
            return section;
        }

        /// <summary>A theme or a similarity group: its labels, how far it reaches, and the surahs it spans.</summary>
        private static HTMLElement GroupCard(QK.MapGroup g, Dictionary<int, QK.MapSurah> surahs, string color)
        {
            var names = (g.SurahNumbers ?? new int[0]).Where(surahs.ContainsKey).Select(n => (Number: n, Surah: surahs[n])).ToArray();
            return Card(color, g.LabelArabic, g.Kind == QK.THEME ? g.Label : "", names.Length, g.Passages, g.Verses,
                        names.Select(x => (x.Number + ". " + x.Surah.Name, x.Surah.Uid)).ToArray(), () => Lists.Open(g.Uid));
        }

        /// <summary>The card of a theme: a dot of its colour, its Arabic words, how far it reaches, its English words, the surahs that tell it.</summary>
        private static HTMLElement Card(string color, string labelArabic, string label, int surahCount, int passages, int verses, (string Name, UID128 Uid)[] surahs, Action open)
        {
            var card = Look.El("div", "qkg-card qkg-theme-card");
            var head = Look.El("div", "qkg-theme-head");
            var dot  = Look.El("span", "qkg-dot qkg-theme-dot");
            dot.style.background = color;
            head.appendChild(dot);
            head.appendChild(Look.Ar(labelArabic, "qkg-arw qkg-theme-ar"));
            head.appendChild(Look.Badge(string.Format("{0} surahs".t(), surahCount), "teal"));
            card.appendChild(head);
            if (!string.IsNullOrEmpty(label)) card.appendChild(Look.El("span", "qkg-theme-en", label));
            card.appendChild(Look.El("span", "qkg-note", string.Format("{0} passages · {1} verses".t(), passages, verses)));

            var chips = Look.El("div", "qkg-theme-surahs");
            foreach (var (name, uid) in surahs)
            {
                var captured = uid;
                var chip = Look.Chip(name, () => Lists.Open(captured));
                chip.classList.add("qkg-theme-surah");
                chips.appendChild(chip);
            }
            card.appendChild(chips);
            return Look.OnClick(card, open);
        }

        /// <summary>The widest few themes and a way to the rest — the Patterns page's section.</summary>
        internal static IComponent Preview(int count)
        {
            EnsureStyles();
            return Defer(async () =>
            {
                var result = await QuranApi.ThemesAsync();
                if (!result.Built) return Raw(Look.El("p", "qkg-note", "The themes have not been built yet (`just clusters`).".t()));

                var grid = Look.El("div", "qkg-theme-grid");
                foreach (var t in (result.Themes ?? new QK.ThemeRow[0]).Take(count))
                {
                    var surahs = new List<(string, UID128)>();
                    for (var i = 0; i < (t.SurahUIDs ?? new UID128[0]).Length; i++) surahs.Add((t.SurahNumbers[i] + ". " + t.SurahNames[i], t.SurahUIDs[i]));
                    var uid = t.Uid;
                    grid.appendChild(Card(Clusters.ColorOf(t.Ordinal), t.LabelArabic, t.Label, (t.SurahNumbers ?? new int[0]).Length, t.Passages, t.Verses, surahs.ToArray(), () => Lists.Open(uid)));
                }
                return Raw(grid).WS();
            }, Raw(Look.El("p", "qkg-note", "Grouping the passages…".t())));
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-lead-num { color: var(--qkg-ink); font-weight: 600; }
.qkg-themes-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.qkg-themes-filter { width: 340px; max-width: 100%; height: 44px; }
.qkg-theme-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(420px, 100%), 1fr)); gap: 14px; align-items: start; }
.qkg-theme-card { display: flex; flex-direction: column; gap: 8px; }
.qkg-theme-head { display: flex; align-items: center; gap: 12px; }
.qkg-theme-dot { width: 10px; height: 10px; }
.qkg-theme-ar { flex: 1 1 auto; font-size: 26px; line-height: 1.5; text-align: left; }
.qkg-theme-en { font-weight: 600; }
.qkg-theme-surahs { display: flex; flex-wrap: wrap; gap: 6px; padding-top: 2px; }
.qkg-theme-surah { min-height: 26px; padding: 0 10px; font-size: 12px; }
.qkg-map-info { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 14px; min-height: 48px; padding: 10px 14px; border-radius: 12px; background: var(--qkg-teal-tint); box-sizing: border-box; }
.qkg-map-info-hint { background: var(--qkg-surface); color: var(--qkg-ink-3); font-size: 13px; }
.qkg-map-info-ar { font-size: 22px; color: var(--qkg-teal-strong); }
";
            document.head.appendChild(style);
        }
    }
}
