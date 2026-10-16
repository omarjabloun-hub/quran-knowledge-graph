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
    /// A verse. Its page reads Arabic first: the Uthmani text, then each word with its root, lemma
    /// and part of speech, and only then the translations. The Tafsir tab gives what Ibn Ashur (the main
    /// tafsir), al-Mukhtasar, al-Jalalayn and Ibn Kathir say on it, in Arabic and English. Two more tabs answer "where else does
    /// the Quran say this?" — the similarity ranking, computed from the Arabic, and the verses Ibn
    /// Kathir's tafsir relates to it, which is the reference that ranking is measured against.
    /// </summary>
    public sealed class VerseRenderer : QuranRendererBase
    {
        private const int SIMILAR_COUNT = 20;

        public VerseRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Verse.Type,
            DisplayName = "Verse",
            Icon        = UIcons.BookOpenReader.ToString(),
            LabelField  = N.Verse.VerseKey,
        }) { ShortDisplayName = "AYAH"; }

        protected override string[] Related360Types => new[] { N.Surah.Type, N.Juz.Type, N.Root.Type, N.Lemma.Type, N.Topic.Type, N.Cluster.Type, N.QurSimPair.Type };

        protected override IComponent CardBody(Node node)
            => VStack().WS().Children(
                   Arabic.Text(node.GetString(N.Verse.TextUthmani), 20),
                   TextBlock(node.GetString(N.Verse.EnglishSaheeh) ?? "").Tiny().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Verse.SurahName);
            yield return "Juz".t() + " " + node.GetInt(N.Verse.Juz);
            yield return "page".t() + " " + node.GetInt(N.Verse.Page);
            yield return node.GetString(N.Verse.RevelationPlace);

            var sajda = node.GetString(N.Verse.SajdaType);
            if (!string.IsNullOrEmpty(sajda)) yield return "Sajda".t() + " (" + sajda + ")";
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("verse",      "Verse".t(),            () => VerseTab(node));
            yield return Tab("commentary", "Tafsir".t(),           () => Body(TafsirView.Create(node.UID, heading: false)));
            yield return Tab("hadith",     "Hadith".t(),           () => HadithTab(node));
            yield return Tab("similar",    "Similar verses".t(),   () => SimilarTab(node));
            yield return Tab("tafsir",     "Ibn Kathir links".t(), () => TafsirTab(node));
        }

        // -----------------------------------------------------------------------------------
        // Verse: the Arabic, word by word, then the translations
        // -----------------------------------------------------------------------------------

        /// <summary>A tab's content in the look's column.</summary>
        internal static IComponent Body(params IComponent[] children)
        {
            Look.EnsureStyles();
            return VStack().WS().Gap(20.px()).Class("qkg-ui qkg-node-body").Children(children);
        }

        private static IComponent VerseTab(Node verse)
        {
            return Defer(async () =>
            {
                var words = await QuranApi.VerseWordsAsync(verse.UID);

                var hero = Look.Card(null);
                var head = Look.El("div", "qkg-keys",
                    Look.El("span", "qkg-h2", verse.GetString(N.Verse.VerseKey)),
                    Look.PlaceBadge(verse.GetString(N.Verse.RevelationPlace)),
                    Look.Badge(string.Format("Juz {0} · page {1}".t(), verse.GetInt(N.Verse.Juz), verse.GetInt(N.Verse.Page))),
                    Look.El("span", "qkg-toolbar-spacer"),
                    Look.Button("Read in its surah".t(), () => QuranLinks.Go(verse.GetString(N.Verse.VerseKey)), "soft", "☰"));
                hero.Add(Raw(head));
                var ar = Look.El("p", "qkg-node-ar", verse.GetString(N.Verse.TextUthmani));
                ar.setAttribute("dir", "rtl");
                ar.setAttribute("lang", "ar");
                hero.Add(Raw(ar));
                var translit = verse.GetString(N.Verse.Transliteration);
                if (!string.IsNullOrEmpty(translit)) hero.Add(Raw(Look.El("p", "qkg-node-translit", translit)));

                return Body(
                    hero,
                    Look.Panel("Word by word".t(),
                               "Each word with its root, its lemma and its part of speech, from the Quranic Arabic Corpus. Open a word for its segments and features.".t(),
                               VerseDetails.WordGrid(words.Words ?? new QK.WordRow[0], 6)),
                    Look.Columns(340,
                        Look.Panel("Roots".t(),  null, NeighborsLinks(words.RootUIDs  ?? new UID.UID128[0], wrap: true, maxItemsToShow: 40)),
                        Look.Panel("Lemmas".t(), null, NeighborsLinks(words.LemmaUIDs ?? new UID.UID128[0], wrap: true, maxItemsToShow: 40))),
                    Look.Panel("Translations".t(),
                               "Reading aids. The similarity work reads the Arabic, not these.".t(),
                               VStack().WS().Gap(14.px()).Children(
                                   Translation("Saheeh International", verse.GetString(N.Verse.EnglishSaheeh)),
                                   Translation("Ali Quli Qarai",       verse.GetString(N.Verse.EnglishQarai)))));
            }).S();
        }

        // -----------------------------------------------------------------------------------
        // Hadith: those that quote it, those close to it in meaning
        // -----------------------------------------------------------------------------------

        private static IComponent HadithTab(Node verse)
            => Defer(async () =>
            {
                var hadith  = await QuranApi.VerseHadithAsync(verse.UID, 12);
                var quoting = VStack().WS().Gap(10.px());
                foreach (var h in hadith.Quoting ?? new QK.HadithRow[0]) quoting.Add(HadithView.Row(h, showVerses: true));
                if ((hadith.Quoting ?? new QK.HadithRow[0]).Length == 0) quoting.Add(Raw(Look.El("p", "qkg-note", "No hadith of Bukhari or Muslim quotes it.".t())));

                var closest = VStack().WS().Gap(10.px());
                foreach (var h in hadith.Closest ?? new QK.HadithRow[0]) closest.Add(HadithView.Row(h, showVerses: true));
                if (!hadith.SemanticReady) closest.Add(Raw(Look.El("p", "qkg-note", "The hadith are not embedded yet.".t())));

                return Body(
                    Look.Heading("Hadith that quote it".t(),
                                 "Bukhari and Muslim whose Arabic quotes the verse (the verse set in braces by the edition, six words or more, or the whole of a short verse) or shares a phrase with it, then those whose English translator cites it.".t()),
                    quoting,
                    Look.Heading("Hadith close to it in meaning".t(),
                                 "The hadith whose text (without its chain) is nearest the verse's Arabic, by the same embedding model. Candidates to read: whether a hadith supports, explains, adds to or seems to sit in tension with the verse is a reading — ask the assistant to argue it.".t()),
                    closest);
            }, TextBlock("Finding the hadith…".t()).Secondary().P(16)).S();

        private static HTMLElement TranslationEl(string source, string text)
            => Look.El("div", "qkg-study-tr",
                   Look.El("p", "qkg-study-tr-text", text ?? ""),
                   Look.El("span", "qkg-note", source));

        private static IComponent Translation(string source, string text) => Raw(TranslationEl(source, text));

        // -----------------------------------------------------------------------------------
        // Similar verses: the ranking, and why each verse is in it
        // -----------------------------------------------------------------------------------

        private static IComponent SimilarTab(Node verse)
        {
            return Defer(async () =>
            {
                var similar = await QuranApi.SimilarVersesAsync(verse.UID, SIMILAR_COUNT);
                var parts = new List<IComponent>
                {
                    Look.Heading("Similar verses".t(), "Ranked by".t() + ": " + similar.Method + ". " +
                        "Each signal is standardised over every verse of the Quran — 2.0 means two standard deviations above the typical verse.".t()),
                };
                if (!similar.SemanticAvailable)
                    parts.Add(Raw(Look.El("p", "qkg-note", "The Arabic embeddings are not ready for this verse yet, so this ranking is lexical only.".t())));
                parts.Add(VerseDetails.SimilarList(similar.Verses ?? new QK.SimilarVerse[0], (uid, _) => Lists.Open(uid), withTranslation: true));
                return Body(parts.ToArray());
            }).S();
        }

        private static IComponent Signal(string name, double z)
        {
            var badge = Badge(name + " " + z.ToString("+0.0;-0.0;0.0"));
            return z >= 2 ? badge.Success() : z >= 1 ? badge.Info() : badge.Neutral();
        }

        // -----------------------------------------------------------------------------------
        // Tafsir links: the reference, for people to read
        // -----------------------------------------------------------------------------------

        private static IComponent TafsirTab(Node verse)
        {
            return Defer(async () =>
            {
                var words   = await QuranApi.VerseWordsAsync(verse.UID);
                var related = words.TafsirRelated ?? new QK.RelatedVerse[0];
                var list    = Look.El("div", "qkg-similar");

                foreach (var r in related)
                {
                    var captured = r;
                    var row = Look.El("div", "qkg-similar-row");
                    row.appendChild(Look.El("div", "qkg-similar-key", Look.El("span", "qkg-similar-vkey", r.VerseKey), Look.Badge(r.Grade, r.Degree == 2 ? "teal" : r.Degree == 1 ? "info" : "plain")));
                    row.appendChild(Look.El("div", "qkg-similar-text",
                        Look.Ar(r.Text, "qkg-arw qkg-similar-ar qkg-similar-ar-full"),
                        Look.El("span", "qkg-similar-en", r.Translation ?? "")));
                    list.appendChild(Look.OnClick(row, () => Lists.Open(captured.Uid)));
                }

                return Body(
                    Look.Heading("Related by Ibn Kathir".t(),
                                 "Verses Ibn Kathir's commentary relates to this one, as collected in the QurSim corpus (Sharaf & Atwell, 2012). This is the reference the similarity ranking is measured against — it is never used to compute it.".t()),
                    related.Length == 0 ? Raw(Look.El("p", "qkg-note", "Ibn Kathir relates no other verse to this one in QurSim.".t())) : Raw(list).WS());
            }).S();
        }
    }
}
