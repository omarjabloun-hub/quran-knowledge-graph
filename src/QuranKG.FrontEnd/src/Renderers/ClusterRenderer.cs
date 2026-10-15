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
    /// passages read as text, surah by surah, the words that tie them marked. A PASSAGE — a run of
    /// consecutive verses of one surah — opens on its text, with the theme it belongs to and the
    /// passages either side of it. Both read through <see cref="ReadingPane"/>: a verse is a
    /// sentence of the text, not a box, and clicking it opens its translation beside it.
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
        protected override string TileText(Node node)
        {
            var kind = node.GetString(N.Cluster.Kind);
            return kind == QK.THEME ? "THEME" : kind == QK.SIMILAR_GROUP ? "SIM" : "PASS";
        }

        protected override IComponent CardBody(Node node) => Arabic.Inline(node.GetString(N.Cluster.LabelArabic), 20);

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Cluster.Kind);
            yield return node.GetString(N.Cluster.Scope);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            // A theme and a similarity group (qkg/clusters/similar-build) are both passages of several surahs.
            if (node.GetString(N.Cluster.Kind) != QK.PASSAGE)
            {
                yield return Tab("read",  "Read".t(),     () => ReadTheme(node), scrolls: false);
                yield return Tab("theme", "Passages".t(), () => ThemeTab(node));
            }
            else
            {
                yield return Tab("read",    "Read".t(),      () => ReadPassage(node), scrolls: false);
                yield return Tab("passage", "Key words".t(), () => PassageTab(node));
            }
        }

        // -----------------------------------------------------------------------------------
        // Read: the verses as text, the key words marked
        // -----------------------------------------------------------------------------------

        /// <summary>Every passage of the theme, surah by surah, as text — the words that tie them marked.</summary>
        private static IComponent ReadTheme(Node theme)
            => new ReadingPane(new ReadingPane.Settings
            {
                Uid               = theme.UID,
                OptionsKey        = "theme",
                KeyWordsByDefault = true,
                ReadInQuran       = true,
                Header            = text =>
                {
                    var sections = text.Sections ?? new QK.ReadingSection[0];
                    var surahs   = sections.Select(x => x.SurahNumber).Distinct().Count();
                    var head     = Look.El("div", "qkg-theme-hero");
                    head.appendChild(Look.El("span", "qkg-kicker", (text.Kind == QK.SIMILAR_GROUP ? "Similarity group · {0} surahs" : "Theme · told in {0} surahs").t().Replace("{0}", surahs.ToString())));
                    head.appendChild(Look.Ar(text.LabelArabic, "qkg-arw qkg-theme-hero-ar"));
                    if (!string.IsNullOrEmpty(text.Label)) head.appendChild(Look.El("span", "qkg-h3", text.Label));
                    head.appendChild(Look.El("span", "qkg-note", string.Format("{0} passages · {1} verses — ".t(), sections.Length, sections.Sum(x => (x.Verses ?? new QK.ReadingVerse[0]).Length))
                        + (text.Kind == QK.SIMILAR_GROUP
                              ? "passages close in Arabic meaning and in their lemmas and roots; the words they share most are marked. A surah's name opens the passage in its surah."
                              : "grouped because they lean on the same distinctive words, marked in the text. A surah's name opens the passage in its surah.").t()));
                    return Raw(head).WS();
                },
            }).S();

        /// <summary>The passage as text, with the way to the passages either side and to its theme.</summary>
        private static IComponent ReadPassage(Node passage)
            => new ReadingPane(new ReadingPane.Settings
            {
                Uid               = passage.UID,
                OptionsKey        = "passage",
                KeyWordsByDefault = true,
                ReadInQuran       = true,
                Header            = text =>
                {
                    var section = (text.Sections ?? new QK.ReadingSection[0]).FirstOrDefault();
                    var where   = Look.El("div", "qkg-reader-header");
                    if (UID128.IsNotNull(text.Previous)) where.appendChild(Look.IconButton("‹", "The passage before".t(), () => Lists.Open(text.Previous)));
                    if (section is object)
                    {
                        where.appendChild(Look.SurahNumber(section.SurahNumber));
                        where.appendChild(Look.El("div", "qkg-reader-header-name",
                            Look.El("span", "qkg-h3", section.SurahName + " · " + string.Format("passage {0}".t(), section.Ordinal)),
                            Look.El("span", "qkg-note", QuranReader.Range(section))));
                        where.appendChild(Look.Ar(text.LabelArabic, "qkg-arw qkg-reader-header-ar"));
                        if (UID128.IsNotNull(section.ThemeUid)) where.appendChild(Clusters.ThemeChip(section.ThemeUid, section.ThemeLabelArabic, section.ThemeLabel, section.ThemeSurahs, section.ThemeOrdinal));
                    }
                    where.appendChild(Look.El("span", "qkg-toolbar-spacer"));
                    if (section is object)
                        where.appendChild(Look.Button("Read in its surah".t(), () => QuranLinks.Go(section.SurahNumber, (section.Verses ?? new QK.ReadingVerse[0]).Select(v => v.Ayah).FirstOrDefault()), "ghost", "☰"));
                    if (UID128.IsNotNull(text.Next)) where.appendChild(Look.IconButton("›", "The passage after".t(), () => Lists.Open(text.Next)));
                    return Raw(where).WS();
                },
            }).S();

        // -----------------------------------------------------------------------------------
        // A theme: where the Quran tells it
        // -----------------------------------------------------------------------------------

        private static IComponent ThemeTab(Node theme)
        {
            return Defer(async () =>
            {
                var profile  = await QuranApi.ClusterProfileAsync(theme.UID);
                var passages = profile.Passages ?? new QK.PassageRow[0];
                var parts    = new List<IComponent>();

                var hero = Look.El("div", "qkg-theme-hero qkg-theme-hero-tab");
                hero.appendChild(Look.Ar(profile.LabelArabic, "qkg-arw qkg-theme-hero-ar"));
                if (!string.IsNullOrEmpty(profile.Label)) hero.appendChild(Look.El("span", "qkg-h3", profile.Label));
                hero.appendChild(Look.El("span", "qkg-note", string.Format("Told in {0} surahs, in {1} passages and {2} verses. The passages were grouped because they lean on the same distinctive words.".t(),
                                                 passages.Select(p => p.SurahNumber).Distinct().Count(), passages.Length, passages.Sum(p => p.Verses))));
                parts.Add(Raw(hero).WS());
                parts.Add(KeyWords(profile));

                foreach (var surah in passages.GroupBy(p => p.SurahNumber))
                {
                    var first = surah.First();
                    var title = Look.El("div", "qkg-keys", Look.SurahNumber(first.SurahNumber, size: "sm"), Look.El("h3", "qkg-h3", first.SurahName));
                    parts.Add(Raw(Look.OnClick(title, () => Lists.Open(first.SurahUid))));
                    foreach (var p in surah) parts.Add(Clusters.PassageCard(p, showSurah: false, showTheme: false));
                }

                parts.Add(Raw(Look.El("p", "qkg-note", profile.Method + " " + "Built".t() + " " + profile.RunId + ".")));
                return VerseRenderer.Body(parts.ToArray());
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
                if (row is null) return VerseRenderer.Body();

                var where = Look.El("div", "qkg-keys");
                if (UID128.IsNotNull(profile.Previous)) where.appendChild(Look.IconButton("‹", "The passage before".t(), () => Lists.Open(profile.Previous)));
                where.appendChild(Look.OnClick(Look.El("span", "qkg-h3", string.Format("{0}. {1} · {2} · passage {3}".t(), row.SurahNumber, row.SurahName, Clusters.Range(row), row.Ordinal)), () => Lists.Open(row.SurahUid)));
                where.appendChild(Look.El("span", "qkg-toolbar-spacer"));
                if (UID128.IsNotNull(profile.Next)) where.appendChild(Look.IconButton("›", "The passage after".t(), () => Lists.Open(profile.Next)));

                var hero = Look.El("div", "qkg-theme-hero qkg-theme-hero-tab");
                hero.appendChild(Look.Ar(profile.LabelArabic, "qkg-arw qkg-theme-hero-ar"));
                if (!string.IsNullOrEmpty(profile.Label)) hero.appendChild(Look.El("span", "qkg-note", profile.Label));
                if (UID128.IsNotNull(row.ThemeUid))
                    hero.appendChild(Look.El("div", "qkg-keys",
                        Look.El("span", "qkg-note", "Part of a theme the Quran tells in several surahs:".t()),
                        Clusters.ThemeChip(row.ThemeUid, row.ThemeLabelArabic, row.ThemeLabel, row.ThemeSurahs, 0)));

                return VerseRenderer.Body(Raw(where).WS(), Raw(hero).WS(), KeyWords(profile));
            }).S();
        }

        private static IComponent KeyWords(QK.ClusterProfileDto profile)
            => Look.Columns(340,
                   Look.Panel("Key words".t(),
                              "Lemmas these verses use far more than the rest of the Quran: verses using it × (log of how rare it is)². Particles and pronouns are left out.".t(),
                              Charts.Bars(profile.KeyLemmas, Palette.Lemma, value: r => r.Score, valueText: r => r.Count.ToString("N0"))),
                   Look.Panel("Key roots".t(),
                              "The same for roots: the families of words the passage is built from.".t(),
                              Charts.Bars(profile.KeyRoots, Palette.Root, value: r => r.Score, valueText: r => r.Count.ToString("N0"))));
    }
}
