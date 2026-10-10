using Tesserae;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// Arabic text drawn as Arabic: right to left, right-aligned, in a typeface that carries the
    /// marks of the Uthmani script (the small high letters, the pause signs, the sukun shapes) and
    /// with the line height the stacked diacritics need. Everything that shows Quranic text goes
    /// through here, so the text looks the same on every page.
    ///
    /// <para>The fonts are Amiri Quran and Amiri (SIL Open Font Licence), requested once from Google
    /// Fonts. Offline, the stack falls back to what the system has: Geeza Pro on macOS, Traditional
    /// Arabic on Windows, Noto Naskh Arabic on Linux.</para>
    /// </summary>
    internal static class Arabic
    {
        internal const string FONT_FAMILY = "'Amiri Quran', 'Amiri', 'Scheherazade New', 'Noto Naskh Arabic', 'Geeza Pro', 'Traditional Arabic', serif";

        private const string FONTS_URL = "https://fonts.googleapis.com/css2?family=Amiri+Quran&family=Amiri:wght@400;700&display=swap";

        private static bool _fontsRequested;

        internal static void EnsureFonts()
        {
            if (_fontsRequested) return;
            _fontsRequested = true;

            var link = document.createElement("link");
            link.setAttribute("rel", "stylesheet");
            link.setAttribute("href", FONTS_URL);
            document.head.appendChild(link);
        }

        /// <summary>A block of Quranic text — a verse, a surah being read.</summary>
        internal static TextBlock Text(string text, int pixelSize = 24)
        {
            EnsureFonts();

            return TextBlock(text ?? "", selectable: true)
               .WS()
               .Style(s =>
               {
                   s.setProperty("direction", "rtl");
                   s.setProperty("text-align", "right");
                   s.setProperty("font-family", FONT_FAMILY);
                   s.setProperty("font-size", pixelSize + "px");
                   s.setProperty("line-height", "2.1");
                   s.setProperty("white-space", "normal");
               });
        }

        /// <summary>A centred line of Arabic: a surah's name, the Bismillah above it.</summary>
        internal static TextBlock Centered(string text, int pixelSize = 28)
        {
            EnsureFonts();

            return TextBlock(text ?? "")
               .WS()
               .Style(s =>
               {
                   s.setProperty("direction", "rtl");
                   s.setProperty("text-align", "center");
                   s.setProperty("font-family", FONT_FAMILY);
                   s.setProperty("font-size", pixelSize + "px");
                   s.setProperty("line-height", "2");
               });
        }

        /// <summary>A word, a root or a name inside a left-to-right line, isolated so it keeps its own direction.</summary>
        internal static TextBlock Inline(string text, int pixelSize = 18)
        {
            EnsureFonts();

            return TextBlock(text ?? "")
               .Style(s =>
               {
                   s.setProperty("direction", "rtl");
                   s.setProperty("unicode-bidi", "isolate");
                   s.setProperty("font-family", FONT_FAMILY);
                   s.setProperty("font-size", pixelSize + "px");
                   s.setProperty("line-height", "1.6");
                   s.setProperty("white-space", "nowrap");
               });
        }
    }
}
