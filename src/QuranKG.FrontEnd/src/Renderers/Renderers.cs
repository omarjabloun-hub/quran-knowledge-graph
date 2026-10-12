using System.Collections.Generic;
using System.Linq;
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
    // The smaller node types. Every type the connector writes has a renderer — one without a
    // renderer has no label and no icon and shows as a raw UID wherever it is reachable. The
    // Surah, Verse, Root and Cluster renderers, which carry pages of their own, have files of
    // their own.
    //
    // LabelField values match src/QuranKG.Definitions/config/schemas/nodes/styles/*.json.

    /// <summary>One word of the text, with the morphology the Quranic Arabic Corpus gives it.</summary>
    public sealed class WordRenderer : QuranRendererBase
    {
        public WordRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Word.Type,
            DisplayName = "Word",
            Icon        = UIcons.Text.ToString(),
            LabelField  = N.Word.TextArabic,
        }) { ShortDisplayName = "WORD"; }

        // Words are 77,429 of the graph's nodes: the Connected tab lists the few kinds a word touches.
        protected override string[] Related360Types => new[] { N.Verse.Type, N.Surah.Type, N.Root.Type, N.Lemma.Type };

        protected override IComponent CardBody(Node node)
            => HStack().WS().AlignItemsCenter().Gap(12.px()).Children(
                   Arabic.Inline(node.GetString(N.Word.TextArabic), 26),
                   TextBlock(node.GetString(N.Word.Segments)).Tiny().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Word.VerseKey) + " · " + "word".t() + " " + node.GetInt(N.Word.Position);
            yield return node.GetString(N.Word.PartOfSpeech);
            yield return Spaced(node.GetString(N.Word.RootLetters));
            yield return node.GetString(N.Word.Features);
        }

        internal static string Spaced(string letters) => string.IsNullOrEmpty(letters) ? null : string.Join(" ", letters.ToCharArray().Select(c => c.ToString()));
    }

    /// <summary>A dictionary headword: every inflected word of the text points at one.</summary>
    public sealed class LemmaRenderer : QuranRendererBase
    {
        public LemmaRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Lemma.Type,
            DisplayName = "Lemma",
            Icon        = UIcons.LetterCase.ToString(),
            LabelField  = N.Lemma.LemmaText,
        }) { ShortDisplayName = "LEM"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Surah.Type, N.Root.Type, N.Word.Type };

        protected override IComponent CardBody(Node node) => Arabic.Inline(node.GetString(N.Lemma.LemmaText), 24);

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Lemma.PartOfSpeech);
            yield return WordRenderer.Spaced(node.GetString(N.Lemma.RootLetters));
        }
    }

    /// <summary>One of the thirty parts the text is divided into for recitation.</summary>
    public sealed class JuzRenderer : QuranRendererBase
    {
        public JuzRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Juz.Type,
            DisplayName = "Juz",
            Icon        = UIcons.Layers.ToString(),
            LabelField  = LabelFields.Template("Juz " + LabelFields.Field(N.Juz.Number)),
        }) { ShortDisplayName = "JUZ"; }

        protected override string[] Related360Types => new[] { N.Surah.Type, N.Verse.Type };

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return "from".t() + " " + node.GetString(N.Juz.FirstVerseKey);
        }
    }

    /// <summary>Where a surah was revealed, by the traditional attribution: Meccan or Medinan.</summary>
    public sealed class RevelationRenderer : QuranRendererBase
    {
        public RevelationRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Revelation.Type,
            DisplayName = "Revelation",
            Icon        = UIcons.MosqueMoon.ToString(),
            LabelField  = N.Revelation.PlaceName,
        }) { ShortDisplayName = "REV"; }

        protected override string[] Related360Types => new[] { N.Surah.Type, N.Verse.Type };

        // Meccan and Medinan are told apart in a mixed list by the tile alone.
        protected override string TileText(Node node)
        {
            var place = node.GetString(N.Revelation.PlaceName) ?? "";
            return place.Length >= 3 ? place.Substring(0, 3).ToUpperInvariant() : ShortDisplayName;
        }
    }

    /// <summary>A theme from the QUL topic index; verses carry their topic and every ancestor of it.</summary>
    public sealed class TopicRenderer : QuranRendererBase
    {
        public TopicRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Topic.Type,
            DisplayName = "Topic",
            Icon        = UIcons.Hashtag.ToString(),
            LabelField  = LabelFields.FirstOf(N.Topic.TopicName, N.Topic.TopicNameArabic, N.Topic.TopicId),
        }) { ShortDisplayName = "TOPIC"; }

        protected override string[] Related360Types => new[] { N.Verse.Type, N.Topic.Type };

        protected override IComponent CardBody(Node node)
        {
            var description = node.GetString(N.Topic.Description);
            return string.IsNullOrEmpty(description) ? null : TextBlock(description).Tiny().Secondary();
        }

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Topic.TopicNameArabic);
            yield return node.GetString(N.Topic.TopicFamily);
        }
    }

    /// <summary>
    /// Two verses Ibn Kathir's tafsir relates (QurSim), with the grade it gives the relation. The
    /// answer key of the similarity work: people may read it, no AI tool may.
    /// </summary>
    public sealed class QurSimPairRenderer : QuranRendererBase
    {
        public QurSimPairRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.QurSimPair.Type,
            DisplayName = "Tafsir link",
            Icon        = UIcons.Link.ToString(),
            LabelField  = LabelFields.Template(LabelFields.Field(N.QurSimPair.VerseA) + " ~ " + LabelFields.Field(N.QurSimPair.VerseB)),
        }) { ShortDisplayName = "TAF"; }

        protected override string[] Related360Types => new[] { N.Verse.Type };

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            switch (node.GetInt(N.QurSimPair.Degree))
            {
                case 2:  yield return "strongly related".t(); break;
                case 1:  yield return "related".t(); break;
                default: yield return "relation not obvious".t(); break;
            }
        }
    }
}
