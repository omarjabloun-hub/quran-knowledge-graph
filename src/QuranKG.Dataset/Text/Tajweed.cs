namespace QuranKG.Dataset.Text;

/// <summary>A rule of tajweed found on a run of a verse's text: its id and the characters [Start, End) it covers.</summary>
public sealed record TajweedMark(string Rule, int Start, int End);

/// <summary>A rule of tajweed: its id, the colour family the mushaf gives it, its names.</summary>
public sealed record TajweedRule(string Id, string Colour, string NameArabic, string Name);

/// <summary>
/// Tajweed on the Qalun text, as the colour-coded mushaf of Tunisia marks it (Dar al-Maarifah's
/// «مصحف التجويد برواية قالون عن نافع»): eight colours, each a family of rules — its key, at the foot
/// of every page, reads
///   dark red   مد 6 حركات لزوماً          the necessary madd (lazim)
///   red        مد متوسط 4 حركات           the joined madd (muttasil; and a separated madd written long)
///   orange     مد 2 أو 4 أو 6 جوازاً      the madd before a stop ('arid, leen)
///   brown      مد حركتان                  a madd written with a small letter: the small alif («ذَٰلِكَ»),
///                                         the sila of the pronoun («لَهُۥ», «بِهِۦ»)
///   green      إخفاء ومواقع الغنة          ikhfa, iqlab, the ghunna of نّ مّ, idgham with ghunna
///   grey       إدغام وما لا يلفظ           a letter merged into the next, a letter not pronounced
///   dark blue  تفخيم الراء                 a heavy rā'
///   light blue قلقلة                       qalqala
/// and two marks of Qalun's own that the King Fahd Complex's text writes with a dot (۬ ۪): the eased
/// second hamza (tas-hil, «ءَٰا۬نذَرْتَهُمْ») and the vowel read lightly (ikhtilas, «نِع۪مَّا»).
///
/// The rules read the marks of the King Fahd Complex's Qalun text — its Madani dabt says what is
/// pronounced how: a nūn with no mark is hidden or merged (the next letter's shadda tells which), an
/// open tanwin (U+0656, U+0657, U+065E) is too, a small mīm (U+06E2) is iqlab, the madd sign (U+0653)
/// marks every lengthened madd, an alif or wāw with sukūn is silent. That text reads the separated
/// madd short and the plural mīm without sila, so neither is coloured; Qalun reads the madd after a
/// hamza (badal) short too, and the mushaf leaves it black («ءَامَنُواْ»). Each verse is read as ending
/// on a stop; a stop inside a verse is not. One rule per letter — the first in <see cref="Rules"/>
/// (the key's order) where two apply: «مِّٓ» is a necessary madd before it is a ghunna. Works per
/// verse; positions are UTF-16 indexes into it.
///
/// Checked against the printed pages of the Dar al-Maarifah Qalun mushaf (Al-Fatiha, Al-Baqarah
/// 1–16); where the King Fahd Complex writes a small alif the mushaf writes full («أَبْصَٰرِهِمْ»), the
/// small alif is coloured here and not there.
/// </summary>
public static class Tajweed
{
    public const string LAZIM = "lazim", MUTTASIL = "muttasil", MUNFASIL = "munfasil", ARID = "arid", LEEN = "leen",
                        SMALL_ALIF = "small_alif", SILA = "sila", GHUNNA = "ghunna", IKHFA = "ikhfa", IKHFA_SHAFAWI = "ikhfa_shafawi",
                        IQLAB = "iqlab", IDGHAM_GHUNNA = "idgham_ghunna", IDGHAM = "idgham", LAM_SHAMSIYYA = "lam_shamsiyya",
                        SILENT = "silent", TAFKHIM = "tafkhim", QALQALA = "qalqala", HAMZATAN = "hamzatan", TASHIL = "tashil", IKHTILAS = "ikhtilas";

