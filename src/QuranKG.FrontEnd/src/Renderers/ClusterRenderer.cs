using System.Collections.Generic;
using System.Linq;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// A group of verses the clusters build found (qkg/clusters/build). A THEME — passages of
    /// several surahs that tell the same story or treat the same subject — opens on those
    /// passages, surah by surah. A PASSAGE — a run of consecutive verses of one surah — opens on
    /// its verses in order, with the theme it belongs to and the passages either side of it.
    /// </summary>
    public sealed class ClusterRenderer : QuranRendererBase
    {
        public ClusterRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Cluster.Type,
            DisplayName = "Cluster",
            Icon        = UIcons.CircleNodes.ToString(),
            LabelField  = LabelFields.FirstOf(N.Cluster.Label, N.Cluster.LabelArabic, N.Cluster.ClusterKey),
        }) { ShortDisplayName = "CLUS"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Surah.Type, N.Cluster.Type };

        // A theme and a passage are told apart in a mixed list by the tile alone.
        protected override string TileText(Node node) => node.GetString(N.Cluster.Kind) == QK.THEME ? "THEME" : "PASS";

        protected override IComponent CardBody(Node node) => Arabic.Inline(node.GetString(N.Cluster.LabelArabic), 20);

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Cluster.Kind);
            yield return node.GetString(N.Cluster.Scope);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            if (node.GetString(N.Cluster.Kind) == QK.THEME) yield return Tab("theme",   "Passages".t(), () => ThemeTab(node));
            else                                            yield return Tab("passage", "Verses".t(),   () => PassageTab(node));
        }

        // -----------------------------------------------------------------------------------
        // A theme: where the Quran tells it
        // -----------------------------------------------------------------------------------

        private static IComponent ThemeTab(Node theme)
        {
            return Defer(async () =>
            {
                var profile  = await QuranApi.ClusterProfileAsync(theme.UID);
                var passages = profile.Passages ?? new QK.PassageRow[0];
                var page     = VStack().WS().P(16).Gap(12.px());

                page.Add(Arabic.Text(profile.LabelArabic, 30));
                page.Add(TextBlock(profile.Label).Secondary());
                page.Add(TextBlock(string.Format("Told in {0} surahs, in {1} passages and {2} verses. The passages were grouped because they lean on the same distinctive words.".t(),
                                                 passages.Select(p => p.SurahNumber).Distinct().Count(), passages.Length, passages.Sum(p => p.Verses))).Small());
                page.Add(KeyWords(profile));

                foreach (var surah in passages.GroupBy(p => p.SurahNumber))
                {
                    var first = surah.First();
                    page.Add(TextBlock(first.SurahNumber + ". " + first.SurahName).SemiBold().MediumPlus().PT(8)
                                .OnClick((_, __) => Lists.Open(first.SurahUid))
                                .Style(s => s.setProperty("cursor", "pointer")));
                    foreach (var p in surah) page.Add(Clusters.PassageCard(p, showSurah: false, showTheme: false));
                }

                page.Add(TextBlock(profile.Method + " " + "Built".t() + " " + profile.RunId + ".").Tiny().Secondary());
                return page;
            }).S();
        }

        // -----------------------------------------------------------------------------------
        // A passage: read in order, in its surah
        // -----------------------------------------------------------------------------------

        private static IComponent PassageTab(Node passage)
        {
            return Defer(async () =>
            {
                var profile = await QuranApi.ClusterProfileAsync(passage.UID);
                var row     = (profile.Passages ?? new QK.PassageRow[0]).FirstOrDefault();
                var page    = VStack().WS().P(16).Gap(12.px());
                if (row is null) return page;

                // Previous and next walk the surah passage by passage; the title opens the surah.
                var where = HStack().WS().AlignItemsCenter().Gap(8.px());
                if (profile.Previous != default(UID128))
                    where.Add(Button("Previous".t()).SetIcon(UIcons.AngleLeft).Compact().OnClick(() => Lists.Open(profile.Previous)));
                where.Add(TextBlock(string.Format("{0}. {1} · {2} · passage {3}".t(), row.SurahNumber, row.SurahName, Clusters.Range(row), row.Ordinal)).SemiBold().Grow()
                             .OnClick((_, __) => Lists.Open(row.SurahUid))
                             .Style(s => s.setProperty("cursor", "pointer")));
                if (profile.Next != default(UID128))
                    where.Add(Button("Next".t()).SetIcon(UIcons.AngleRight, afterText: true).Compact().OnClick(() => Lists.Open(profile.Next)));

                page.Add(where);
                page.Add(Arabic.Text(profile.LabelArabic, 26));
                page.Add(TextBlock(profile.Label).Secondary());

                if (row.ThemeUid != default(UID128))
                    page.Add(HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().Children(
                        TextBlock("Part of a theme the Quran tells in several surahs:".t()).Small(),
                        Clusters.ThemeLink(row.ThemeUid, row.ThemeLabelArabic, row.ThemeLabel, row.ThemeSurahs)));

                page.Add(KeyWords(profile));

                foreach (var v in profile.Verses ?? new QK.VerseLine[0])
                    page.Add(Lists.VerseRow(v.Uid, v.VerseKey, v.Text, v.Translation));

                return page;
            }).S();
        }

        private static IComponent KeyWords(QK.ClusterProfileDto profile)
            => Panels.Row(
                   Panels.Panel("Key words".t(),
                                "Lemmas these verses use far more than the rest of the Quran: verses using it × (log of how rare it is)². Particles and pronouns are left out.".t(),
                                Lists.RankedBars(profile.KeyLemmas, Palette.Lemma, value: r => r.Score, valueText: r => r.Count.ToString("N0"))),
                   Panels.Panel("Key roots".t(),
                                "The same for roots: the families of words the passage is built from.".t(),
                                Lists.RankedBars(profile.KeyRoots, Palette.Root, value: r => r.Score, valueText: r => r.Count.ToString("N0"))));
    }
}
