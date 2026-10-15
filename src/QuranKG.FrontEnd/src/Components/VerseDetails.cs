using System;
using System.Linq;
using Mosaik;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// The side panel a verse opens in a reader: the verse, its translations and transliteration,
    /// its tafsir in Arabic and English (<see cref="TafsirView"/>), the hadith that quote it or are
    /// close to it in meaning, the passage and theme it sits in, its words with their roots, the verses closest to it in
    /// the Arabic, and the verses Ibn Kathir relates to it. Each part loads on its own, so the
    /// translation shows at once and the similarity ranking follows.
    /// </summary>
    internal static class VerseDetails
    {
        private const int SIMILAR_COUNT = 6;
        private const int TAFSIR_COUNT  = 8;
        private const int HADITH_COUNT  = 6;

        /// <param name="goToVerse">Where a related verse leads: in the Quran View, to the verse in the text; elsewhere, to its page.</param>
        /// <param name="readInQuran">Whether to offer to read the verse in its surah (not in the Quran View itself).</param>
        /// <param name="listen">Listen to the surah's recitation from this verse; null when there is none (a passage, a theme).</param>
        internal static IComponent Create(QK.ReadingVerse v, QK.ReadingSection s, Action close, Action<UID128, string> goToVerse, bool readInQuran, Action<QK.ReadingVerse> listen = null)
        {
            Look.EnsureStyles();
            EnsureStyles();
            goToVerse = goToVerse ?? ((uid, _) => Lists.Open(uid));

            var actions = Look.El("div", "qkg-study-actions");
            if (listen is object) actions.appendChild(Look.IconButton("▶", "Listen from this verse".t(), () => listen(v)));
            if (readInQuran) actions.appendChild(Look.IconButton("☰", "Read it in its surah".t(), () => QuranLinks.Go(v.VerseKey)));
            actions.appendChild(Look.IconButton("↗", "Open the verse's page".t(), () => Lists.Open(v.Uid)));
            actions.appendChild(Look.IconButton("✕", "Close".t(), () => close()));

            var head = Look.El("div", "qkg-study-head");
            head.appendChild(Look.El("div", "qkg-study-title",
                Look.El("h2", "qkg-h2", v.VerseKey),
                Look.El("span", "qkg-note", s.SurahNumber + " · " + s.SurahName + (v.Page > 0 ? " · " + string.Format("Juz {0} · page {1}".t(), v.Juz, v.Page) : ""))));
            head.appendChild(actions);

            var body = VStack().WS().Gap(22.px()).Class("qkg-study-body");
            var tabs = new[] { "Verse".t(), "Tafsir".t(), "Hadith".t(), "Related".t() };
            void Show(int tab)
            {
                Remember(tab);
                body.Clear();
                switch (tab)
                {
                    case 1: body.Add(TafsirView.Create(v.Uid, heading: false)); break;
                    case 2: body.Add(HadithPart(v)); break;
                    case 3: body.Add(RelatedPart(v, goToVerse)); break;
                    default: body.Add(VersePart(v, s)); break;
                }
            }
            var tabBar = Look.Segmented(tabs, Saved(), Show, "About this verse".t());
            tabBar.classList.add("qkg-study-tabs");

            Show(Saved());

            return VStack().WS().Children(
                Raw(Look.El("div", "qkg-study-top", head, tabBar)),
                body);
        }

        // -----------------------------------------------------------------------------------

        /// <summary>The verse: the Arabic, its translations, its passage and theme, its words.</summary>
        private static IComponent VersePart(QK.ReadingVerse v, QK.ReadingSection s)
        {
            var part = VStack().WS().Gap(20.px());

            part.Add(Raw(Look.El("p", "qkg-study-ar", v.Text)).Style(st => { st.setProperty("direction", "rtl"); }));

            part.Add(Defer(async () =>
            {
                var node = await Mosaik.API.Nodes.GetAsync(v.Uid);
                var t = Look.El("div", "qkg-study-trans");
                var translit = node.GetString(N.Verse.Transliteration);
                if (!string.IsNullOrEmpty(translit)) t.appendChild(Look.El("p", "qkg-study-translit", translit));
                t.appendChild(Translation("Saheeh International", node.GetString(N.Verse.EnglishSaheeh)));
                t.appendChild(Translation("Ali Quli Qarai", node.GetString(N.Verse.EnglishQarai)));
                return Raw(t);
            }, Raw(Translation("Saheeh International", v.Translation))));

            if (UID128.IsNotNull(s.PassageUid))
            {
                var passage = Look.El("div", "qkg-study-passage");
                passage.appendChild(Look.El("span", "qkg-pas-glyph", "◇"));
                passage.appendChild(Look.OnClick(Look.El("span", "qkg-study-passage-name", string.Format("Passage {0} · {1}".t(), s.Ordinal, QuranReader.Range(s))), () => Lists.Open(s.PassageUid)));
                if (!string.IsNullOrEmpty(s.LabelArabic)) passage.appendChild(Look.Ar(s.LabelArabic, "qkg-arw qkg-study-passage-ar"));
                if (UID128.IsNotNull(s.ThemeUid))
                    passage.appendChild(ThemeChip(s.ThemeUid, s.ThemeLabelArabic, string.Format("Theme · {0} surahs".t(), s.ThemeSurahs), Clusters.ColorOf(s.ThemeOrdinal), s.ThemeLabel));
                if (UID128.IsNotNull(s.GroupUid))
                    passage.appendChild(ThemeChip(s.GroupUid, s.GroupLabelArabic, string.Format("similar · {0} surahs".t(), s.GroupSurahs), Clusters.GroupColorOf(s.GroupOrdinal), "Passages close in Arabic meaning, lemmas and roots".t()));
                part.Add(Raw(passage));
            }

            part.Add(Defer(async () =>
            {
                var words = await QuranApi.VerseWordsAsync(v.Uid);
                return VStack().WS().Gap(10.px()).Children(
                    Look.Heading("Word by word".t(), null),
                    WordGrid(words.Words ?? new QK.WordRow[0], 4),
                    Raw(Look.El("p", "qkg-note", "Root, lemma and part of speech from the Quranic Arabic Corpus. Open a word for its segments; a root opens everywhere it occurs.".t())));
            }, Raw(Look.El("p", "qkg-note", "Reading the words…".t()))));

            return part;
        }

        /// <summary>The hadith that quote the verse, then those close to it in meaning.</summary>
        private static IComponent HadithPart(QK.ReadingVerse v)
            => Defer(async () =>
            {
                var hadith  = await QuranApi.VerseHadithAsync(v.Uid, HADITH_COUNT);
                var part    = VStack().WS().Gap(12.px());
                var quoting = hadith.Quoting ?? new QK.HadithRow[0];

                part.Add(Look.Heading("Hadith that quote it".t(), quoting.Length == 0
                    ? "No hadith of Bukhari or Muslim quotes it.".t()
                    : string.Format("{0} hadith of Bukhari and Muslim meet it in words.".t(), quoting.Length)));
                foreach (var h in quoting.Take(HADITH_COUNT)) part.Add(HadithView.Row(h, showVerses: false, compact: true));
                if (quoting.Length > HADITH_COUNT)
                    part.Add(Raw(Look.OnClick(Look.El("span", "qkg-link", string.Format("{0} more on the verse's page →".t(), quoting.Length - HADITH_COUNT)), () => Lists.Open(v.Uid))));

                var closest = hadith.Closest ?? new QK.HadithRow[0];
                if (closest.Length > 0)
                {
                    part.Add(Look.Heading("Close in meaning".t(), "The Arabic of both, one embedding model — candidates to read, not links the text makes.".t()));
                    foreach (var h in closest) part.Add(HadithView.Row(h, showVerses: false, compact: true));
                }
                return part;
            }, Raw(Look.El("p", "qkg-note", "Finding the hadith…".t())));

        /// <summary>The closest verses in the Arabic, then the verses Ibn Kathir relates to it.</summary>
        private static IComponent RelatedPart(QK.ReadingVerse v, Action<UID128, string> goToVerse)
        {
            var part = VStack().WS().Gap(22.px());

            part.Add(Defer(async () =>
            {
                var similar = await QuranApi.SimilarVersesAsync(v.Uid, SIMILAR_COUNT);
                return VStack().WS().Gap(10.px()).Children(
                    Look.Heading("Closest verses".t(), similar.SemanticAvailable
                        ? "Ranked from the Arabic — meaning, shared lemmas, shared roots.".t()
                        : "Ranked by shared lemmas and roots; meaning joins once this verse's Arabic is embedded.".t()),
                    SimilarList(similar.Verses ?? new QK.SimilarVerse[0], goToVerse));
            }, Raw(Look.El("p", "qkg-note", "Ranking the closest verses…".t()))));

            part.Add(Defer(async () =>
            {
                var words   = await QuranApi.VerseWordsAsync(v.Uid);
                var related = (words.TafsirRelated ?? new QK.RelatedVerse[0]).Take(TAFSIR_COUNT).ToArray();
                var list    = VStack().WS().Gap(10.px()).Children(
                    Look.Heading("Related by Ibn Kathir".t(), "The verses his tafsir relates to this one (QurSim) — the reference the similarity is scored against, shown for reading.".t()));
                if (related.Length == 0) list.Add(Raw(Look.El("p", "qkg-note", "None.".t())));
                foreach (var r in related)
                    list.Add(Raw(VerseLine(r.Uid, r.VerseKey, r.Text, Look.Badge(r.Grade, r.Degree == 2 ? "teal" : r.Degree == 1 ? "info" : "plain"), null, goToVerse)));
                return list;
            }, Raw()));

            return part;
        }

        // -----------------------------------------------------------------------------------
        // parts shared with the verse's page
        // -----------------------------------------------------------------------------------

        /// <summary>The words of a verse as tiles, right to left: the word, its root, its part of speech. A tile opens the word.</summary>
        internal static IComponent WordGrid(QK.WordRow[] words, int columns)
        {
            EnsureStyles();
            var grid = Look.El("div", "qkg-words");
            grid.setAttribute("dir", "rtl");
            grid.style.setProperty("grid-template-columns", "repeat(auto-fill, minmax(" + (columns >= 6 ? 96 : 88) + "px, 1fr))");
            foreach (var word in words)
            {
                var w = word;
                var tile = Look.El("div", "qkg-word");
                tile.appendChild(Look.Ar(w.Text, "qkg-arw qkg-word-ar"));
                tile.appendChild(Look.Ar(string.IsNullOrEmpty(w.Root) ? "—" : w.Root, "qkg-arw qkg-word-root"));
                var pos = Look.El("span", "qkg-word-pos", w.PartOfSpeech ?? "");
                pos.setAttribute("dir", "ltr");
                tile.appendChild(pos);
                tile.title = string.Join(" · ", new[] { w.Lemma, w.Segments, w.Features }.Where(x => !string.IsNullOrEmpty(x)));
                grid.appendChild(Look.OnClick(tile, () => Lists.Open(w.Uid)));
            }
            return Raw(grid).WS();
        }

        /// <summary>Ranked verses: key and score, the Arabic on one line, a bar of what tied them (meaning, lemmas, roots), the roots shared.</summary>
        internal static IComponent SimilarList(QK.SimilarVerse[] verses, Action<UID128, string> goToVerse, bool withTranslation = false)
        {
            EnsureStyles();
            var list = Look.El("div", "qkg-similar");
            var max  = Math.Max(1e-9, verses.Select(x => Math.Max(0, x.Semantic) + Math.Max(0, x.Lemmas) + Math.Max(0, x.Roots)).DefaultIfEmpty(0).Max());
            foreach (var other in verses)
            {
                var o = other;
                var bar = Look.El("span", "qkg-similar-bar");
                foreach (var (value, color, name) in new[] { (o.Semantic, Palette.Meaning, "meaning".t()), (o.Lemmas, Palette.Lemma, "lemmas".t()), (o.Roots, Palette.Root, "roots".t()) })
                {
                    if (value <= 0) continue;
                    var part = Look.El("i", "");
                    part.style.width      = (100 * value / max).ToString("0.#") + "%";
                    part.style.background = color;
                    part.title            = name + " " + value.ToString("+0.0;-0.0");
                    bar.appendChild(part);
                }
                bar.setAttribute("role", "img");
                bar.setAttribute("aria-label", string.Format("meaning {0:0.0}, lemmas {1:0.0}, roots {2:0.0}".t(), o.Semantic, o.Lemmas, o.Roots));

                var row = Look.El("div", "qkg-similar-row");
                row.appendChild(Look.El("div", "qkg-similar-key", Look.El("span", "qkg-similar-vkey", o.VerseKey), Look.El("span", "qkg-note", "score".t() + " " + o.Score.ToString("0.0"))));
                var text = Look.El("div", "qkg-similar-text");
                text.appendChild(Look.Ar(o.Text, "qkg-arw qkg-similar-ar" + (withTranslation ? " qkg-similar-ar-full" : "")));
                text.appendChild(bar);
                if (withTranslation && !string.IsNullOrEmpty(o.Translation)) text.appendChild(Look.El("span", "qkg-similar-en", o.Translation));
                if ((o.SharedRoots ?? new string[0]).Length > 0) text.appendChild(Look.Ar(string.Join(" · ", o.SharedRoots), "qkg-arw qkg-similar-roots"));
                row.appendChild(text);
                list.appendChild(Look.OnClick(row, () => goToVerse(o.Uid, o.VerseKey)));
            }
            var keys = Look.El("div", "qkg-keys", Look.Key(Palette.Meaning, "meaning".t()), Look.Key(Palette.Lemma, "lemmas".t()), Look.Key(Palette.Root, "roots".t()));
            var wrap = Look.El("div", "qkg-similar-wrap", list, keys);
            return Raw(wrap).WS();
        }

        // -----------------------------------------------------------------------------------

        private static HTMLElement Translation(string source, string text)
            => Look.El("div", "qkg-study-tr",
                   Look.El("p", "qkg-study-tr-text", text ?? ""),
                   Look.El("span", "qkg-note", source));

        private static HTMLElement ThemeChip(UID128 uid, string labelArabic, string what, string color, string tooltip)
        {
            var chip = Look.El("span", "qkg-chip qkg-chip-out");
            var dot  = Look.El("span", "qkg-dot");
            dot.style.background = color;
            chip.appendChild(dot);
            chip.appendChild(Look.Ar(labelArabic));
            chip.appendChild(Look.El("span", "qkg-note", what));
            chip.title = tooltip ?? "";
            return Look.OnClick(chip, () => Lists.Open(uid));
        }

        private static HTMLElement VerseLine(UID128 uid, string key, string text, HTMLElement badge, string translation, Action<UID128, string> goToVerse)
        {
            var row = Look.El("div", "qkg-similar-row");
            row.appendChild(Look.El("div", "qkg-similar-key", Look.El("span", "qkg-similar-vkey", key), badge));
            var body = Look.El("div", "qkg-similar-text");
            body.appendChild(Look.Ar(text, "qkg-arw qkg-similar-ar qkg-similar-ar-full"));
            if (!string.IsNullOrEmpty(translation)) body.appendChild(Look.El("span", "qkg-similar-en", translation));
            row.appendChild(body);
            return Look.OnClick(row, () => goToVerse(uid, key));
        }

        private const string TAB_KEY = "qkg-study-tab";

        private static int Saved()
        {
            try { return int.TryParse(localStorage.getItem(TAB_KEY), out var t) && t >= 0 && t <= 3 ? t : 0; }
            catch { return 0; }
        }

        private static void Remember(int tab)
        {
            try { localStorage.setItem(TAB_KEY, tab.ToString()); } catch { /* nothing to keep it in */ }
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = CSS.Replace("{FONT}", Arabic.FONT_FAMILY);
            document.head.appendChild(style);
        }

        private const string CSS = @"
.qkg-study-top { position: sticky; top: 0; z-index: 2; display: flex; flex-direction: column; gap: 12px; padding: 18px 24px 14px 24px; background: var(--qkg-bg); border-bottom: 1px solid var(--qkg-line); }
.qkg-study-head { display: flex; align-items: flex-start; gap: 6px; }
.qkg-study-title { flex: 1 1 auto; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.qkg-study-actions { display: inline-flex; gap: 2px; }
.qkg-study-tabs { align-self: flex-start; }
.qkg-study-body .qkg-h2 { font-size: 16px; }
.qkg-study-body { padding: 20px 24px 40px 24px; box-sizing: border-box; color: var(--qkg-ink); }
.qkg-study-ar { font-family: {FONT}; font-size: 28px; line-height: 2.05; text-align: right; color: var(--qkg-ink); margin: 0; }
.qkg-study-trans { display: flex; flex-direction: column; gap: 14px; }
.qkg-study-translit { font-style: italic; font-size: 14px; color: var(--qkg-ink-3); margin: 0; }
.qkg-study-tr { display: flex; flex-direction: column; gap: 4px; }
.qkg-study-tr-text { font-size: 16px; line-height: 1.6; color: var(--qkg-ink); margin: 0; }
.qkg-study-passage { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 12px 14px; border-radius: 14px; background: var(--qkg-surface); }
.qkg-study-passage-name { font-weight: 600; font-size: 14px; }
.qkg-study-passage-name:hover { color: var(--qkg-teal-strong); }
.qkg-study-passage-ar { font-size: 19px; color: var(--qkg-mark); }
.qkg-words { display: grid; gap: 8px; }
.qkg-word { display: flex; flex-direction: column; align-items: center; gap: 2px; padding: 10px 6px 8px 6px; border-radius: 12px; border: 1px solid var(--qkg-line); background: var(--qkg-raised); text-align: center; }
.qkg-word:hover { border-color: var(--qkg-teal); background: var(--qkg-teal-tint); }
.qkg-word-ar { font-size: 26px; line-height: 1.6; color: var(--qkg-ink); }
.qkg-word-root { font-size: 15px; color: var(--qkg-root); }
.qkg-word-pos { font-size: 11px; color: var(--qkg-ink-3); line-height: 1.3; }
.qkg-similar-wrap { display: flex; flex-direction: column; gap: 10px; }
.qkg-similar { display: flex; flex-direction: column; }
.qkg-similar-row { display: grid; grid-template-columns: 64px minmax(0, 1fr); gap: 10px; padding: 10px 6px; border-top: 1px solid var(--qkg-line); border-radius: 8px; color: var(--qkg-ink); }
.qkg-similar-row:hover { background: var(--qkg-surface); }
.qkg-similar-key { display: flex; flex-direction: column; gap: 4px; align-items: flex-start; }
.qkg-similar-vkey { font-weight: 600; font-size: 14px; color: var(--qkg-teal-strong); }
.qkg-similar-text { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.qkg-similar-ar { font-size: 20px; line-height: 1.8; text-align: right; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; display: block; }
.qkg-similar-ar-full { white-space: normal; }
.qkg-similar-en { font-size: 14px; line-height: 1.55; color: var(--qkg-ink-2); }
.qkg-similar-bar { display: flex; height: 6px; border-radius: 999px; overflow: hidden; background: var(--qkg-surface-2); }
.qkg-similar-bar > i { display: block; height: 100%; }
.qkg-similar-roots { font-size: 14px; color: var(--qkg-ink-3); text-align: right; display: block; }
";
    }

    /// <summary>The Quran View's addresses: #/read/2 opens Al-Baqarah, #/read/2/255 opens it on Ayat al-Kursi.</summary>
    internal static class QuranLinks
    {
        internal const string Route = "#/read";

        internal static string For(int surah, int ayah = 0) => Route + "/" + surah + (ayah > 0 ? "/" + ayah : "");

        /// <summary>"2:255" → the Quran View on that verse.</summary>
        internal static void Go(string verseKey)
        {
            var parts = (verseKey ?? "").Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var surah) && int.TryParse(parts[1], out var ayah)) Go(surah, ayah);
        }

        /// <summary>Set before going to a surah: the Quran View starts its recitation when it opens.</summary>
        internal static bool ListenOnArrival { get; set; }

        private const string LAST_KEY = "qkg-last-read";

        /// <summary>Where the reader was last: a surah and a verse of it (0: its start), or (0, 0).</summary>
        internal static (int Surah, int Ayah) LastRead()
        {
            try
            {
                var parts = (localStorage.getItem(LAST_KEY) ?? "").Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var s) && int.TryParse(parts[1], out var a) && s >= 1 && s <= 114) return (s, a);
            }
            catch { /* storage blocked: nothing remembered */ }
            return (0, 0);
        }

        internal static void Remember(int surah, int ayah)
        {
            try { localStorage.setItem(LAST_KEY, surah + ":" + ayah); } catch { /* nothing to keep it in */ }
        }

        internal static void Go(int surah, int ayah = 0)
        {
            // A node page, or a verse opened from the chat, is a modal over the page: reading in the
            // Quran replaces them all.
            VerseReferences.Close();
            ModalStack.Clear();
            Router.Navigate(For(surah, ayah));
        }
    }
}
