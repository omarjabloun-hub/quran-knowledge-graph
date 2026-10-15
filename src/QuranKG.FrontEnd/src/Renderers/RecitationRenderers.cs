using System.Collections.Generic;
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
    // The reciters and their recited surahs. LabelField values match
    // src/QuranKG.Definitions/config/schemas/nodes/styles/*.json.

    /// <summary>A reciter and the reading he recites in.</summary>
    public sealed class ReciterRenderer : QuranRendererBase
    {
        public ReciterRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Reciter.Type,
            DisplayName = "Reciter",
            Icon        = UIcons.MicrophoneAlt.ToString(),
            LabelField  = LabelFields.FirstOf(N.Reciter.ReciterName, N.Reciter.ReciterId),
        }) { ShortDisplayName = "RCTR"; }

        protected override string[] Related360Types => new[] { N.Recitation.Type, N.Surah.Type };

        protected override IComponent CardBody(Node node)
            => VStack().WS().Children(
                   Arabic.Inline(node.GetString(N.Reciter.ReciterNameArabic), 22),
                   Arabic.Inline(node.GetString(N.Reciter.RiwayaArabic), 17).Foreground(Theme.Colors.Neutral600),
                   TextBlock(node.GetString(N.Reciter.Attribution) ?? "").Tiny().Secondary());

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return node.GetString(N.Reciter.Riwaya);
            yield return node.GetString(N.Reciter.Country);
            yield return node.GetString(N.Reciter.Style);
        }
    }

    /// <summary>One surah recited: open it in the Quran View, playing, the text following the verse.</summary>
    public sealed class RecitationRenderer : QuranRendererBase
    {
        public RecitationRenderer() : base(new SchemaStyleInfo
        {
            Name        = N.Recitation.Type,
            DisplayName = "Recitation",
            Icon        = UIcons.ListMusic.ToString(),
            LabelField  = N.Recitation.RecitationKey,
        }) { ShortDisplayName = "RCTN"; }

        protected override string[] Related360Types => new[] { N.Surah.Type, N.Reciter.Type };

        protected override IComponent CardBody(Node node)
            => TextBlock(string.Format("Surah {0} · {1} · {2} verses in the recording's own count".t(),
                                       node.GetInt(N.Recitation.SurahNumber), Clock(node.GetInt(N.Recitation.DurationMs)), node.GetInt(N.Recitation.RecordedVerses))).Small().Secondary();

        protected override IEnumerable<string> FooterFacts(Node node)
        {
            yield return Clock(node.GetInt(N.Recitation.DurationMs));
            var estimated = (node.GetString(N.Recitation.EstimatedAyahs) ?? "").Split(' ');
            var count     = string.IsNullOrWhiteSpace(node.GetString(N.Recitation.EstimatedAyahs)) ? 0 : estimated.Length;
            if (count > 0) yield return string.Format("{0} verses timed by estimate".t(), count);
        }

        protected override IEnumerable<NodeTab> LeadingTabs(Node node)
        {
            yield return Tab("listen", "Listen".t(), () =>
            {
                var surah     = node.GetInt(N.Recitation.SurahNumber);
                var estimated = node.GetString(N.Recitation.EstimatedAyahs) ?? "";
                return VStack().WS().P(16).Gap(10.px()).Children(
                    TextBlock(string.Format("Surah {0}, recited — {1}.".t(), surah, Clock(node.GetInt(N.Recitation.DurationMs)))).SemiBold(),
                    TextBlock("Every verse of the text has its place in the recording: the reader marks the verse being recited and keeps it in view.".t()).Small().Secondary(),
                    TextBlock(string.IsNullOrWhiteSpace(estimated)
                                  ? "Every verse starts where a verse of the recording starts: all the times are the recording's own.".t()
                                  : string.Format("The recording counts the verses its own way ({0} here): verses {1} start inside one of its verses, and their start is estimated from the letters before them.".t(), node.GetInt(N.Recitation.RecordedVerses), estimated.Replace(" ", ", "))).Small().Secondary(),
                    HStack().Children(Button("Listen in the Quran View".t()).SetIcon(UIcons.PlayAlt).Primary().OnClick(() =>
                    {
                        QuranLinks.ListenOnArrival = true;
                        QuranLinks.Go(surah);
                    })));
            });
        }

        private static string Clock(int milliseconds)
        {
            var s = milliseconds / 1000;
            return s >= 3600 ? string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60) : string.Format("{0}:{1:00}", s / 60, s % 60);
        }
    }
}
