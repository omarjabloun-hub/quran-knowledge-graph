using System;
using System.Linq;
using Mosaik;
using QuranKG.FrontEnd.Api;
using QuranKG.FrontEnd.Components;
using Tesserae;
using TNT;
using static Tesserae.UI;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.TajweedPage
{
    /// <summary>
    /// Tajweed in the reading of Qalun: the colours of the mushaf of Tunisia (Dar al-Maarifah's
    /// Qalun mushaf) and the rule behind each one, in Arabic and English, with what Qalun reads
    /// differently from Hafs. Each rule is counted live across the Qalun text (qkg/tajweed/rules) —
    /// how many letters it is on, in how many verses — with words to learn it from, Al-Fatiha's and
    /// Juz 'Amma's first: a word opens its verse in the Quran View, the colours on, or plays Sheikh
    /// Latrech reciting it.
    /// </summary>
    internal sealed class TajweedView : IComponent
    {
        internal const string Route = "#/tajweed";

        internal static bool IsShowing() => (window.location.hash ?? "").StartsWith(Route, StringComparison.Ordinal);

        private const int EXAMPLES = 8;

        private readonly IComponent _content;

        public TajweedView(Parameters state)
        {
            Look.EnsureStyles();
            TajweedKey.EnsureStyles();
            Arabic.EnsureFonts();
            EnsureStyles();
            _content = Look.Page(Defer(async () => Build(await QuranApi.TajweedRulesAsync(EXAMPLES)),
                                       Raw(Look.El("p", "qkg-note", "Reading the rules in the text…".t()))));
        }

        public HTMLElement Render() => _content.Render();

        private static IComponent Build(QK.TajweedDto dto)
        {
            var stats = (dto.Rules ?? new QK.TajweedRuleStats[0]).ToDictionary(r => r.Rule, r => r);

            var read = Look.Button("Read with tajweed".t(), () => Open(0, 0, listen: false), "primary", "☰",
                                   "The Quran View, the letters coloured — where you left off".t());

            var page = VStack().WS().Gap(28.px()).Class("qkg-tj-page").Children(
                Look.Title("Tajweed".t(),
                    string.Format("The rules of reciting, in the reading of {0} ({1}) — coloured as the mushaf of Tunisia colours them, the Dar al-Maarifah «مصحف التجويد برواية قالون عن نافع». Every rule is found in the King Fahd Complex's Qalun text from its marks, and counted across its {2:N0} verses.".t(),
                                  dto.RiwayaName, dto.RiwayaNameArabic, dto.Verses),
                    Raw(read), kicker: "التجويد"));

            // The key, as the mushaf prints it at the foot of each page.
            var key = Look.Panel("The colours".t(), "The key at the foot of every page of the mushaf; Qalun's own rules are underlined.".t(),
                                 Raw(TajweedKey.Legend(null)));
            page.Add(key);

            foreach (var family in TajweedKey.Families)
            {
                var rules = TajweedKey.Rules.Where(r => r.FamilyId == family.Id).ToArray();
                if (rules.Length == 0) continue;

                var head = Look.El("div", "qkg-tj-family");
                head.appendChild(Look.El("span", "qkg-tj-swatch qkg-tj-swatch-lg qkg-tj-bg-" + family.Id));
                head.appendChild(Look.Ar(family.KeyArabic, "qkg-arw qkg-tj-family-ar"));
                head.appendChild(Look.El("span", "qkg-note", family.Key));
                page.Add(Raw(head));

                page.Add(Look.Columns(460, rules.Select(r => RuleCard(r, stats.TryGetValue(r.Id, out var st) ? st : null)).ToArray()));
            }

            page.Add(Raw(Look.El("p", "qkg-note qkg-tj-caveat",
                "The colours are worked out from the King Fahd Complex's marks and checked against printed pages of the Dar al-Maarifah mushaf (Al-Fatiha, the start of Al-Baqarah). They help you see a rule; they do not replace a teacher — recite to one.".t())));
            return page;
        }

        private static IComponent RuleCard(TajweedKey.Rule r, QK.TajweedRuleStats st)
        {
            var card = Look.El("div", "qkg-card qkg-tj-rule");

            var title = Look.El("div", "qkg-tj-rule-head");
            title.appendChild(Look.Ar(r.NameArabic, "qkg-arw qkg-tj-rule-ar qkg-tj-" + r.FamilyId));
            title.appendChild(Look.El("span", "qkg-h3", r.Name));
            card.appendChild(title);

            if (st is object)
                card.appendChild(Look.El("span", "qkg-note", string.Format("On {0:N0} letters, in {1:N0} verses".t(), st.Count, st.Verses)));

            var ar = Look.El("p", "qkg-tj-rule-text-ar", r.Arabic);
            ar.setAttribute("dir", "rtl");
            card.appendChild(ar);
            card.appendChild(Look.El("p", "qkg-tj-rule-text", r.English));
            card.appendChild(Look.El("p", "qkg-note qkg-tj-rule-qalun", "Qalun: ".t() + r.Qalun));

            var examples = st?.Examples ?? new QK.TajweedExample[0];
            if (examples.Length > 0)
            {
                var list = Look.El("div", "qkg-tj-examples");
                foreach (var e in examples)
                {
                    var captured = e;
                    var item = Look.El("div", "qkg-tj-example");
                    var word = Look.El("span", "qkg-tj-word");
                    word.setAttribute("dir", "rtl");
                    // The word with all its colours, the rule's own letters lit.
                    var marks = TajweedKey.Parse(e.Tajweed);
                    TajweedKey.Paint(word, e.Word, 0, marks, underline: r.Id);
                    word.title = string.Format("{0} — read it in its verse".t(), e.VerseKey);
                    item.appendChild(Look.OnClick(word, () => Open(captured.SurahNumber, HafsAyah(captured.HafsKey), listen: false)));
                    var meta = Look.El("span", "qkg-tj-example-meta");
                    meta.appendChild(Look.El("span", "qkg-note", e.VerseKey));
                    var play = Look.El("button", "qkg-tj-play", "▶");
                    play.setAttribute("type", "button");
                    play.title = string.Format("Sheikh Latrech reciting {0}".t(), e.VerseKey);
                    play.onclick = ev => { ev.stopPropagation(); Open(captured.SurahNumber, HafsAyah(captured.HafsKey), listen: true); };
                    meta.appendChild(play);
                    item.appendChild(meta);
                    list.appendChild(item);
                }
                card.appendChild(list);
            }
            else if (st is null)
                card.appendChild(Look.El("p", "qkg-note", "Not found in this text.".t()));

            return Raw(card);
        }

        private static int HafsAyah(string key)
        {
            var colon = (key ?? "").IndexOf(':');
            return colon > 0 && int.TryParse(key.Substring(colon + 1), out var a) ? a : 0;
        }

        /// <summary>The Quran View with the colours on: at a verse, or where the reader left off; reciting it if asked.</summary>
        private static void Open(int surah, int ayah, bool listen)
        {
            ReaderOptions.RememberTajweed("quran");
            if (surah == 0)
            {
                var last = QuranLinks.LastRead();
                surah = last.Surah > 0 ? last.Surah : 1;
                ayah  = last.Surah > 0 ? last.Ayah : 0;
            }
            QuranLinks.ListenOnArrival = listen;
            QuranLinks.Go(surah, ayah);
        }

        private static bool _styled;

        private static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            style.textContent = @"
.qkg-tj-family { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-top: 8px; }
.qkg-tj-swatch-lg { width: 18px; height: 18px; }
.qkg-tj-family-ar { font-size: 26px; color: var(--qkg-ink); }
.qkg-tj-rule { display: flex; flex-direction: column; gap: 10px; }
.qkg-tj-rule-head { display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap; }
.qkg-tj-rule-ar { font-size: 26px; }
.qkg-tj-rule-text-ar { margin: 0; font-family: 'Amiri', 'Noto Naskh Arabic', serif; font-size: 19px; line-height: 1.9; color: var(--qkg-ink); }
.qkg-tj-rule-text { margin: 0; font-size: 14px; line-height: 1.6; color: var(--qkg-ink-2); }
.qkg-tj-rule-qalun { margin: 0; }
.qkg-tj-examples { display: flex; flex-wrap: wrap; gap: 8px; direction: rtl; }
.qkg-tj-example { display: inline-flex; flex-direction: column; align-items: center; gap: 2px; padding: 6px 12px; border: 1px solid var(--qkg-line); border-radius: 12px; background: var(--qkg-raised); }
.qkg-tj-word { font-family: 'KFGQPC Qalun', 'Amiri Quran', 'Amiri', serif; font-size: 30px; line-height: 1.9; cursor: pointer; color: var(--qkg-ink); }
.qkg-tj-example-meta { display: inline-flex; align-items: center; gap: 6px; direction: ltr; }
.qkg-tj-play { border: 0; background: transparent; color: var(--qkg-teal-strong); cursor: pointer; font-size: 12px; padding: 0 2px; }
.qkg-tj-caveat { max-width: 760px; }
";
            document.head.appendChild(style);
        }
    }
}
