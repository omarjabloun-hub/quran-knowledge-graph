using System;
using System.Collections.Generic;
using System.Linq;
using Mosaik;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Themes
{
    /// <summary>
    /// The passages at a glance: one row per surah, one block per passage as wide as its words, in
    /// the colour of its group — its THEME (qkg/clusters/build, shared distinctive words) or its
    /// SIMILARITY GROUP (qkg/clusters/similar-build, Arabic meaning + lemmas + roots), whichever the
    /// page shows; grey where it has none. Hovering a block lights its group across the whole Quran;
    /// clicking pins it and says what it is, with the way to read it. The page's filter lights the
    /// groups it matches.
    /// </summary>
    internal sealed class ClusterMap : IComponent
    {
        internal enum Colouring { Themes, Groups }

        private const int FOLD_AFTER = 24;

        private readonly IComponent  _content;
        private readonly HTMLElement _grid;
        private readonly Stack       _info;
        private readonly Colouring   _colouring;
        private readonly Dictionary<int, List<HTMLElement>> _byGroup  = new Dictionary<int, List<HTMLElement>>();
        private readonly Dictionary<int, List<HTMLElement>> _labelsOf = new Dictionary<int, List<HTMLElement>>();
        private readonly List<HTMLElement> _lit = new List<HTMLElement>();
        private readonly Dictionary<int, QK.MapGroup> _groups;

        private QK.MapPassage _pinned;
        private HashSet<int>  _filter;

        public ClusterMap(QK.ClusterMapDto map, Colouring colouring)
        {
            QuranReader.EnsureStyles();
            Arabic.EnsureFonts();

            _colouring = colouring;
            _groups    = ((colouring == Colouring.Themes ? map.Themes : map.Groups) ?? new QK.MapGroup[0]).ToDictionary(t => t.Ordinal);
            _grid      = Div(Att("qkg-map"));
            _info      = VStack().WS();

            var bySurah = (map.Passages ?? new QK.MapPassage[0]).GroupBy(p => p.SurahNumber).ToDictionary(g => g.Key, g => g.ToArray());

            var folded = new List<HTMLElement>();
            foreach (var surah in map.Surahs ?? new QK.MapSurah[0])
            {
                var number = surah.Number;
                var label  = Div(Att("qkg-map-label"));
                if (number > FOLD_AFTER) folded.Add(label);
                label.appendChild(Text("span", "", number + ". " + surah.Name));
                var ar = Text("span", "qkg-map-label-ar", surah.NameArabic);
                ar.setAttribute("dir", "rtl");
                label.appendChild(ar);
                label.title   = "Read the surah".t();
                label.onclick = _ => QuranLinks.Go(number);
                _grid.appendChild(label);

                var strip = Div(Att("qkg-strip"));
                foreach (var p in bySurah.TryGetValue(number, out var ps) ? ps : new QK.MapPassage[0])
                {
                    var passage = p;
                    var seg     = Div(Att("qkg-strip-seg"));
                    seg.style.flex       = Math.Max(1, p.Words) + " 1 0";
                    seg.style.background = ColorOf(OrdinalOf(p));
                    seg.title            = Describe(p);
                    seg.onmouseenter     = _ => { if (_pinned is null) Light(passage, seg); };
                    seg.onmouseleave     = _ => { if (_pinned is null) Restore(); };
                    seg.onclick          = _ => Pin(passage, seg);
                    strip.appendChild(seg);

                    if (OrdinalOf(p) > 0)
                    {
                        Add(_byGroup, OrdinalOf(p), seg);
                        Add(_labelsOf, OrdinalOf(p), label);
                    }
                }
                _grid.appendChild(strip);
                if (number > FOLD_AFTER) folded.Add(strip);
            }

            // The first surahs at once; the rest a click away (the map is 114 rows).
            HTMLElement more = null;
            if (folded.Count > 0)
            {
                foreach (var el in folded) el.style.display = "none";
                var open = false;
                more = Look.Button(string.Format("Show all {0} surahs".t(), (map.Surahs ?? new QK.MapSurah[0]).Length), null, "plain", "↓");
                more.onclick = _ =>
                {
                    open = !open;
                    foreach (var el in folded) el.style.display = open ? "" : "none";
                    more.querySelector("span:last-child").As<HTMLElement>().textContent = open ? "Show fewer".t() : string.Format("Show all {0} surahs".t(), (map.Surahs ?? new QK.MapSurah[0]).Length);
                };
            }

            ShowHint();

            var legend = colouring == Colouring.Themes
                ? "Each row is a surah and each block one of its passages, as wide as its words. Blocks of one colour belong to one theme; grey blocks share their subject with no other surah. Hover a block to light its theme across the Quran, click to pin it.".t()
                : "Each row is a surah and each block one of its passages, as wide as its words. Blocks of one colour belong to one similarity group; grey blocks are in none. Hover a block to light its group across the Quran, click to pin it.".t();

            var keys = Look.El("div", "qkg-keys",
                Look.Key("var(--qkg-teal)", colouring == Colouring.Themes ? "a theme, lit".t() : "a group, lit".t()),
                Look.Key(Clusters.ColorOf(3), colouring == Colouring.Themes ? "other themes, one colour each".t() : "other groups, one colour each".t()),
                Look.Key(Clusters.Unthemed, colouring == Colouring.Themes ? "a subject no other surah shares".t() : "in no group".t()));
            keys.title = legend;

            var foot = Look.El("div", "qkg-keys", keys);
            if (more is object) { foot.appendChild(Look.El("span", "qkg-toolbar-spacer")); foot.appendChild(more); }

            _content = VStack().WS().Gap(14.px()).Children(
                _info,
                Raw(_grid).WS(),
                Raw(foot).WS());
        }

        public HTMLElement Render() => _content.Render();

        /// <summary>Lights these groups (the page's filter, by Ordinal), or none.</summary>
        internal void Filter(HashSet<int> ordinals)
        {
            _filter = ordinals;
            if (_pinned is null) Restore();
        }

        internal string ColorOf(int ordinal) => _colouring == Colouring.Themes ? Clusters.ColorOf(ordinal) : Clusters.GroupColorOf(ordinal);

        private int OrdinalOf(QK.MapPassage p) => _colouring == Colouring.Themes ? p.ThemeOrdinal : p.GroupOrdinal;

        // -----------------------------------------------------------------------------------

        private void Light(QK.MapPassage p, HTMLElement seg)
        {
            Clear();
            _grid.classList.add("qkg-focus");
            var ordinal = OrdinalOf(p);
            if (ordinal > 0)
            {
                foreach (var el in _byGroup[ordinal]) Lit(el);
                foreach (var el in _labelsOf[ordinal]) Lit(el);
            }
            else
            {
                Lit(seg);
            }
        }

        private void Restore()
        {
            Clear();
            if (_filter is null) { _grid.classList.remove("qkg-focus"); return; }

            _grid.classList.add("qkg-focus");
            foreach (var t in _filter)
            {
                if (_byGroup.TryGetValue(t, out var segs)) foreach (var el in segs) Lit(el);
                if (_labelsOf.TryGetValue(t, out var labels)) foreach (var el in labels) Lit(el);
            }
        }

        private void Pin(QK.MapPassage p, HTMLElement seg)
        {
            if (_pinned is object && _pinned.Uid == p.Uid) { Unpin(); return; }

            _pinned = p;
            Light(p, seg);
            _info.Clear();


            var band = Look.El("div", "qkg-map-info");
            if (OrdinalOf(p) > 0 && _groups.TryGetValue(OrdinalOf(p), out var t))
            {
                band.appendChild(Raw(Swatch(ColorOf(t.Ordinal))).Render());
                band.appendChild(Look.Ar(t.LabelArabic, "qkg-arw qkg-map-info-ar"));
                if (_colouring == Colouring.Themes) band.appendChild(Look.El("strong", "", t.Label));
                band.appendChild(Look.El("span", "qkg-note", string.Format("{0} surahs · {1} passages · {2} verses".t(), (t.SurahNumbers ?? new int[0]).Length, t.Passages, t.Verses)));
                band.appendChild(Look.El("span", "qkg-toolbar-spacer"));
                band.appendChild(Look.Button(_colouring == Colouring.Themes ? "Read the theme".t() : "Read the group".t(), () => Lists.Open(t.Uid), "primary", null,
                                             "All its passages, surah by surah, with the words they share marked".t()));
            }
            else
            {
                band.appendChild(Look.El("strong", "", Range(p) + " · " + string.Format("passage {0}".t(), p.Ordinal)));
                band.appendChild(Look.Ar(p.LabelArabic, "qkg-arw qkg-map-info-ar"));
                band.appendChild(Look.El("span", "qkg-note", (_colouring == Colouring.Themes ? "Shares its subject with no other surah." : "In no similarity group.").t()));
                band.appendChild(Look.El("span", "qkg-toolbar-spacer"));
                band.appendChild(Look.Button("Open the passage".t(), () => Lists.Open(p.Uid), "plain"));
            }
            band.appendChild(Look.Button(string.Format("Read {0}".t(), Range(p)), () => QuranLinks.Go(p.SurahNumber, p.FirstAyah), "ghost", "☰", "This passage in its surah, in the Quran View".t()));
            band.appendChild(Look.IconButton("✕", "Unpin".t(), Unpin));
            _info.Add(Raw(band).WS());
        }

        private void Unpin()
        {
            _pinned = null;
            ShowHint();
            Restore();
        }

        private void ShowHint()
        {
            _info.Clear();
            _info.Add(Raw(Look.El("div", "qkg-map-info qkg-map-info-hint", (_colouring == Colouring.Themes ? "Hover a block to light its theme across the Quran; click to pin it." : "Hover a block to light its group across the Quran; click to pin it.").t())).WS());
        }

        private void Lit(HTMLElement el)
        {
            el.classList.add("qkg-lit");
            _lit.Add(el);
        }

        private void Clear()
        {
            foreach (var el in _lit) el.classList.remove("qkg-lit");
            _lit.Clear();
        }

        private string Describe(QK.MapPassage p)
        {
            var text = Range(p) + " · " + p.LabelArabic;
            if (OrdinalOf(p) > 0 && _groups.TryGetValue(OrdinalOf(p), out var t))
                text += "\n" + string.Format((_colouring == Colouring.Themes ? "Theme: {0} ({1} surahs)" : "Similarity group: {0} ({1} surahs)").t(), t.LabelArabic, (t.SurahNumbers ?? new int[0]).Length);
            return text;
        }

        private static string Range(QK.MapPassage p)
            => p.FirstAyah == p.LastAyah ? p.SurahNumber + ":" + p.FirstAyah : p.SurahNumber + ":" + p.FirstAyah + "–" + p.LastAyah;

        internal static HTMLElement Swatch(string color)
        {
            var dot = Div(Att(""));
            dot.style.width        = "14px";
            dot.style.height       = "14px";
            dot.style.borderRadius = "4px";
            dot.style.flex         = "0 0 auto";
            dot.style.background   = color;
            return dot;
        }

        private static void Add(Dictionary<int, List<HTMLElement>> into, int key, HTMLElement el)
        {
            if (!into.TryGetValue(key, out var list)) into[key] = list = new List<HTMLElement>();
            if (!list.Contains(el)) list.Add(el);
        }

        private static HTMLElement Text(string tag, string className, string text)
        {
            var el = document.createElement(tag);
            el.className   = className;
            el.textContent = text ?? "";
            return el;
        }
    }
}
