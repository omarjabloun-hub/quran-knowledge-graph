using System;
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
using static Transpose.Core.dom;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// A surah. Its page opens on the text itself — read in order, in Arabic, as continuous text in
    /// its passages (<see cref="ReadingPane"/>), a clicked verse opening its translation beside it —
    /// then its lexical profile: how long its verses run, which roots it leans on
    /// and which it uses far more than the rest of the Quran.
    /// </summary>
    public sealed class SurahRenderer : QuranRendererBase
    {
        public SurahRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Surah.Type,
            DisplayName = "Surah",
            Icon        = UIcons.BookQuran.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.Surah.Number) + ". " + LabelFields.Field(N.Surah.NameScholarly)),
        }) { ShortDisplayName = "SURA"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Word.Type, N.Juz.Type, N.Revelation.Type, N.Cluster.Type };

        protected override IComponent CardBody(Node node)
            => HStack().WS().AlignItemsCenter().Gap(12.px()).Children(
                   Arabic.Inline(node.GetString(N.Surah.NameArabic), 24),
                   TextBlock(node.GetString(N.Surah.NameEnglish)).Small().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Surah.RevelationPlace);
            yield return Panels.Number(node.GetInt(N.Surah.AyahCount)) + " " + "verses".t();
            yield return "revealed".t() + " #" + node.GetInt(N.Surah.RevelationOrder);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("read",     "Read".t(),           () => ReadTab(node), scrolls: false);
            yield return Tab("passages", "Passages".t(),       () => PassagesTab(node));
            yield return Tab("similar",  "Similar surahs".t(), () => SimilarTab(node));
            yield return Tab("profile",  "Profile".t(),        () => ProfileTab(node));
        }

        // -----------------------------------------------------------------------------------
        // Read: the surah as it is read
        // -----------------------------------------------------------------------------------

        private static IComponent ReadTab(Node surah)
        {
            var number = surah.GetInt(N.Surah.Number);
            return new ReadingPane(new ReadingPane.Settings
            {
                Uid               = surah.UID,
                OptionsKey        = "quran",
                KeyWordsByDefault = false,
                ReadInQuran       = true,
                Header            = _ => Raw(Look.El("div", "qkg-reader-header",
                                        Look.El("span", "qkg-note", "Click a verse for its translation, its words and the verses related to it.".t()),
                                        Look.El("span", "qkg-toolbar-spacer"),
                                        Look.Button("Open in the Quran View".t(), () => QuranLinks.Go(number), "soft", "☰"))).WS(),
            }).S();
        }

        // -----------------------------------------------------------------------------------
        // Passages: the surah's sections, and the themes they share with other surahs
        // -----------------------------------------------------------------------------------

        private static IComponent PassagesTab(Node surah)
        {
            return Defer(async () =>
            {
                var result = await QuranApi.SurahPassagesAsync(surah.UID);
                if (!result.Built)
                    return VerseRenderer.Body(Banner("The passages have not been built yet. An administrator builds them with `just clusters` (the qkg/clusters/build endpoint); it takes a few seconds.".t()).Warning());

                var passages = result.Passages ?? new QK.PassageRow[0];
                var themed   = passages.Count(p => UID128.IsNotNull(p.ThemeUid));

                var parts = new List<IComponent>
                {
                    Look.Heading(string.Format("{0} passages".t(), passages.Length),
                                 string.Format("Cut where the vocabulary changes; {0} of them tell a story or treat a subject that other surahs share — in teal.".t(), themed)),
                    Clusters.Strip(passages),
                    Raw(Look.El("p", "qkg-note", result.Method + " " + "Built".t() + " " + result.RunId + ".")),
                };
                foreach (var p in passages) parts.Add(Clusters.PassageCard(p, showSurah: false));
                return VerseRenderer.Body(parts.ToArray());
            }).S();
        }

        // -----------------------------------------------------------------------------------
        // Similar surahs: which surahs lean on the same distinctive words, and hold verses close in meaning
        // -----------------------------------------------------------------------------------

        private const int SIMILAR_COUNT = 10;

        private static IComponent SimilarTab(Node surah)
        {
            return Defer(async () =>
            {
                var similar = await QuranApi.SimilarSurahsAsync(surah.UID, SIMILAR_COUNT);
                var parts = new List<IComponent>
                {
                    Look.Heading("Similar surahs".t(), "Ranked by".t() + ": " + similar.Method + ". " +
                        "Each score is standardised over the other surahs: 2.0 means two standard deviations above the typical surah. The Arabic under each is what they share most; the verse pairs are the closest in meaning; the themes are stories or subjects both tell.".t()),
                };
                foreach (var s in similar.Surahs ?? new QK.SimilarSurah[0]) parts.Add(SimilarCard(s));
                return VerseRenderer.Body(parts.ToArray());
            }).S();
        }

        private static IComponent SimilarCard(QK.SimilarSurah s)
        {
            var card = Look.El("div", "qkg-card qkg-similar-surah");
            var head = Look.El("div", "qkg-keys");
            head.appendChild(Look.SurahNumber(s.Number));
            head.appendChild(Look.El("div", "qkg-reader-header-name", Look.El("span", "qkg-h3", s.Name), Look.El("span", "qkg-note", s.NameEnglish)));
            head.appendChild(Look.Ar(s.NameArabic, "qkg-arw qkg-similar-surah-ar"));
            head.appendChild(Look.PlaceBadge(s.Place));
            head.appendChild(Look.El("span", "qkg-toolbar-spacer"));
            head.appendChild(Look.Badge("score".t() + " " + s.Score.ToString("0.0"), "teal"));
            head.appendChild(Look.Badge("lemmas".t() + " " + s.Lemmas.ToString("+0.0;-0.0;0.0")));
            head.appendChild(Look.Badge("roots".t() + " " + s.Roots.ToString("+0.0;-0.0;0.0")));
            head.appendChild(Look.Badge("meaning".t() + " " + s.Meaning.ToString("+0.0;-0.0;0.0")));
            card.appendChild(head);

            var shared = Look.El("div", "qkg-keys");
            foreach (var lemma in s.SharedLemmas ?? new QK.Counted[0])
            {
                var captured = lemma;
                shared.appendChild(Look.OnClick(Look.Ar(captured.Label, "qkg-arw qkg-similar-surah-lemma"), () => Lists.Open(captured.Uid)));
            }
            card.appendChild(shared);

            var pairs = s.CloseVerses ?? new QK.VersePair[0];
            if (pairs.Length > 0)
            {
                var row = Look.El("div", "qkg-keys", Look.El("span", "qkg-note", "Close in meaning:".t()));
                foreach (var pair in pairs.Take(3))
                {
                    var p = pair;
                    row.appendChild(Look.El("span", "qkg-note",
                        Look.OnClick(Look.El("span", "qkg-link", p.SeedVerseKey), () => Lists.Open(p.SeedUid)),
                        Look.El("span", "", " ↔ "),
                        Look.OnClick(Look.El("span", "qkg-link", p.VerseKey), () => Lists.Open(p.Uid))));
                }
                row.appendChild(Look.El("span", "qkg-note", string.Format("{0} pairs among each verse's 10 nearest".t(), pairs.Length)));
                card.appendChild(row);
            }

            var themes = (s.SharedThemes ?? new QK.Counted[0]).Take(4).ToArray();
            if (themes.Length > 0)
            {
                var row = Look.El("div", "qkg-keys", Look.El("span", "qkg-note", "Shared themes:".t()));
                foreach (var t in themes) row.appendChild(Clusters.ThemeChip(t.Uid, t.Detail, t.Label, 0, 0));
                card.appendChild(row);
            }

            var uid = s.Uid;
            Look.OnClick(card, () => Lists.Open(uid));
            EnsureStyles();
            return Raw(card).WS();
        }

        // -----------------------------------------------------------------------------------
        // Profile: what this surah's language looks like next to the rest
        // -----------------------------------------------------------------------------------

        private static IComponent ProfileTab(Node surah)
        {
            return Defer(async () =>
            {
                var p        = await QuranApi.SurahProfileAsync(surah.UID);
                var place    = surah.GetString(N.Surah.RevelationPlace);
                var passages = QuranApi.SurahPassagesAsync(surah.UID);
                var similar  = QuranApi.SimilarSurahsAsync(surah.UID, 3);

                var hero = Look.El("div", "qkg-surah-hero qkg-surah-hero-" + (place == QK.MEDINAN ? "medinan" : "meccan"));
                hero.appendChild(Look.SurahNumber(surah.GetInt(N.Surah.Number), true, "lg"));
                var name = Look.El("div", "qkg-surah-hero-name");
                name.appendChild(Look.El("span", "qkg-h1", (surah.GetString(N.Surah.NameScholarly) ?? surah.GetString(N.Surah.NameLatin) ?? "") + " · " + (surah.GetString(N.Surah.NameEnglish) ?? "")));
                var badges = Look.El("div", "qkg-keys", Look.PlaceBadge(place),
                    Look.Badge(string.Format("{0} verses".t(), Panels.Number(p.Verses))),
                    Look.Badge(string.Format("revealed #{0}".t(), surah.GetInt(N.Surah.RevelationOrder))));
                name.appendChild(badges);
                hero.appendChild(name);
                hero.appendChild(Look.Ar(surah.GetString(N.Surah.NameArabic), "qkg-arw qkg-surah-hero-ar"));
                var number = surah.GetInt(N.Surah.Number);
                hero.appendChild(Look.El("div", "qkg-keys qkg-surah-hero-actions",
                    Look.Button("Read".t(), () => QuranLinks.Go(number), "primary", "☰"),
                    Look.Button("Listen".t(), () => { QuranLinks.ListenOnArrival = true; QuranLinks.Go(number); }, "plain", "▶")));

                var stats = Look.Columns(140,
                    Look.Stat(Panels.Number(p.Verses),         "verses".t()),
                    Look.Stat(Panels.Number(p.Words),          "words".t()),
                    Look.Stat(Panels.Number(p.DistinctRoots),  "distinct roots".t()),
                    Look.Stat(Panels.Number(p.DistinctLemmas), "distinct lemmas".t()));

                var lengths = p.VerseLengths ?? new int[0];
                var longest = lengths.Length == 0 ? 0 : Array.IndexOf(lengths, lengths.Max());
                var verseUIDs = p.VerseUIDs ?? new UID128[0];
                var lengthChart = Look.Panel("Verse lengths".t(),
                    string.Format("Words in each verse, in order — the longest is {0}:{1}, {2} words. Early Meccan surahs move in short verses; Medinan ones in long, legislative ones.".t(), number, longest + 1, lengths.Length == 0 ? 0 : lengths[longest]),
                    Charts.Columns(lengths.Select(x => (double)x).ToArray(),
                                   i => i == longest ? Look.TEAL : Palette.ForPlace(place),
                                   i => number + ":" + (i + 1) + " · " + lengths[i] + " " + "words".t(),
                                   i => { if (i < verseUIDs.Length) Lists.Open(verseUIDs[i]); },
                                   height: 160, xLabels: new[] { number + ":1", number + ":" + Math.Max(1, lengths.Length / 2), number + ":" + lengths.Length },
                                   label: "Words in each verse".t()));

                var strip = Defer(async () =>
                {
                    var result = await passages;
                    var rows   = result.Passages ?? new QK.PassageRow[0];
                    if (!result.Built || rows.Length == 0) return Raw(Look.El("span", ""));
                    return Look.Panel(string.Format("The surah in its {0} passages".t(), rows.Length),
                                      "Teal passages tell a story or treat a subject other surahs share. Click one to open it.".t(),
                                      Clusters.Strip(rows));
                });

                var closest = Defer(async () =>
                {
                    var result = await similar;
                    var list   = VStack().WS().Gap(4.px());
                    foreach (var s in (result.Surahs ?? new QK.SimilarSurah[0]).Take(3))
                    {
                        var captured = s;
                        var row = Look.El("div", "qkg-close-surah");
                        row.appendChild(Look.El("div", "qkg-keys", Look.SurahNumber(s.Number, size: "sm"), Look.El("strong", "", s.Name), Look.El("span", "qkg-toolbar-spacer"), Look.Ar(s.NameArabic, "qkg-arw qkg-close-surah-ar")));
                        row.appendChild(Look.El("div", "qkg-keys",
                            Look.Badge("score".t() + " " + s.Score.ToString("0.0"), "teal"),
                            Look.Badge("lemmas".t() + " " + s.Lemmas.ToString("+0.0;-0.0;0.0")),
                            Look.Badge("roots".t() + " " + s.Roots.ToString("+0.0;-0.0;0.0")),
                            Look.Badge("meaning".t() + " " + s.Meaning.ToString("+0.0;-0.0;0.0"))));
                        list.Add(Raw(Look.OnClick(row, () => Lists.Open(captured.Uid))));
                    }
                    return Look.Panel("Closest surahs".t(), "Shared distinctive lemmas and roots, and verses close in meaning.".t(), list);
                });

                var pos = Look.Panel("Parts of speech".t(), null, VStack().WS().Gap(10.px()).Children(
                    Raw(Look.Parts(string.Format("Nouns {0}, verbs {1}, particles {2}".t(), p.Nouns, p.Verbs, p.Particles), (p.Nouns, Palette.Noun), (p.Verbs, Palette.Verb), (p.Particles, Palette.Other))),
                    Raw(Look.El("div", "qkg-keys", Look.Key(Palette.Noun, "Nouns".t(), Panels.Number(p.Nouns)), Look.Key(Palette.Verb, "Verbs".t(), Panels.Number(p.Verbs)), Look.Key(Palette.Other, "Particles".t(), Panels.Number(p.Particles))))));

                EnsureStyles();
                var main = VStack().WS().Gap(16.px()).Style(st => st.setProperty("min-width", "0")).Children(
                    stats, strip, lengthChart,
                    Look.Columns(260,
                        Look.Panel("Distinctive roots".t(), "Used here far more than in the rest of the Quran — they name what the surah is about.".t(),
                                   Charts.Bars(p.DistinctiveRoots, Palette.Root, value: r => r.Score, valueText: r => r.Score.ToString("0.0"))),
                        Look.Panel("Most frequent roots".t(), "How often each root occurs in this surah.".t(), Charts.Bars(p.TopRoots, Palette.Root)),
                        Look.Panel("Most frequent lemmas".t(), "Dictionary headwords of the content words.".t(), Charts.Bars(p.TopLemmas, Palette.Lemma))));
                var aside = VStack().WS().Gap(16.px()).Children(closest, pos);

                var layout = Look.El("div", "qkg-surah-layout");
                layout.appendChild(main.Render());
                layout.appendChild(aside.Render());

                return VerseRenderer.Body(Raw(hero).WS(), Raw(layout).WS());
            }).S();
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-surah-hero { display: flex; flex-wrap: wrap; align-items: center; gap: 16px 28px; padding: 26px 30px; border-radius: 24px; }
.qkg-surah-hero-meccan { background: var(--qkg-meccan-tint); }
.qkg-surah-hero-medinan { background: var(--qkg-medinan-tint); }
.qkg-surah-hero-name { flex: 1 1 280px; display: flex; flex-direction: column; gap: 8px; }
.qkg-surah-hero-ar { font-size: 54px; line-height: 1.4; }
.qkg-surah-hero-meccan .qkg-surah-hero-ar { color: var(--qkg-meccan); }
.qkg-surah-hero-medinan .qkg-surah-hero-ar { color: var(--qkg-medinan); }
.qkg-surah-hero-actions { flex: 1 1 100%; }
.qkg-surah-layout { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 20px; align-items: start; }
@media (max-width: 1000px) { .qkg-surah-layout { grid-template-columns: minmax(0, 1fr); } }
.qkg-close-surah { display: flex; flex-direction: column; gap: 8px; padding: 12px 0; border-top: 1px solid var(--qkg-line); }
.qkg-close-surah-ar { font-size: 20px; }
.qkg-similar-surah { display: flex; flex-direction: column; gap: 10px; }
.qkg-similar-surah-ar { font-size: 22px; }
.qkg-similar-surah-lemma { font-size: 19px; color: var(--qkg-lemma); }
";
            document.head.appendChild(style);
        }
    }
}