    /// <summary>The rules, in the order of the mushaf's key; <c>Colour</c> is the family ("madd6", "madd4", "madd246", "madd2", "ghunna", "silent", "tafkhim", "qalqala", "qalun").</summary>
    public static readonly IReadOnlyList<TajweedRule> Rules =
    [
        new(LAZIM,         "madd6",   "المد اللازم",                 "Necessary madd — 6 counts"),
        new(MUTTASIL,      "madd4",   "المد المتصل",                 "Joined madd — 4 counts"),
        new(MUNFASIL,      "madd4",   "المد المنفصل",                "Separated madd, written long — 4 counts"),
        new(ARID,          "madd246", "المد العارض للسكون",          "Madd before a stop — 2, 4 or 6 counts"),
        new(LEEN,          "madd246", "مد اللين",                    "Leen madd before a stop — 2, 4 or 6 counts"),
        new(SMALL_ALIF,    "madd2",   "الألف الصغيرة",               "The small alif — a madd of 2 counts"),
        new(SILA,          "madd2",   "الصلة الصغرى",                "Sila of the pronoun hā' — 2 counts"),
        new(GHUNNA,        "ghunna",  "الغنة",                       "Ghunna of a doubled nūn or mīm"),
        new(IKHFA,         "ghunna",  "الإخفاء",                     "Ikhfa — the nūn or tanwin hidden"),
        new(IKHFA_SHAFAWI, "ghunna",  "الإخفاء الشفوي",              "Ikhfa of the mīm before bā'"),
        new(IQLAB,         "ghunna",  "الإقلاب",                     "Iqlab — the nūn or tanwin turned to mīm"),
        new(IDGHAM_GHUNNA, "ghunna",  "الإدغام بغنة",                "Idgham with ghunna"),
        new(IDGHAM,        "silent",  "الإدغام",                     "Idgham — a letter merged into the next"),
        new(LAM_SHAMSIYYA, "silent",  "اللام الشمسية",               "The lām of al- before a sun letter"),
        new(SILENT,        "silent",  "ما لا يلفظ",                  "A letter written, not pronounced"),
        new(TAFKHIM,       "tafkhim", "تفخيم الراء",                 "A heavy rā'"),
        new(QALQALA,       "qalqala", "القلقلة",                     "Qalqala"),
        new(HAMZATAN,      "qalun",   "الهمزتان من كلمتين",          "Two hamzas across words: the first dropped or eased (Qalun)"),
        new(TASHIL,        "qalun",   "تسهيل الهمزة",                "The second hamza eased (Qalun)"),
        new(IKHTILAS,      "qalun",   "الاختلاس",                    "A vowel read lightly (Qalun)"),
    ];

    private const char Fatha = '\u064E', Damma = '\u064F', Kasra = '\u0650', Sukun = '\u0652', Shadda = '\u0651',
                       Fathatan = '\u064B', Dammatan = '\u064C', Kasratan = '\u064D',
                       OpenFathatan = '\u0657', OpenDammatan = '\u065E', OpenKasratan = '\u0656',
                       Maddah = '\u0653', Dagger = '\u0670', SmallMeem = '\u06E2', SmallWaw = '\u06E5', SmallYeh = '\u06E6',
                       Zero = '\u06DF', DotHigh = '\u06EC', DotLow = '\u06EA', HamzaAbove = '\u0654';

    private const string Hamzas     = "ءأإؤئ";
    private const string Qalqalah   = "قطبجد";
    private const string Isti3la    = "خصضغطقظ";
    private const string GhunnaNext = "ينمو";

    /// <summary>A letter and the marks written on it.</summary>
    private sealed class G
    {
        public char   Base;
        public string Marks = "";
        public int    Start, End, Word, Index;
        public bool   Has(char c) => Marks.IndexOf(c) >= 0;
        public bool   IsHamza => Hamzas.IndexOf(Base) >= 0 || (Base == '\u0640' && (Has(HamzaAbove) || Has('\u0655')));   // a hamza on a tatweel: «يَـُٔودُهُۥ», «اَ۬لنَّبِيٓـِٕۧنَ»
        public bool   Dotted  => Has(DotHigh) || Has(DotLow);
        public bool   HasVowel => Has(Fatha) || Has(Damma) || Has(Kasra) || HasTanwin;
        public bool   HasTanwin => Has(Fathatan) || Has(Dammatan) || Has(Kasratan) || OpenTanwin;
        public bool   OpenTanwin => Has(OpenFathatan) || Has(OpenDammatan) || Has(OpenKasratan);
        /// <summary>No vowel, sukūn, shadda or tanwin: a letter merged or hidden, or a madd letter.</summary>
        public bool   Bare => !HasVowel && !Has(Sukun) && !Has(Shadda);
    }

