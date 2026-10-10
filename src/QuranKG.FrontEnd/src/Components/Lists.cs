using System;
using System.Linq;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using QuranKG.FrontEnd.Api;
using Tesserae;
using TNT;
using UID;
using static Mosaik.UI;
using static Tesserae.UI;
using static TNT.T;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// The list shapes every page shares. Each row that counts a node opens that node: a count is
    /// half a question, and the node it counted is the other half.
    /// </summary>
    internal static class Lists
    {
        /// <summary>
        /// A ranked list drawn as bars: the label (Arabic when <paramref name="arabicLabels"/>), a bar
        /// proportional to the top count, and the number. Clicking the label opens the node; the
        /// row's Detail is its tooltip.
        /// </summary>
        internal static IComponent RankedBars(QK.Counted[] rows, string color, bool arabicLabels = true, Func<QK.Counted, string> valueText = null, Func<QK.Counted, double> value = null)
        {
            var list = VStack().WS().Gap(2.px());
            if (rows is null || rows.Length == 0) return TextBlock("Nothing to show.".t()).Small().Secondary();

            value     = value     ?? (r => r.Count);
            valueText = valueText ?? (r => r.Count.ToString("N0"));

            var max = rows.Max(r => value(r));
            if (max <= 0) max = 1;

            foreach (var row in rows)
            {
                var captured = row;

                IComponent label = arabicLabels
                    ? (IComponent)Arabic.Inline(captured.Label, 17).OnClick((_, __) => Open(captured.Uid)).Style(s => s.setProperty("cursor", "pointer"))
                    : TextBlock(captured.Label).Small().NoWrap().Ellipsis().OnClick((_, __) => Open(captured.Uid)).Style(s => s.setProperty("cursor", "pointer"));

                var bar = ContributionBar().Decimals(0)
                   .Max(max)
                   .Add(captured.Label, Math.Max(0, value(captured)), color)
                   .HideLegend()
                   .Thickness(10.px());

                var line = HStack().WS().AlignItemsCenter().Gap(8.px()).Children(
                    label.W(110.px()).NoShrink(),
                    bar.Grow(),
                    TextBlock(valueText(captured)).Small().TextRight().W(64.px()).NoShrink());

                list.Add(string.IsNullOrEmpty(captured.Detail) ? line : line.Tooltip(captured.Detail));
            }

            return list;
        }

        /// <summary>
        /// A verse as a row: its citation and whatever ranked it, the Arabic, then the gloss. The
        /// whole card opens the verse.
        /// </summary>
        internal static IComponent VerseRow(UID128 uid, string verseKey, string text, string translation, params IComponent[] badges)
        {
            var header = HStack().WS().AlignItemsCenter().Gap(6.px()).Wrap().Children(TextBlock(verseKey).SemiBold());
            foreach (var badge in badges ?? new IComponent[0]) header.Add(badge);

            return Card(VStack().WS().Children(
                        header,
                        Arabic.Text(text, 22).PT(4),
                        TextBlock(translation ?? "").Small().Secondary()))
                   .OnClick(() => Open(uid))
                   .MB(8);
        }

        /// <summary>A modal listing exactly these nodes, each with its own renderer — the set something counted.</summary>
        internal static void ShowNodes(string title, UID128[] uids)
        {
            var area = SearchArea()
               .WithFacets()
               .SearchFor(new SearchRequest().WithTargetUIDs(uids ?? new UID128[0]))
               .S();

            ShowModal(title, area);
        }

        /// <summary>A modal searching every node of one type, with facets and a search box.</summary>
        internal static void ShowNodesOfType(string title, string nodeType)
        {
            var area = SearchArea()
               .WithFacets()
               .OnSearch(sr => sr.WithTargetQuery(Mosaik.API.Query.StartAt(nodeType).TakeAll()))
               .S();

            ShowModal(title, area);
        }

        internal static void Open(UID128 uid)
        {
            if (uid == default(UID128)) return;
            NodePreview.For(uid, ignoreNotFound: true);
        }

        private static void ShowModal(string title, IComponent content)
        {
            var modal = Modal(TextBlock(title).SemiBold().Large())
               .Width(88.vw())
               .Height(86.vh())
               .LightDismiss()
               .ShowCloseButton();

            modal.Content = content;
            modal.Show();
        }
    }
}
