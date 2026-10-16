using System.Collections.Generic;
using System.Linq;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Components;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Renderers
{
    // The readings and their verses. LabelField values match
    // src/QuranKG.Definitions/config/schemas/nodes/styles/*.json.

    /// <summary>A reading (riwaya): Qalun, the main one, or Hafs, the graph's own text.</summary>
    public sealed class RiwayaRenderer : QuranRendererBase
    {
        public RiwayaRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Riwaya.Type,
            DisplayName = "Reading",
            Icon        = UIcons.BookOpenCover.ToString(),
            LabelField  = LabelFields.FirstOf(N.Riwaya.RiwayaName, N.Riwaya.RiwayaId),
        }) { ShortDisplayName = "RWY"; }

        protected override string[] Related360Types => new[] { N.RiwayaVerse.Type };

        protected override IComponent CardBody(Node node)
            => VStack().WS().Children(
                   Arabic.Inline(node.GetString(N.Riwaya.RiwayaNameArabic), 22),
                   TextBlock(node.GetString(N.Riwaya.Description) ?? "").Small(),
                   TextBlock(node.GetString(N.Riwaya.Attribution) ?? "").Tiny().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            if (node.GetInt(N.Riwaya.Rank) == 1) yield return "The main reading".t();
            yield return string.Format("{0} count".t(), node.GetString(N.Riwaya.VerseCount));
            yield return node.GetString(N.Riwaya.Region);
        }
    }

    /// <summary>A verse of a reading other than Hafs, in its own numbering, and the Hafs verses its words are in.</summary>
    public sealed class RiwayaVerseRenderer : QuranRendererBase
    {
        public RiwayaVerseRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.RiwayaVerse.Type,
            DisplayName = "Verse in a reading",
            Icon        = UIcons.BookOpenReader.ToString(),
            LabelField  = N.RiwayaVerse.RiwayaVerseKey,
        }) { ShortDisplayName = "RVRS"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Surah.Type, N.Riwaya.Type };

        protected override IComponent CardBody(Node node)
        {
            Arabic.EnsureFonts();
            var text = Arabic.Text(node.GetString(N.RiwayaVerse.Text), 24);
            text.Style(s => s.setProperty("font-family", Arabic.FontFor(node.GetString(N.RiwayaVerse.RiwayaId))));
            return VStack().WS().Gap(6.px()).Children(
                text,
                TextBlock(string.Format("In Hafs: {0}".t(), (node.GetString(N.RiwayaVerse.HafsVerseKeys) ?? "").Replace(" ", ", "))).Small().Secondary());
        }

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.RiwayaVerse.VerseKey);
            yield return string.Format("Page {0} · Juz {1}".t(), node.GetInt(N.RiwayaVerse.Page), node.GetInt(N.RiwayaVerse.Juz));
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("read", "Read".t(), () =>
            {
                var surah = node.GetInt(N.RiwayaVerse.SurahNumber);
                var hafs  = (node.GetString(N.RiwayaVerse.HafsVerseKeys) ?? "").Split(' ').FirstOrDefault() ?? "";
                var ayah  = hafs.Contains(":") && int.TryParse(hafs.Substring(hafs.IndexOf(':') + 1), out var a) ? a : 0;
                return VStack().WS().P(16).Gap(10.px()).Children(
                    TextBlock(string.Format("{0} in its surah, in its reading.".t(), node.GetString(N.RiwayaVerse.VerseKey))).SemiBold(),
                    HStack().Children(Button("Read it in the Quran View".t()).SetIcon(UIcons.BookOpenReader).Primary().OnClick(() => QuranLinks.Go(surah, ayah))));
            });
        }
    }
}
