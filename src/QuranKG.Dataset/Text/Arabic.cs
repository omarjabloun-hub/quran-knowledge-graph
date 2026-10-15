using System.Text;
using System.Text.RegularExpressions;

namespace QuranKG.Dataset.Text;

/// <summary>Arabic text helpers shared by the readers, the validator and the tests.</summary>
public static partial class Arabic
{
    /// <summary>The plain-text form of the Bismillah, as Tanzil's Simple-Clean text writes it.</summary>
    public const string BismillahPlain = "بسم الله الرحمن الرحيم";

    // Harakat and tanween (064B–065F), superscript alef (0670), Quranic annotation signs
    // (06D6–06ED, 0610–061A), tatweel (0640).
    [GeneratedRegex("[ؐ-ًؚ-ٰٟۖ-ۭـ]")]
    private static partial Regex TashkeelPattern();

    [GeneratedRegex("[ء-يٱ]")]
    private static partial Regex ArabicLetterPattern();

    /// <summary>Removes diacritics, Quranic marks and tatweel; maps alef wasla to a plain alef.</summary>
    public static string StripTashkeel(string text)
        => TashkeelPattern().Replace(text, "").Replace('ٱ', 'ا');

    /// <summary>True when the token contains at least one Arabic letter (pause marks and hizb signs do not).</summary>
    public static bool HasLetter(string token) => ArabicLetterPattern().IsMatch(token);

    /// <summary>
    /// The letters of a word only, for comparing two spellings of the same word: diacritics and
    /// marks removed, a tatweel-borne hamza written as a hamza, alef wasla as alef.
    /// </summary>
    public static string Skeleton(string text)
    {
        var s  = text.Replace("ـٔ", "ء").Replace("ٔ", "ء");
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == 'ٱ') sb.Append('ا');
            else if ((c >= 'ء' && c <= 'غ') || (c >= 'ف' && c <= 'ي')) sb.Append(c);
        }
        return sb.ToString();
    }

    // Diacritics, Quranic marks, tatweel and the directional marks hadith texts carry (200C–200F).
    [GeneratedRegex("[\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED\u0640\u200C-\u200F]")]
    private static partial Regex MatchNoisePattern();

    [GeneratedRegex("[\u0621-\u064A]+")]
    private static partial Regex MatchTokenPattern();

    /// <summary>
    /// The words of an Arabic text in the one spelling two texts are compared in: no diacritics,
    /// every alef form as ا, ى as ي, ة as ه, ؤ as و, ئ as ي; punctuation and digits dropped. A
    /// verse's Simple-Clean text and a hadith quoting it come out the same.
    /// </summary>
    public static IReadOnlyList<string> MatchTokens(string text)
    {
        var t = MatchNoisePattern().Replace(text ?? "", "");
        var sb = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            sb.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _   => c,
            });
        }
        return MatchTokenPattern().Matches(sb.ToString()).Select(m => m.Value).ToList();
    }

    /// <summary>
    /// <see cref="MatchTokens"/>, each word marked when it stands between braces — how the editions
    /// of Bukhari and Muslim set a verse of the Quran apart: <c>{اقرأ باسم ربك الذي خلق}</c>.
    /// </summary>
    public static (IReadOnlyList<string> Words, IReadOnlyList<bool> Marked) MatchTokensMarked(string text)
    {
        var words  = new List<string>();
        var marked = new List<bool>();
        var depth  = 0;
        var start  = 0;
        var t      = text ?? "";
        for (var i = 0; i <= t.Length; i++)
        {
            if (i < t.Length && t[i] != '{' && t[i] != '}') continue;
            foreach (var w in MatchTokens(t[start..i])) { words.Add(w); marked.Add(depth > 0); }
            if (i < t.Length) depth = t[i] == '{' ? depth + 1 : Math.Max(0, depth - 1);
            start = i + 1;
        }
        return (words, marked);
    }

    private static readonly string[] Suffixes = { "هما", "كما", "هم", "هن", "كم", "كن", "نا", "ها", "ه", "ك", "ي" };

    /// <summary>
    /// A word in the matching spelling without what it carries — و/ف, the article (ال، بال، لل) and
    /// an attached pronoun (ـه، ـها، ـهم، ـكم، ـنا …) — so جاره and الجار, ضيفه and ضيف count as one
    /// word. Light and blunt, for counting shared words, never for display. The same rule is
    /// written in the endpoints' library (QK.Stem); change both.
    /// </summary>
    public static string Stem(string word)
    {
        var w = word ?? "";
        if (w is "الله" or "لله" or "بالله" or "والله" or "فالله" or "تالله") return "الله";
        if (w.Length > 3 && (w[0] == 'و' || w[0] == 'ف')) w = w[1..];
        if (w.Length > 4 && (w.StartsWith("بال", StringComparison.Ordinal) || w.StartsWith("كال", StringComparison.Ordinal))) w = w[3..];
        else if (w.Length > 3 && w.StartsWith("ال", StringComparison.Ordinal)) w = w[2..];
        else if (w.Length > 4 && w.StartsWith("لل", StringComparison.Ordinal)) w = w[2..];
        foreach (var suffix in Suffixes)
            if (w.Length - suffix.Length >= 3 && w.EndsWith(suffix, StringComparison.Ordinal)) { w = w[..^suffix.Length]; break; }
        return w;
    }

    /// <summary>The word tokens of a Tanzil line: pause marks, rub-el-hizb and sajda signs dropped.</summary>
    public static IReadOnlyList<string> WordTokens(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(HasLetter).ToList();

    /// <summary>
    /// Removes the Bismillah that Tanzil prefixes to the first verse of every surah but 1 and 9.
    /// Returns the text unchanged (and false) when it does not start with one.
    /// </summary>
    public static (string Text, bool Stripped) StripBismillahPrefix(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var seen   = 0;
        var cut    = -1;
        var letters = new List<string>(4);

        for (var i = 0; i < tokens.Length && seen < 4; i++)
        {
            if (!HasLetter(tokens[i])) continue;
            letters.Add(Skeleton(tokens[i]));
            seen++;
            if (seen == 4) cut = i + 1;
        }

        if (cut < 0 || string.Join(' ', letters) != BismillahPlain) return (text, false);

        return (string.Join(' ', tokens.Skip(cut)), true);
    }
}
