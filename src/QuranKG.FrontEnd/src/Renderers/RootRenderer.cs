using System.Collections.Generic;
using System.Linq;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using static Mosaik.UI;
using static Tesserae.UI;
using static Transpose.Core.dom;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// A root — the two to four consonants a family of Arabic words is built on (ر ح م: mercy, the
    /// womb, the Merciful). Its page shows where in the Quran the family lives: across the 114
    /// surahs, between Meccan and Medinan, which lemmas grow from it and which forms it takes.
    /// </summary>
    public sealed class RootRenderer : QuranRendererBase
    {
        public RootRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Root.Type,
            DisplayName = "Root",
            Icon        = UIcons.Seedling.ToString(),
            LabelField  = LabelFields.FirstOf(N.Root.Spaced, N.Root.RootLetters),
        }) { ShortDisplayName = "ROOT"; }

        protected override string[] Related360Types => new[] { N.Lemma.Type, N.Verse.Type, N.Word.Type };

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("distribution", "Distribution".t(), () => DistributionTab(node));
        }

        private static IComponent DistributionTab(Node root)
        {
            return Defer(async () =>
            {
                var p      = await QuranApi.RootProfileAsync(root.UID);
                var corpus = await QuranApi.CorpusAsync();
                var surahs = corpus.Overview.SurahRows.OrderBy(s => s.Number).ToArray();
                Look.EnsureStyles();
                EnsureStyles();

                var hero = Look.El("div", "qkg-root-hero");
                hero.appendChild(Look.El("div", "qkg-root-hero-letters",
                    Look.Ar(WordRenderer.Spaced(p.Letters), "qkg-arw qkg-root-hero-ar"),
                    Look.El("span", "qkg-kicker", "Root".t())));
                var stats = Look.El("div", "qkg-root-hero-stats");
                stats.appendChild(Stat(Panels.Number(p.Occurrences), "occurrences".t(), null));
                stats.appendChild(Stat(Panels.Number(p.Verses), "verses".t(), () => Lists.ShowNodes(WordRenderer.Spaced(p.Letters) + " — " + "verses".t(), p.VerseUIDs)));
                stats.appendChild(Stat(Panels.Number(p.Surahs), string.Format("of {0} surahs".t(), surahs.Length), null));
                var places = Look.El("div", "qkg-root-places");
                places.appendChild(Look.El("span", "qkg-num",
                    Look.El("span", "qkg-root-meccan", Panels.Number(p.Meccan)), Look.El("span", "", " · "), Look.El("span", "qkg-root-medinan", Panels.Number(p.Medinan))));
                places.appendChild(Look.Parts(string.Format("Meccan {0}, Medinan {1}".t(), p.Meccan, p.Medinan), (p.Meccan, Palette.Meccan), (p.Medinan, Palette.Medinan)));
                places.appendChild(Look.El("span", "qkg-note", "Meccan · Medinan".t()));
                stats.appendChild(places);
                hero.appendChild(stats);

                var chartHost = VStack().WS();
                void ShowChart(bool perThousand)
                {
                    chartHost.Clear();
                    chartHost.Add(SurahChart(p, surahs, perThousand));
                }
                ShowChart(false);

                // Where the root is densest, relative to each surah's length — clickable, unlike the bars.
                var densest = surahs.Select((s, i) => new QK.Counted
                    {
                        Uid    = s.Uid,
                        Label  = s.Number + ". " + s.Name,
                        Detail = Panels.Number(p.PerSurah[i]) + " " + "occurrences in".t() + " " + Panels.Number(s.Words) + " " + "words".t(),
                        Count  = p.PerSurah[i],
                        Score  = s.Words > 0 ? 1000.0 * p.PerSurah[i] / s.Words : 0,
                    })
                   .Where(c => c.Count > 0)
                   .OrderByDescending(c => c.Score)
                   .Take(12)
                   .ToArray();

                return Renderers.VerseRenderer.Body(
                    Raw(hero).WS(),
                    Look.Card(null,
                        Look.Heading(string.Format("Across the {0} surahs".t(), surahs.Length), "In the order of the book; a bar opens its surah. Per 1,000 words makes a short surah comparable to a long one.".t(),
                            Raw(Look.Segmented(new[] { "Occurrences".t(), "Per 1,000 words".t() }, 0, i => ShowChart(i == 1), "Scale".t()))),
                        chartHost,
                        Raw(Look.El("div", "qkg-keys", Look.Key(Palette.Meccan, "Meccan".t()), Look.Key(Palette.Medinan, "Medinan".t())))),
                    Look.Columns(300,
                        Look.Panel("Where it is densest".t(), "Surahs where the root is most frequent for their length.".t(),
                                   Charts.Bars(densest, Palette.Root, arabicLabels: false, value: c => c.Score, valueText: c => c.Score.ToString("0.0") + " ‰")),
                        Look.Panel("Its lemmas".t(), "The dictionary headwords built on this root.".t(), Charts.Bars(p.Lemmas, Palette.Lemma)),
                        Look.Panel("Word forms".t(), "The forms it takes in the text, without diacritics.".t(),
                                   Charts.Bars((p.Forms ?? new QK.Counted[0]).Take(20).ToArray(), "#8a9491"))),
                    Look.Panel("Verses".t(), "Every verse a word of this root occurs in.".t(),
                               SearchArea().WithFacets()
                                  .SearchFor(new SearchRequest().WithTargetUIDs(p.VerseUIDs ?? new UID.UID128[0]))
                                  .WS().H(640.px())));
            }).S();
        }

        private static HTMLElement Stat(string value, string label, System.Action open)
        {
            var el = Look.El("div", "qkg-root-stat", Look.El("span", "qkg-stat-value", value), Look.El("span", "qkg-note", label));
            return open is null ? el : Look.OnClick(el, open);
        }

        private static IComponent SurahChart(QK.RootProfileDto p, QK.SurahStats[] surahs, bool perThousand)
        {
            var values = surahs.Select((s, i) => perThousand ? (s.Words > 0 ? 1000.0 * p.PerSurah[i] / s.Words : 0) : p.PerSurah[i]).ToArray();
            return Charts.Columns(values,
                                  i => Palette.ForPlace(surahs[i].Place),
                                  i => surahs[i].Number + ". " + surahs[i].Name + " — " + (perThousand ? values[i].ToString("0.0") + " ‰" : values[i].ToString("N0")),
                                  i => Lists.Open(surahs[i].Uid),
                                  height: 200,
                                  xLabels: new[] { "1", "20", "40", "60", "80", "100", surahs.Length.ToString() },
                                  yFormat: v => perThousand ? v.ToString("0.#") : v.ToString("N0"),
                                  label: "The root in each surah".t());
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-root-hero { display: flex; flex-wrap: wrap; align-items: center; gap: 24px 40px; padding: 26px 30px; border-radius: 24px; background: var(--qkg-root-tint); }
.qkg-root-hero-letters { display: flex; flex-direction: column; align-items: center; gap: 10px; min-width: 150px; }
.qkg-root-hero-ar { font-size: 64px; line-height: 1.35; color: var(--qkg-root); }
.qkg-root-hero-letters .qkg-kicker { color: var(--qkg-root); }
.qkg-root-hero-stats { flex: 1 1 360px; display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 14px; align-items: center; }
.qkg-root-stat { display: flex; flex-direction: column; }
.qkg-root-places { display: flex; flex-direction: column; gap: 6px; font-weight: 600; }
.qkg-root-meccan { color: var(--qkg-meccan); }
.qkg-root-medinan { color: var(--qkg-medinan); }
";
            document.head.appendChild(style);
        }
    }
}