    public static List<TajweedMark> Annotate(string text)
    {
        var g = Parse(text ?? "");
        if (g.Count == 0) return [];

        // One rule per letter: the first in the key's order.
        var rank   = Rules.Select((r, k) => (r.Id, k)).ToDictionary(x => x.Id, x => x.k, StringComparer.Ordinal);
        var chosen = new Dictionary<int, string>();
        void Mark(string rule, G x)
        {
            if (!chosen.TryGetValue(x.Index, out var had) || rank[rule] < rank[had]) chosen[x.Index] = rule;
        }
        G?   At(int i) => i >= 0 && i < g.Count ? g[i] : null;
        bool SameWord(int i, int j) => At(i) is { } a && At(j) is { } b && a.Word == b.Word;

        // The vowel a letter is read with: its own, or the tanwin of the alif after it («كَثِيراٗ»).
        bool Reads(int i, params char[] vowels)
        {
            var x = g[i];
            if (vowels.Any(x.Has)) return true;
            return x.Bare && SameWord(i, i + 1) && (g[i + 1].Base is 'ا' or '\u0649') && vowels.Any(g[i + 1].Has);
        }
        bool Fath(int i)  => Reads(i, Fatha, Fathatan, OpenFathatan);
        bool Damm(int i)  => Reads(i, Damma, Dammatan, OpenDammatan);
        bool Kasr(int i)  => Reads(i, Kasra, Kasratan, OpenKasratan);

        // A madd letter: alif after fatha, wāw after damma, yā' after kasra, each with no mark of its own.
        bool MaddLetter(int i)
        {
            var x = g[i];
            if (!SameWord(i - 1, i) || x.Dotted || x.Has(Sukun) || x.Has(Shadda) || x.Has(Fatha) || x.Has(Damma) || x.Has(Kasra)) return false;
            return (x.Base is 'ا' or '\u0649' && Fath(i - 1))
                || (x.Base == 'و' && Damm(i - 1))
                || (x.Base is 'ي' or '\u06D2' && Kasr(i - 1));
        }
        bool Leen(int i) => SameWord(i - 1, i) && (g[i].Base is 'و' or 'ي' or '\u06D2') && g[i].Has(Sukun) && Fath(i - 1);

        var lastWord = g[^1].Word;
        var taken    = new HashSet<int>();   // letters a madd rule has already claimed

        for (var i = 0; i < g.Count; i++)
        {
            var x    = g[i];
            var next = At(i + 1);
            var endOfWord = next is null || next.Word != x.Word;

            // ---- madd: the madd sign says the madd is lengthened; what follows says why ----------
            // A hamza after it: joined. A sukūn or shadda after it, or alone in the opening letters
            // («أَلَٓمِّٓ»): necessary. A hamza carrying it («الْأٓخِرَةِ», hamza then alif): badal. A
            // hamza opening the next word: separated, written long.
            // Two hamzas meeting across words («هَٰؤُلَآ۟ إِن»): Qalun drops or eases the first, written
            // as the madd with a small zero (U+06DF) — his own rule, not a separated madd.
            if ((x.Has(Maddah) || x.Base == '\u0622') && x.Has(Zero) && endOfWord && next is not null && next.IsHamza)
            {
                Mark(HAMZATAN, x);
                taken.Add(i);
            }
            else if (x.Has(Maddah) || x.Base == '\u0622')
            {
                if (x.Has(HamzaAbove) || (!endOfWord && next!.IsHamza))         Mark(MUTTASIL, x);   // «اُ۬لْبَرِيَٓٔةِ»: the hamza on the madd letter
                else if (!endOfWord && (next!.Has(Sukun) || next.Has(Shadda)))   Mark(LAZIM, x);
                else if (x.IsHamza)                                              { }   // hamza then alif («الْأٓخِرَةِ»): badal, short in Qalun
                else if (endOfWord && next is not null && next.IsHamza)          Mark(MUNFASIL, x);
                else                                                             Mark(LAZIM, x);
                taken.Add(i);
            }

            // ---- a madd written with a small letter: the small alif ----------------------------------
            // (On an alif maqsura — «عَلَىٰ» — the madd is written with a full letter: left black.)
            if (x.Has(Dagger) && !x.Has(Maddah) && x.Base != '\u0649') Mark(SMALL_ALIF, x);

            // ---- the sila of the pronoun hā' («لَهُۥ», «بِهِۦ») ---------------------------------------
            if (x.Has(SmallWaw) || x.Has(SmallYeh)) Mark(SILA, x);

            // ---- nūn sākina and tanwin (not the verse's last letter: the verse ends on a stop) --------
            if (i == g.Count - 1) { }
            else if (x.Has(SmallMeem))
                Mark(IQLAB, x);
            else if ((x.Base == 'ن' && (x.Bare || x.Has(Sukun))) || x.OpenTanwin)
            {
                // Into the next word's first letter. The text writes the idgham two ways: «مَنْ يَّقُولُ»
                // (sukūn, then shadda) and «غِشَٰوَةٞ وَلَهُمْ» (open tanwin, no shadda on the wāw).
                var after    = At(i + 1);
                var nextWord = after is not null && after.Word != x.Word;
                var sakin    = x.Has(Sukun);
                if (after is not null && nextWord && GhunnaNext.IndexOf(after.Base) >= 0 && (!sakin || after.Has(Shadda)))
                {
                    if (!x.OpenTanwin) Mark(IDGHAM, x);
                    Mark(IDGHAM_GHUNNA, after);
                }
                else if (after is not null && nextWord && after.Base is 'ل' or 'ر' && (!sakin || after.Has(Shadda)))
                {
                    if (!x.OpenTanwin) Mark(IDGHAM, x);   // into lām or rā': no ghunna
                }
                else if (after is not null && !sakin && !after.Has(Shadda))
                {
                    // The tanwin of «كَثِيراٗ» is on the alif: the hidden sound is the letter's and the alif's.
                    if (x.OpenTanwin && x.Base is 'ا' or '\u0649' && i > 0 && SameWord(i - 1, i)) Mark(IKHFA, g[i - 1]);
                    Mark(IKHFA, x);
                }
            }

            // ---- mīm sākina -----------------------------------------------------------------------
            // (Merged into a doubled mīm, it is left black: the doubled mīm's ghunna carries it.)
            if (x.Base == 'م' && x.Bare && next is not null && next.Base == 'ب') Mark(IKHFA_SHAFAWI, x);

            // ---- the ghunna of a doubled nūn or mīm ----------------------------------------------------
            if (x.Base is 'ن' or 'م' && x.Has(Shadda)) Mark(GHUNNA, x);

            // ---- lām of al- before a sun letter («اَ۬لرَّحْمَٰنِ», «لِلنَّاسِ») -----------------------------
            if (x.Base == 'ل' && x.Bare && next is not null && SameWord(i, i + 1) && next.Has(Shadda)
                && i > 0 && SameWord(i - 1, i) && (g[i - 1].Base == 'ا' || (g[i - 1].Base == 'ل' && g[i - 1].Has(Kasra)))
                && !(next.Base == 'ل' && At(i + 2) is { Base: 'ه' } && SameWord(i, i + 2)))   // the name of God: left black
                Mark(LAM_SHAMSIYYA, x);

            // ---- written, not pronounced -----------------------------------------------------------
            if ((x.Base == 'ا' && x.Has(Sukun))
                || (x.Base == 'و' && x.Has(Sukun) && i > 0 && SameWord(i - 1, i) && Damm(i - 1))
                || (x.Has(Zero) && i > 0 && SameWord(i - 1, i) && !x.Has(Maddah) && x.Base != '\u0622'))
                Mark(SILENT, x);

            // ---- a consonant merged into the doubled letter after it («قَد تَّبَيَّنَ», «أَرَدتُّمْ») ------
            if (x.Bare && x.Base is not ('ن' or 'م' or 'ل' or 'ا' or '\u0649' or 'و' or 'ي' or '\u06D2' or '\u0640') && !x.IsHamza
                && !x.Dotted && !x.Has(SmallMeem) && next is not null && next.Has(Shadda))
                Mark(IDGHAM, x);

            // ---- Qalun's dotted marks: the eased hamza, the vowel read lightly ------------------------------
            if (x.Dotted && x.Base is not ('ا' or 'ٱ')) Mark(IKHTILAS, x);
            if (x.Dotted && x.Base == 'ا' && i > 0 && SameWord(i - 1, i) && g[i - 1].IsHamza) Mark(TASHIL, x);

            // ---- qalqala: a qalqala letter with sukūn, or ending the verse ------------------------------
            if (Qalqalah.IndexOf(x.Base) >= 0 && (x.Has(Sukun) || (i == g.Count - 1))) Mark(QALQALA, x);

            // ---- a heavy rā' ------------------------------------------------------------------------
            if (x.Base == 'ر' && HeavyRa(i)) Mark(TAFKHIM, x);
        }

        // ---- the madd before the stop that ends the verse ---------------------------------------------
        {
            var last = g.Count - 1;
            var prev = last - 1;
            if (prev >= 0 && g[prev].Word == lastWord && !taken.Contains(prev) && !(g[last].Base is 'ا' or '\u0649' && g[last].HasTanwin))
            {
                if (MaddLetter(prev) || (g[prev].Has(Dagger) && !g[prev].Has(Maddah))) Mark(ARID, g[prev]);
                else if (Leen(prev)) Mark(LEEN, g[prev]);
            }
        }

        return chosen.OrderBy(kv => kv.Key).Select(kv => new TajweedMark(kv.Value, g[kv.Key].Start, g[kv.Key].End)).ToList();

        // Rā' is heavy with fatha or damma and light with kasra; read with sukūn — written so, or at the
        // stop that ends the verse, whatever its vowel — it goes by what comes before it.
        bool HeavyRa(int i)
        {
            if (i != g.Count - 1)
            {
                if (Fath(i) || Damm(i)) return true;
                if (Kasr(i))            return false;
            }
            return !StopLight(i);
        }

        // Rā' read with sukūn: light after a kasra (one sākin letter between allowed: «حِجْرٍ») or a yā'
        // sākina — unless the kasra is a hamzat al-wasl's («اِ۪رْجِعُواْ») or a heavy letter follows in the
        // word («فِرْقَةٌ»); heavy otherwise («ٱلْقَدْرِ»).
        bool StopLight(int i)
        {
            var j = i - 1;
            if (j < 0 || !SameWord(j, i)) return false;
            if (g[j].Base is 'ي' or '\u06D2' && (g[j].Has(Sukun) || MaddLetter(j))) return true;
            // One sākin letter between (not a heavy one): look one further back («حِجْرٍ» light, «ٱلْقَدْرِ» heavy).
            if (g[j].Has(Sukun) && Isti3la.IndexOf(g[j].Base) < 0 && j - 1 >= 0 && SameWord(j - 1, i)) j--;
            if (!Kasr(j)) return false;
            if (g[j].Base is 'ا' or 'ٱ') return false;                      // the kasra of a hamzat al-wasl
            var k = i + 1;
            if (k < g.Count && SameWord(i, k) && Isti3la.IndexOf(g[k].Base) >= 0) return false;
            return true;
        }
    }

    /// <summary>The text as letters with their marks; pause marks and signs belong to none.</summary>
    private static List<G> Parse(string text)
    {
        var list = new List<G>();
        var word = 0;
        var inWord = false;
        G? current = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ' || c == '\u00A0')
            {
                if (inWord) word++;
                inWord  = false;
                current = null;
                continue;
            }
            if (IsLetter(c))
            {
                current = new G { Base = c, Start = i, End = i + 1, Word = word, Index = list.Count };
                list.Add(current);
                inWord = true;
            }
            else if (current is not null && !IsSign(c))
            {
                current.Marks += c;
                current.End = i + 1;
            }
        }
        return list;
    }

    private static bool IsLetter(char c) => (c >= 'ء' && c <= 'ي') || c is 'ٱ' or '\u06D2' or '\u0640';

    /// <summary>Pause marks (U+06D6–06DC), the rub' sign, the sajda sign, digits: not part of a letter.</summary>
    private static bool IsSign(char c) => (c >= '\u06D6' && c <= '\u06DC') || c is '\u06DE' or '\u06E9' || (c >= '\u0660' && c <= '\u0669') || c == '\u200F';
}
