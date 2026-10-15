using System.Collections.Generic;
using System.Linq;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    // The hadith of Bukhari and Muslim and where they meet the Quran, and the tafsirs. LabelField
    // values match src/QuranKG.Definitions/config/schemas/nodes/styles/*.json.

    /// <summary>A hadith: its text, the verses it quotes and the verses closest to it.</summary>
    public sealed class HadithRenderer : QuranRendererBase
    {
        public HadithRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Hadith.Type,
            DisplayName = "Hadith",
            Icon        = UIcons.CommentQuote.ToString(),
            LabelField  = LabelFields.FirstOf(N.Hadith.Reference, N.Hadith.HadithKey),
        }) { ShortDisplayName = "HDTH"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.HadithBook.Type, N.HadithCollection.Type, N.QuranQuote.Type };

        protected override string TileText(Node node) => node.GetString(N.Hadith.CollectionId) == "muslim" ? "MSLM" : "BKHR";

        protected override IComponent CardBody(Node node)
        {
            var matn = node.GetString(N.Hadith.Matn) ?? "";
            return VStack().WS().Children(
                Arabic.Text(matn.Length > 220 ? matn.Substring(0, 220) + " …" : matn, 19),
                TextBlock(Cut(node.GetString(N.Hadith.TextEnglish), 200)).Tiny().Secondary());
        }

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Hadith.BookName);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("hadith", "Hadith".t(), () => HadithView.Page(node.UID));
        }

        internal static string Cut(string text, int length) => string.IsNullOrEmpty(text) || text.Length <= length ? text ?? "" : text.Substring(0, length) + " …";
    }

    /// <summary>A book (kitab) of a collection: its hadith in order, those quoting the Quran marked.</summary>
    public sealed class HadithBookRenderer : QuranRendererBase
    {
        private const int PAGE = 25;

        public HadithBookRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.HadithBook.Type,
            DisplayName = "Hadith book",
            Icon        = UIcons.BookBookmark.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.HadithBook.BookName) + " (" + LabelFields.Field(N.HadithBook.CollectionId) + ")"),
        }) { ShortDisplayName = "BOOK"; }

        protected override string[] Related360Types => new[] { N.Hadith.Type, N.HadithCollection.Type };

        protected override IComponent CardBody(Node node) => Arabic.Inline(node.GetString(N.HadithBook.BookNameArabic), 20);

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return (node.GetString(N.HadithBook.CollectionId) == "muslim" ? "Sahih Muslim" : "Sahih al-Bukhari") + " · " + "book".t() + " " + node.GetInt(N.HadithBook.BookNumber);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("hadith", "Its hadith".t(), () => HadithList(node, "all"));
            yield return Tab("quran",  "Quoting the Quran".t(), () => HadithList(node, "any"));
        }

        private static IComponent HadithList(Node book, string kind)
        {
            var list = VStack().WS().P(16);
            var more = Button("More".t()).Compact();
            var skip = 0;

            void Load()
            {
                more.Collapse();
                list.Add(Defer(async () =>
                {
                    var page = await QuranApi.HadithListAsync(new QK.HadithListRequest { BookUid = book.UID, Kind = kind, Skip = skip, Take = PAGE });
                    var block = VStack().WS();
                    if (skip == 0)
                        block.Add(TextBlock(string.Format(kind == "all" ? "{0} hadith" : "{0} hadith quote a verse or share a phrase with one".t(), page.Total)).Small().Secondary().PB(8));
                    foreach (var row in page.Rows ?? new QK.HadithRow[0]) block.Add(HadithView.Row(row));
                    skip += PAGE;
                    if (skip < page.Total) more.Show();
                    return block;
                }, TextBlock("Reading…".t()).Small().Secondary()));
            }

            more.OnClick(() => { list.Remove(more); Load(); list.Add(more); });
            Load();
            list.Add(more);
            return list;
        }
    }

    /// <summary>Sahih al-Bukhari or Sahih Muslim.</summary>
    public sealed class HadithCollectionRenderer : QuranRendererBase
    {
        public HadithCollectionRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.HadithCollection.Type,
            DisplayName = "Hadith collection",
            Icon        = UIcons.Books.ToString(),
            LabelField  = N.HadithCollection.CollectionName,
        }) { ShortDisplayName = "COLL"; }

        protected override string[] Related360Types => new[] { N.HadithBook.Type, N.Hadith.Type };

        protected override IComponent CardBody(Node node) => Arabic.Inline(node.GetString(N.HadithCollection.CollectionNameArabic), 22);

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.HadithCollection.Compiler);
            yield return "English".t() + ": " + node.GetString(N.HadithCollection.Translator);
        }
    }

    /// <summary>Where a hadith's words meet a verse: a quotation, a shared phrase, or the translator's citation.</summary>
    public sealed class QuranQuoteRenderer : QuranRendererBase
    {
        public QuranQuoteRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.QuranQuote.Type,
            DisplayName = "Quran in a hadith",
            Icon        = UIcons.QuoteRight.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.QuranQuote.HadithKey) + " → " + LabelFields.Field(N.QuranQuote.VerseKey)),
        }) { ShortDisplayName = "QUOT"; }

        protected override string[] Related360Types => new[] { N.Hadith.Type, N.Verse.Type };

        protected override IComponent CardBody(Node node)
        {
            var quoted = node.GetString(N.QuranQuote.Quoted);
            return HStack().WS().AlignItemsCenter().Gap(8.px()).Wrap().Children(
                HadithView.KindBadge(node.GetString(N.QuranQuote.Kind)),
                string.IsNullOrEmpty(quoted) ? (IComponent)TextBlock("") : Arabic.Inline(quoted, 18));
        }

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            if (node.GetString(N.QuranQuote.Kind) != "translator")
                yield return string.Format("{0} of {1} words".t(), node.GetInt(N.QuranQuote.Words), node.GetInt(N.QuranQuote.VerseWords));
        }
    }

    /// <summary>A tafsir in one language — al-Mukhtasar, al-Jalalayn, Ibn Kathir, in Arabic or English.</summary>
    public sealed class TafsirRenderer : QuranRendererBase
    {
        public TafsirRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Tafsir.Type,
            DisplayName = "Tafsir",
            Icon        = UIcons.BookOpenReader.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.Tafsir.TafsirName) + " (" + LabelFields.Field(N.Tafsir.Language) + ")"),
        }) { ShortDisplayName = "TFSR"; }

        protected override string[] Related360Types => new[] { N.TafsirEntry.Type };

        protected override IComponent CardBody(Node node)
            => VStack().WS().Children(
                   Arabic.Inline(node.GetString(N.Tafsir.TafsirNameArabic), 20),
                   TextBlock(node.GetString(N.Tafsir.Description) ?? "").Tiny().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Tafsir.Language) == "ar" ? "Arabic".t() : "English".t();
        }
    }

    /// <summary>One commentary of a tafsir, on a verse or a run of verses.</summary>
    public sealed class TafsirEntryRenderer : QuranRendererBase
    {
        public TafsirEntryRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.TafsirEntry.Type,
            DisplayName = "Tafsir entry",
            Icon        = UIcons.BookOpenCover.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.TafsirEntry.TafsirId) + " " + LabelFields.Field(N.TafsirEntry.FirstVerseKey)),
        }) { ShortDisplayName = "TFSR"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Tafsir.Type };

        protected override IComponent CardBody(Node node)
        {
            var text = HadithRenderer.Cut(node.GetString(N.TafsirEntry.Text), 260);
            return node.GetString(N.TafsirEntry.Language) == "ar" ? (IComponent)Arabic.Text(text, 18) : TextBlock(text).Small().Secondary();
        }

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            var first = node.GetString(N.TafsirEntry.FirstVerseKey);
            var last  = node.GetString(N.TafsirEntry.LastVerseKey);
            yield return first == last ? first : first + "–" + node.GetInt(N.TafsirEntry.LastAyah);
        }
    }
}
