using System;
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
    /// How a hadith is drawn wherever it appears — the hadith page, a verse's panel, the Hadith and
    /// the Quran page — and the hadith page itself: its text, the verses it quotes, the verses
    /// closest to it. The relation (does the Quran support it, does it explain or add to a verse,
    /// is there an apparent tension) is the assistant's to argue, and a reader's to judge: these
    /// pages show the evidence.
    /// </summary>
    internal static class HadithView
    {
        internal static string Color => Theme.Colors.Teal700;

        /// <summary>How a hadith meets a verse, as a badge: quotation, shared phrase, the translator's citation.</summary>
        internal static IComponent KindBadge(string kind)
        {
            switch (kind)
            {
                case "quotation":  return Badge("quotes it".t()).Success().Tooltip("The verse's words in the hadith's Arabic: set in braces by the edition, six words or more, or the whole of a short verse".t());
                case "phrase":     return Badge("shares a phrase".t()).Warning().Tooltip("Four to seven words of the verse, unmarked — weaker evidence: a formula in the Quran's words may be shared by many hadith".t());
                case "translator": return Badge("translator's citation".t()).Neutral().Tooltip("The English translator cites the verse in brackets; the Arabic does not quote it".t());
                default:           return Badge("close in meaning".t()).Info().Tooltip("Ranked by meaning and shared words — a candidate to read, not a link the text makes".t());
            }
        }

        /// <summary>A hadith in a list: its reference and book, how it meets the Quran, the opening of its text in Arabic and English. Opens its page.</summary>
        internal static IComponent Row(QK.HadithRow h, bool showVerses = true)
        {
            var header = HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().Children(
                TextBlock(h.Reference).SemiBold(),
                TextBlock(h.BookName ?? "").Tiny().Secondary());

            if (!string.IsNullOrEmpty(h.Kind) || h.Score > 0) header.Add(KindBadge(h.Kind));
            if (h.Score > 0) header.Add(TextBlock(string.Format("cosine {0:0.00}".t(), h.Score)).Tiny().Secondary());
            if (showVerses)
                foreach (var key in (h.VerseKeys ?? new string[0]).Take(6))
                {
                    var captured = key;
                    header.Add(TextBlock(captured).Tiny().Foreground(Theme.Primary.Background)
                                  .Style(s => s.setProperty("cursor", "pointer"))
                                  .OnClick((_, e) => { e.stopPropagation(); VerseReferences.Open(captured); }));
                }

            return Card(VStack().WS().Gap(4.px()).Children(
                        header,
                        Arabic.Text(h.Matn ?? "", 19),
                        TextBlock(h.English ?? "").Small().Secondary()))
                   .OnClick(() => Lists.Open(h.Uid))
                   .MB(8);
        }

        // -----------------------------------------------------------------------------------
        // the hadith page
        // -----------------------------------------------------------------------------------

        internal static IComponent Page(UID128 hadith)
            => Defer(async () => Render(await QuranApi.HadithProfileAsync(hadith)),
                     TextBlock("Reading the hadith…".t()).Secondary().P(16));

        private static IComponent Render(QK.HadithProfileDto h)
        {
            var nav = HStack().AlignItemsCenter().Gap(4.px());
            var previous = Button().SetIcon(UIcons.AngleLeft).Compact().Tooltip("The hadith before it in its book".t());
            var next     = Button().SetIcon(UIcons.AngleRight).Compact().Tooltip("The hadith after it in its book".t());
            if (UID128.IsNotNull(h.Previous)) previous.OnClick(() => Lists.Open(h.Previous)); else previous.Disabled();
            if (UID128.IsNotNull(h.Next))     next.OnClick(() => Lists.Open(h.Next));         else next.Disabled();
            nav.Children(previous, next);

            var where = HStack().WS().AlignItemsCenter().Gap(10.px()).Wrap().Children(
                Arabic.Inline(h.CollectionNameArabic, 20),
                TextBlock(h.Reference).SemiBold().MediumPlus(),
                Link(h.BookName, () => Lists.Open(h.BookUid)),
                Arabic.Inline(h.BookNameArabic, 17).Foreground(Theme.Colors.Neutral600),
                TextBlock(h.NumberInBook > 0 ? string.Format("hadith {0} of the book".t(), h.NumberInBook) : "").Tiny().Secondary().Grow(),
                nav);

            var page = VStack().WS().P(16).Gap(16.px()).Children(where);

            if (h.IsVariant)
                page.Add(TextBlock("Only a chain of narrators pointing to the hadith before it (\"with this chain, the same\"): its text is that hadith's.".t()).Small().Secondary());

            // The text, without its chain; the whole Arabic, chain and all, a click away.
            var withChain = Arabic.Text(h.TextArabic, 20).Foreground(Theme.Colors.Neutral600);
            withChain.Collapse();
            var toggle = Button("With its chain of narrators".t()).SetIcon(UIcons.Link).Compact();
            var chainShown = false;
            toggle.OnClick(() => { chainShown = !chainShown; if (chainShown) withChain.Show(); else withChain.Collapse(); });

            page.Add(Panels.Panel("The hadith".t(),
                "Its text in Arabic without the chain of narrators (cut where the chain ends — a heuristic), then the English. The full Arabic is a click away.".t(),
                VStack().WS().Gap(10.px()).Children(
                    Arabic.Text(h.Matn, 26),
                    HStack().Children(toggle),
                    withChain,
                    TextBlock(h.TextEnglish ?? "", selectable: true).Small())));

            // The verses it quotes.
            var quotes = VStack().WS().Gap(4.px());
            var quoteRows = h.Quotes ?? new QK.HadithQuoteRow[0];
            if (quoteRows.Length == 0) quotes.Add(TextBlock("Its Arabic quotes no verse, and its translator cites none.".t()).Small().Secondary());
            foreach (var q in quoteRows)
            {
                var badges = new System.Collections.Generic.List<IComponent> { KindBadge(q.Kind) };
                if (q.Kind != "translator") badges.Add(TextBlock(string.Format("{0} of its {1} words".t(), q.Words, q.VerseWords)).Tiny().Secondary());
                var row = VerseLine(q.VerseKey, q.VerseText, q.Translation, badges.ToArray());
                if (!string.IsNullOrEmpty(q.Quoted)) row.Add(Arabic.Text("«" + q.Quoted + "»", 17).Foreground(Theme.Colors.Green700));
                quotes.Add(row);
            }
            page.Add(Panels.Panel("The Quran in it".t(),
                "Verses its Arabic quotes (set in braces by the edition, six words or more, or a whole short verse), shares a phrase with (four or five words), or that only its English translator cites. Found by matching the Arabic: the detector finds 94% of the hadith Bukhari's translator gives a verse for, 82% of Muslim's.".t(),
                quotes));

            // The verses closest to it.
            var closest = VStack().WS().Gap(4.px());
            if (!h.SemanticReady)
                closest.Add(TextBlock("Its text is not embedded yet: ranked by shared words alone.".t()).Tiny().Secondary());
            foreach (var v in h.Closest ?? new QK.HadithVerseRow[0])
            {
                var badges = new System.Collections.Generic.List<IComponent>
                {
                    Badge(string.Format("score {0:0.0}".t(), v.Score)).Primary(),
                    TextBlock(string.Format("meaning {0:+0.0;-0.0} · words {1:+0.0;-0.0}".t(), v.Semantic, v.Lexical)).Tiny().Secondary(),
                };
                if (v.Quoted) badges.Add(KindBadge("quotation"));
                var row = VerseLine(v.VerseKey, v.Text, v.Translation, badges.ToArray());
                if ((v.Shared ?? new string[0]).Length > 0)
                    row.Add(Arabic.Inline(string.Join(" · ", v.Shared), 15).Foreground(Palette.Lemma).Tooltip("Words of the hadith the verse uses too".t()));
                closest.Add(row);
            }
            page.Add(Panels.Panel("Verses closest to it".t(),
                "Ranked from the Arabic: the meaning of its text against each verse's (one embedding model for both), plus the hadith's distinctive words the verse uses. Candidates to read — closeness is not agreement. Measured on the 85 hadith whose translator cites a verse their Arabic does not quote: that verse is in this top 10 for 28% of them (chance: 0.2%).".t(),
                closest));

            page.Add(TextBlock("Does the Quran support this hadith, does it explain or add to a verse, or is there an apparent tension? Ask the assistant: it reads the hadith, these verses and their context, and argues each case with the evidence — a reading to weigh, not a ruling.".t()).Small().Secondary());

            return page;
        }

        /// <summary>A verse as a block: its key (opens it in its surah), badges, the Arabic, the gloss.</summary>
        private static Stack VerseLine(string key, string text, string translation, params IComponent[] badges)
        {
            var header = HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().Children(Link(key, () => VerseReferences.Open(key)));
            foreach (var b in badges) header.Add(b);
            return VStack().WS().PB(10).Children(
                header,
                Arabic.Text(text ?? "", 21),
                TextBlock(translation ?? "").Small().Secondary());
        }

        private static IComponent Link(string text, Action onClick)
            => TextBlock(text ?? "").Small().Foreground(Theme.Primary.Background)
                                    .OnClick((_, __) => onClick())
                                    .Style(st => st.setProperty("cursor", "pointer"));
    }
}
