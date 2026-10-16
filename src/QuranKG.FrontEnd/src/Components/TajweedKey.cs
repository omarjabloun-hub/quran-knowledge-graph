using System;
using System.Collections.Generic;
using System.Linq;
using TNT;
using static TNT.T;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// Tajweed in colour, as the mushaf of Tunisia prints it: the eight colours of Dar al-Maarifah's
    /// «مصحف التجويد برواية قالون عن نافع», whose key sits at the foot of every page, and what each
    /// rule is — for the reader's colours, its key, and the Tajweed page.
    ///
    /// <para>The rule ids mirror <c>QuranKG.Dataset.Text.Tajweed.Rules</c>, which finds them in the
    /// King Fahd Complex's Qalun text; a verse carries them as "rule:start-end …"
    /// (<c>RiwayaVerse.Tajweed</c>). The explanations are the standard ones of the books of tajweed,
    /// with what Qalun reads differently from Hafs.</para>
    /// </summary>
    internal static class TajweedKey
    {
        internal sealed class Family
        {
            internal string Id;
            /// <summary>The mushaf's own words for it, in its key.</summary>
            internal string KeyArabic;
            internal string Key;
            internal Family(string id, string keyArabic, string key) { Id = id; KeyArabic = keyArabic; Key = key; }
        }

        internal sealed class Rule
        {
            internal string Id, FamilyId, NameArabic, Name, Arabic, English, Qalun;
        }

        /// <summary>The key's eight colours, in its order, and Qalun's own marks.</summary>
        internal static readonly Family[] Families =
        {
            new Family("madd6",   "مد 6 حركات لزوماً",       "Madd of 6 counts, necessarily"),
            new Family("madd4",   "مد متوسط 4 حركات",        "Madd of 4 counts"),
            new Family("madd246", "مد 2 أو 4 أو 6 جوازاً",   "Madd of 2, 4 or 6 counts, by choice"),
            new Family("madd2",   "مد حركتان",               "Madd of 2 counts"),
            new Family("ghunna",  "إخفاء ومواقع الغنة",      "Ikhfa, and where the ghunna is"),
            new Family("silent",  "إدغام وما لا يلفظ",       "Idgham, and what is not pronounced"),
            new Family("tafkhim", "تفخيم الراء",             "A heavy rā'"),
            new Family("qalqala", "قلقلة",                   "Qalqala"),
            new Family("qalun",   "من أصول قالون",           "Qalun's own (underlined)"),
        };

        internal static readonly Rule[] Rules =
        {
            new Rule { Id = "lazim", FamilyId = "madd6", NameArabic = "المد اللازم", Name = "The necessary madd",
                Arabic  = "حرف مدٍّ بعده سكونٌ أصليٌّ أو حرفٌ مشدَّد في الكلمة نفسها، أو حرفٌ من فواتح السور هجاؤه ثلاثة أحرف أوسطها حرف مدّ. يُمَدّ ستَّ حركات لزوماً.",
                English = "A madd letter followed, in the same word, by an original sukūn or a doubled letter — or a letter of a surah's opening letters whose name has a madd in its middle. Held for six counts, always.",
                Qalun   = "As Hafs: «اَ۬لضَّآلِّينَ»، «الٓمٓ»." },
            new Rule { Id = "muttasil", FamilyId = "madd4", NameArabic = "المد المتصل", Name = "The joined madd",
                Arabic  = "حرف مدٍّ بعده همزةٌ في الكلمة نفسها. يمدّه قالون أربع حركات (التوسط).",
                English = "A madd letter followed by a hamza in the same word. Qalun holds it four counts (tawassut).",
                Qalun   = "«سَوَآءٌ»، «أُوْلَٰٓئِكَ»، «جَآءَ»." },
            new Rule { Id = "munfasil", FamilyId = "madd4", NameArabic = "المد المنفصل", Name = "The separated madd",
                Arabic  = "حرف مدٍّ في آخر كلمة وهمزةٌ في أول التي بعدها: «بِمَا أُنزِلَ». يقرؤه هذا المصحف لقالون بالقصر حركتين فلا يلوَّن، ويُلوَّن ما كُتب منه بعلامة المد.",
                English = "A madd letter ending a word, a hamza opening the next: «بِمَا أُنزِلَ». This mushaf reads Qalun with qasr — two counts — and leaves it black; the few written with the madd sign are coloured.",
                Qalun   = "Qalun has qasr (2) and tawassut (4); the King Fahd Complex's text, like the mushaf, writes qasr." },
            new Rule { Id = "arid", FamilyId = "madd246", NameArabic = "المد العارض للسكون", Name = "The madd before a stop",
                Arabic  = "حرف مدٍّ بعده حرفٌ سُكِّن للوقف: «نَسْتَعِينُ» ← «نَسْتَعِينْ». يجوز فيه القصر حركتان، والتوسط أربع، والإشباع ست.",
                English = "A madd letter before a letter made silent by the stop: «نَسْتَعِينُ» read «نَسْتَعِينْ». Two, four or six counts, by choice — keep to one in a reading.",
                Qalun   = "As Hafs. Here every verse is read as ending on a stop." },
            new Rule { Id = "leen", FamilyId = "madd246", NameArabic = "مد اللين", Name = "The leen madd",
                Arabic  = "واوٌ أو ياءٌ ساكنة قبلها فتح، بعدها حرفٌ سُكِّن للوقف: «قُرَيْشْ»، «خَوْفْ». يجوز فيه 2 أو 4 أو 6.",
                English = "A wāw or yā' with sukūn after a fatha, before a letter made silent by the stop: «قُرَيْشْ», «خَوْفْ». Two, four or six counts.",
                Qalun   = "As Hafs." },
            new Rule { Id = "small_alif", FamilyId = "madd2", NameArabic = "الألف الصغيرة", Name = "The small alif",
                Arabic  = "ألفٌ صغيرة فوق الحرف تُقرأ مدّاً طبيعياً بمقدار حركتين: «ذَٰلِكَ»، «اَ۬لْكِتَٰبُ»، «اَ۬لرَّحْمَٰنِ».",
                English = "A small alif over a letter, read as a natural madd of two counts: «ذَٰلِكَ», «اَ۬لْكِتَٰبُ», «اَ۬لرَّحْمَٰنِ».",
                Qalun   = "As Hafs. Where the King Fahd Complex writes a small alif the printed mushaf sometimes writes a full one, uncoloured." },
            new Rule { Id = "sila", FamilyId = "madd2", NameArabic = "الصلة الصغرى", Name = "The sila of the pronoun",
                Arabic  = "هاء الضمير بين متحرّكين توصل بواوٍ أو ياءٍ صغيرة تُمَدّ حركتين: «لَهُۥ»، «بِهِۦ».",
                English = "The pronoun hā' between two voweled letters is joined to a small wāw or yā', held two counts: «لَهُۥ», «بِهِۦ».",
                Qalun   = "Qalun differs from Hafs in some words (he shortens the hā' of «يُؤَدِّهِ» and its like); the text writes the small letter only where he reads it." },
            new Rule { Id = "ghunna", FamilyId = "ghunna", NameArabic = "الغنة", Name = "Ghunna",
                Arabic  = "النون والميم المشدَّدتان: صوتٌ أغنُّ يخرج من الخيشوم بمقدار حركتين: «إِنَّ»، «ثُمَّ»، «اِ۬لنَّاسِ».",
                English = "A doubled nūn or mīm: a nasal sound from the nose, held two counts: «إِنَّ», «ثُمَّ», «اِ۬لنَّاسِ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "ikhfa", FamilyId = "ghunna", NameArabic = "الإخفاء", Name = "Ikhfa",
                Arabic  = "النون الساكنة أو التنوين قبل أحد خمسة عشر حرفاً (ت ث ج د ذ ز س ش ص ض ط ظ ف ق ك): تُخفى بغنّة. يكتبها المصحف نوناً بلا سكون وتنويناً متتابعاً: «مِن قَبْلِكَ»، «يُنفِقُونَ».",
                English = "A silent nūn or a tanwin before one of fifteen letters (ت ث ج د ذ ز س ش ص ض ط ظ ف ق ك) is hidden, with ghunna. The mushaf writes the nūn without a sukūn and the tanwin staggered: «مِن قَبْلِكَ», «يُنفِقُونَ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "ikhfa_shafawi", FamilyId = "ghunna", NameArabic = "الإخفاء الشفوي", Name = "Ikhfa of the mīm",
                Arabic  = "الميم الساكنة قبل الباء تُخفى بغنّة: «وَمَا هُم بِمُؤْمِنِينَ».",
                English = "A silent mīm before a bā' is hidden, with ghunna: «وَمَا هُم بِمُؤْمِنِينَ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "iqlab", FamilyId = "ghunna", NameArabic = "الإقلاب", Name = "Iqlab",
                Arabic  = "النون الساكنة أو التنوين قبل الباء تُقلب ميماً مخفاة بغنّة، وتُكتب فوقها ميمٌ صغيرة: «أَلِيمُۢ بِمَا»، «مِنۢ بَعْدِ».",
                English = "A silent nūn or a tanwin before a bā' turns into a hidden mīm, with ghunna; a small mīm is written over it: «أَلِيمُۢ بِمَا», «مِنۢ بَعْدِ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "idgham_ghunna", FamilyId = "ghunna", NameArabic = "الإدغام بغنة", Name = "Idgham with ghunna",
                Arabic  = "النون الساكنة أو التنوين قبل ي ن م و في الكلمة التالية تُدغم فيها بغنّة: «مَنْ يَّقُولُ»، «غِشَٰوَةٞ وَلَهُمْ».",
                English = "A silent nūn or a tanwin before ي ن م و opening the next word merges into it, with ghunna: «مَنْ يَّقُولُ», «غِشَٰوَةٞ وَلَهُمْ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "idgham", FamilyId = "silent", NameArabic = "الإدغام", Name = "Idgham",
                Arabic  = "حرفٌ يُدغم في الحرف الذي بعده فلا يُلفظ: النون قبل اللام والراء بلا غنّة («مِّن رَّبِّهِمْ»، «وَلَٰكِن لَّا»)، والدال في التاء («قَد تَّبَيَّنَ»)، وغيرها.",
                English = "A letter merged into the one after it, not pronounced: a nūn before lām or rā', without ghunna («مِّن رَّبِّهِمْ», «وَلَٰكِن لَّا»); dāl into tā' («قَد تَّبَيَّنَ»); and others.",
                Qalun   = "As Hafs, with a few differences the text writes as Qalun reads them." },
            new Rule { Id = "lam_shamsiyya", FamilyId = "silent", NameArabic = "اللام الشمسية", Name = "The lām of al- before a sun letter",
                Arabic  = "لام التعريف قبل حرفٍ شمسيّ تُكتب ولا تُلفظ، ويُشدَّد الحرف بعدها: «اَ۬لرَّحْمَٰنِ»، «اِ۬لنَّاسِ».",
                English = "The lām of al- before a sun letter is written but not pronounced; the letter after it is doubled: «اَ۬لرَّحْمَٰنِ», «اِ۬لنَّاسِ».",
                Qalun   = "As Hafs. The name of God is left black, as in the mushaf." },
            new Rule { Id = "silent", FamilyId = "silent", NameArabic = "ما لا يُلفظ", Name = "Written, not pronounced",
                Arabic  = "حرفٌ يُكتب ولا يُلفظ: الألف بعد واو الجماعة («كَفَرُواْ»)، والواو في «أُوْلَٰٓئِكَ».",
                English = "A letter written but not pronounced: the alif after the plural wāw («كَفَرُواْ»), the wāw of «أُوْلَٰٓئِكَ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "tafkhim", FamilyId = "tafkhim", NameArabic = "تفخيم الراء", Name = "A heavy rā'",
                Arabic  = "تُفخَّم الراء إذا فُتحت أو ضُمّت، أو سكنت بعد فتحٍ أو ضمّ: «رَبِّ»، «اَ۬لْأَرْضِ»؛ وتُرقَّق إذا كُسرت، أو سكنت بعد كسرٍ أصليّ ليس بعدها حرف استعلاء: «فِرْعَوْنَ». وفي الوقف ينظر إلى ما قبلها.",
                English = "Rā' is heavy with fatha or damma, or silent after a fatha or damma: «رَبِّ», «اَ۬لْأَرْضِ»; light with kasra, or silent after an original kasra with no heavy letter after it: «فِرْعَوْنَ». At a stop, what comes before it decides.",
                Qalun   = "As Hafs (Warsh, the other reading of Nafi', makes many more rā's light)." },
            new Rule { Id = "qalqala", FamilyId = "qalqala", NameArabic = "القلقلة", Name = "Qalqala",
                Arabic  = "حروف «قُطْبُ جَدٍّ» إذا سكنت تضطرب في مخرجها حتى يُسمع لها نبرةٌ قويّة، وتقوى عند الوقف: «قَبْلِكَ»، «اَ۬لْفَلَقِ».",
                English = "The letters of «قُطْبُ جَدٍّ», when silent, bounce in their place of articulation with a strong echo — stronger at a stop: «قَبْلِكَ», «اَ۬لْفَلَقِ».",
                Qalun   = "As Hafs." },
            new Rule { Id = "hamzatan", FamilyId = "qalun", NameArabic = "الهمزتان من كلمتين", Name = "Two hamzas across words",
                Arabic  = "إذا التقت همزتان في كلمتين: يُسقط قالون الأولى إذا اتفقتا فتحاً («جَآ أَجَلُهُمْ»)، ويُسهّلها إذا اتفقتا كسراً أو ضمّاً («هَٰؤُلَآ۟ إِن»). يكتبها المصحف بصفرٍ صغير.",
                English = "When two hamzas meet across words, Qalun drops the first if both have fatha («جَآ أَجَلُهُمْ») and eases it if both have kasra or damma («هَٰؤُلَآ۟ إِن»). The text marks it with a small zero.",
                Qalun   = "Qalun's own: Hafs pronounces both." },
            new Rule { Id = "tashil", FamilyId = "qalun", NameArabic = "تسهيل الهمزة", Name = "The second hamza eased",
                Arabic  = "إذا اجتمعت همزتان في كلمة سهّل قالون الثانية بين الهمزة والحرف، وأدخل بينهما ألفاً: «ءَٰا۬نذَرْتَهُمْ». يكتبها المصحف بنقطة.",
                English = "Two hamzas in a word: Qalun eases the second, between a hamza and a vowel, and puts an alif between them: «ءَٰا۬نذَرْتَهُمْ». The text marks it with a dot.",
                Qalun   = "Qalun's own: Hafs pronounces both hamzas." },
            new Rule { Id = "ikhtilas", FamilyId = "qalun", NameArabic = "الاختلاس", Name = "A vowel read lightly",
                Arabic  = "يختلس قالون حركة بعض الحروف، أي ينطقها سريعةً خفيفة: «نِعِمَّا» (نِع۪مَّا)، «لَا تَعْدُوا» (تَع۬دُّواْ). يكتبها المصحف بنقطة.",
                English = "Qalun reads the vowel of a few letters quickly and lightly: «نِعِمَّا» (نِع۪مَّا), «لَا تَعْدُوا» (تَع۬دُّواْ). The text marks it with a dot.",
                Qalun   = "Qalun's own." },
        };

        internal static Rule RuleOf(string id) => Rules.FirstOrDefault(r => r.Id == id);
        internal static string FamilyOf(string ruleId) => RuleOf(ruleId)?.FamilyId ?? "";

        /// <summary>A verse's or a word's tajweed: "rule:start-end …" as (rule, start, end).</summary>
        internal static List<(string Rule, int Start, int End)> Parse(string marks)
        {
            var list = new List<(string, int, int)>();
            foreach (var m in (marks ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = m.LastIndexOf(':');
                var dash  = m.LastIndexOf('-');
                if (colon <= 0 || dash <= colon) continue;
                if (int.TryParse(m.Substring(colon + 1, dash - colon - 1), out var start) && int.TryParse(m.Substring(dash + 1), out var end) && end > start)
                    list.Add((m.Substring(0, colon), start, end));
            }
            return list;
        }

        /// <summary>
        /// Writes <paramref name="text"/> (a word, at <paramref name="offset"/> in its verse) into
        /// <paramref name="into"/>, each run a rule is on in a span of its colour. A run is a whole
        /// letter with its marks, so the Arabic still joins.
        /// </summary>
        internal static void Paint(HTMLElement into, string text, int offset, List<(string Rule, int Start, int End)> marks, string underline = null)
        {
            var at = 0;
            foreach (var m in marks.Where(x => x.End > offset && x.Start < offset + text.Length).OrderBy(x => x.Start))
            {
                var from = Math.Max(0, m.Start - offset);
                var to   = Math.Min(text.Length, m.End - offset);
                if (from < at) continue;
                if (from > at) into.appendChild(document.createTextNode(text.Substring(at, from - at)));
                var span = document.createElement("span");
                span.className   = "qkg-tj qkg-tj-" + FamilyOf(m.Rule) + (m.Rule == underline ? " qkg-tj-this" : "");
                span.textContent = text.Substring(from, to - from);
                var rule = RuleOf(m.Rule);
                if (rule is object) span.title = rule.NameArabic + " — " + rule.Name;
                into.appendChild(span);
                at = to;
            }
            if (at < text.Length) into.appendChild(document.createTextNode(text.Substring(at)));
        }

        /// <summary>The key: a swatch and the mushaf's words for each colour; a click opens the Tajweed page.</summary>
        internal static HTMLElement Legend(Action open)
        {
            EnsureStyles();
            var key = Look.El("div", "qkg-tj-key");
            foreach (var f in Families)
            {
                var item = Look.El("span", "qkg-tj-key-item");
                item.appendChild(Look.El("span", "qkg-tj-swatch qkg-tj-bg-" + f.Id));
                item.appendChild(Look.Ar(f.KeyArabic, "qkg-arw qkg-tj-key-ar"));
                item.title = f.Key;
                key.appendChild(item);
            }
            if (open is object) key.appendChild(Look.OnClick(Look.El("span", "qkg-link qkg-tj-key-more", "The rules →".t()), open));
            return key;
        }

        private static bool _styled;

        internal static void EnsureStyles()
        {
            if (_styled) return;
            _styled = true;
            var style = document.createElement("style");
            // The colours of the Dar al-Maarifah mushaf, a shade lighter on a dark page.
            style.textContent = @"
:root { --qkg-tj-madd6: #8b1c1c; --qkg-tj-madd4: #e2231a; --qkg-tj-madd246: #ef7d00; --qkg-tj-madd2: #a8741a;
        --qkg-tj-ghunna: #12923c; --qkg-tj-silent: #9a9a9a; --qkg-tj-tafkhim: #1c3f8f; --qkg-tj-qalqala: #1e9bd7; --qkg-tj-qalun: #7c3aed; }
.qkg-reading-dark, body.tss-dark-mode .qkg-reader-scroll:not(.qkg-reading-light):not(.qkg-reading-sepia), body.tss-dark-mode .qkg-tj-page {
        --qkg-tj-madd6: #e06a6a; --qkg-tj-madd4: #ff6b5e; --qkg-tj-madd246: #ffa940; --qkg-tj-madd2: #d9a54a;
        --qkg-tj-ghunna: #47c46e; --qkg-tj-silent: #8d8d8d; --qkg-tj-tafkhim: #7ea6ff; --qkg-tj-qalqala: #5cc4f5; --qkg-tj-qalun: #b794f6; }
.qkg-tj-madd6 { color: var(--qkg-tj-madd6); } .qkg-tj-madd4 { color: var(--qkg-tj-madd4); } .qkg-tj-madd246 { color: var(--qkg-tj-madd246); }
.qkg-tj-madd2 { color: var(--qkg-tj-madd2); } .qkg-tj-ghunna { color: var(--qkg-tj-ghunna); } .qkg-tj-silent { color: var(--qkg-tj-silent); }
.qkg-tj-tafkhim { color: var(--qkg-tj-tafkhim); } .qkg-tj-qalqala { color: var(--qkg-tj-qalqala); }
.qkg-tj-qalun { text-decoration: underline dotted var(--qkg-tj-qalun); text-decoration-thickness: 2px; text-underline-offset: 6px; }
.qkg-tj-this { background: color-mix(in srgb, currentColor 14%, transparent); border-radius: 4px; }
.qkg-tj-bg-madd6 { background: var(--qkg-tj-madd6); } .qkg-tj-bg-madd4 { background: var(--qkg-tj-madd4); } .qkg-tj-bg-madd246 { background: var(--qkg-tj-madd246); }
.qkg-tj-bg-madd2 { background: var(--qkg-tj-madd2); } .qkg-tj-bg-ghunna { background: var(--qkg-tj-ghunna); } .qkg-tj-bg-silent { background: var(--qkg-tj-silent); }
.qkg-tj-bg-tafkhim { background: var(--qkg-tj-tafkhim); } .qkg-tj-bg-qalqala { background: var(--qkg-tj-qalqala); }
.qkg-tj-bg-qalun { background: transparent; border: 2px dotted var(--qkg-tj-qalun); box-sizing: border-box; }
.qkg-tj-key { flex: 1 1 100%; display: flex; flex-wrap: wrap; align-items: center; gap: 6px 16px; direction: rtl; }
.qkg-tj-key-item { display: inline-flex; align-items: center; gap: 6px; }
.qkg-tj-swatch { width: 12px; height: 12px; border-radius: 50%; display: inline-block; flex: 0 0 auto; }
.qkg-tj-key-ar { font-size: 15px; color: var(--qkg-ink-2); }
.qkg-tj-key-more { direction: ltr; font-size: 13px; }
";
            document.head.appendChild(style);
        }
    }
}
