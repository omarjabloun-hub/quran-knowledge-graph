using System.Linq;
using QuranKG.FrontEnd.Api;
using Tesserae;
using static Tesserae.UI;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>The colours that carry meaning across every chart: a Meccan bar is the same orange everywhere.</summary>
    internal static class Palette
    {
        internal static string Meccan  => Theme.Colors.Orange600;
        internal static string Medinan => Theme.Colors.Teal600;
        internal static string Root    => Theme.Colors.Yellow700;
        internal static string Lemma   => Theme.Colors.Purple600;
        internal static string Noun    => Theme.Colors.Blue600;
        internal static string Verb    => Theme.Colors.Magenta600;
        internal static string Other   => Theme.Colors.Neutral500;

        internal static string ForPlace(string place) => place == QK.MEDINAN ? Medinan : Meccan;
    }

    /// <summary>The page furniture: titled panels, rows of them that wrap on a narrow screen, numbers.</summary>
    internal static class Panels
    {
        /// <summary>A titled card: what it shows, one sentence on how to read it, then the content.</summary>
        internal static IComponent Panel(string title, string note, IComponent content)
        {
            var body = VStack().WS().Children(TextBlock(title).SemiBold().MediumPlus());
            if (!string.IsNullOrEmpty(note)) body.Add(TextBlock(note).Small().Secondary().PT(2));
            body.Add(content.WS().PT(12));

            return Card(body).WS();
        }

        /// <summary>Panels side by side, wrapping under each other when there is no room.</summary>
        internal static IComponent Row(params IComponent[] panels)
        {
            var row = HStack().WS().Wrap().Gap(16.px()).AlignItems(ItemAlign.Start);
            foreach (var panel in panels) row.Add(panel.Grow().MinWidth(340.px()).Style(s => s.setProperty("flex-basis", "0")));
            return row;
        }

        /// <summary>A section heading between panels.</summary>
        internal static IComponent Heading(string title, string note = null)
        {
            var heading = VStack().WS().PT(24).PB(8).Children(TextBlock(title).SemiBold().Large());
            if (!string.IsNullOrEmpty(note)) heading.Add(TextBlock(note).Small().Secondary().PT(2));
            return heading;
        }

        internal static string Number(int value)    => value.ToString("N0");
        internal static string Number(double value) => value.ToString("N1");

        /// <summary>"12" → "١٢", for verse numbers inside Arabic text.</summary>
        internal static string ArabicDigits(int value)
            => new string(value.ToString().Select(c => c >= '0' && c <= '9' ? (char)('٠' + (c - '0')) : c).ToArray());
    }
}
