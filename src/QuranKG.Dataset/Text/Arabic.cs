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
