namespace QuranKG.Dataset.Text;

/// <summary>
/// Finds the verses a hadith's Arabic quotes: runs of consecutive words shared with a verse, both
/// texts in the matching spelling (<see cref="Arabic.MatchTokens"/>).
///
/// A run counts when it is four words or more — or a whole verse of three (وانذر عشيرتك الاقربين) —
/// and is not a formula:
///   - it holds at least two words that are not particles or "Allah";
///   - under eight words, it is in at most <see cref="MaxVerses"/> verses ("يا ايها الذين امنوا" is in 89);
///   - under eight words, it is in at most <see cref="MaxHadiths"/> hadith ("من كان يؤمن بالله واليوم الآخر"
///     opens dozens: a formula, not a quotation of 65:2);
///   - under six words, it does not name the Prophet (رسول الله, النبي): that is the narrator speaking;
///   - it is not inside a longer run the same words make with another verse.
/// Six words or more, or the whole of a verse, is a quotation; four or five, a shared phrase — and so
/// is a run under eight words found in more than <see cref="QuotationMaxHadiths"/> hadith: a formula
/// in the Quran's words ("من كان يؤمن بالله واليوم الآخر", the basmala heading a letter).
///
/// A run the edition sets in braces (<c>{…}</c>, how Bukhari's and Muslim's editors mark the Quran) is a
/// quotation whatever its length or frequency, down to two words; the rules above are for the rest.
///
/// Measured against the English translators' bracketed references, which the detector never reads
/// (the corpus loader reports it): the Arabic of 94% of the hadith Bukhari's translator cites a
/// verse for quotes that verse, 82% for Muslim's (84% and 73% before the braces were read).
/// </summary>
public sealed class QuranQuotes
{
    public const int MinWords   = 4;
    public const int Quotation  = 6;
    public const int MaxVerses  = 3;
    public const int MaxHadiths = 25;
    /// <summary>Runs shorter than this are checked against the hadith: "من كان يؤمن بالله واليوم الآخر" (six words, 65:2) opens dozens of them.</summary>
    public const int Formula    = 8;
    /// <summary>A run under eight words in more hadith than this is graded a shared phrase, however long.</summary>
    public const int QuotationMaxHadiths = 10;

    private const int Gram = 4;

    private static readonly HashSet<string> Particles = new(StringComparer.Ordinal)
    {
        "الله", "ان", "لا", "من", "في", "علي", "الي", "ما", "و", "او", "ثم", "قد", "هو", "هي", "هم", "انه", "انا",
        "انما", "الا", "اذا", "لم", "لن", "عن", "كان", "قال", "الذي", "التي", "الذين", "ذلك", "هذا", "يا", "ايها",
        "لهم", "له", "لكم", "به", "بها", "فيه", "منه", "منهم",
    };

    public sealed record Match(int Verse, int HadithStart, int VerseStart, int Length);
    public sealed record Found(int Verse, int Words, string Quoted, bool IsQuotation);

    private readonly IReadOnlyList<IReadOnlyList<string>> _verses;
    private readonly Dictionary<string, List<(int Verse, int Pos)>> _grams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _threeWordVerses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(int Verse, int Pos)>> _shortGrams = new(StringComparer.Ordinal);
    private Dictionary<string, int> _hadithFrequency = new(StringComparer.Ordinal);

    /// <param name="verses">Each verse's words in the matching spelling, in mushaf order.</param>
    public QuranQuotes(IReadOnlyList<IReadOnlyList<string>> verses)
    {
        _verses = verses;
        for (var v = 0; v < verses.Count; v++)
        {
            var w = verses[v];
            for (var p = 0; p + Gram <= w.Count; p++) Add(_grams, Join(w, p, Gram), (v, p));
            if (w.Count == 3) Add(_threeWordVerses, Join(w, 0, 3), v);
            for (var n = 2; n < Gram; n++)
                for (var p = 0; p + n <= w.Count; p++) Add(_shortGrams, Join(w, p, n), (v, p));
        }
    }

    /// <summary>
    /// Finds the quotes of every hadith (each a list of words in the matching spelling). Two passes:
    /// the candidate runs, then how many hadith hold each short one, then the filter.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Found>> FindAll(IReadOnlyList<(IReadOnlyList<string> Words, IReadOnlyList<bool> Marked)> texts)
    {
        var hadiths    = texts.Select(t => t.Words).ToList();
        var candidates = texts.Select(t => Candidates(t.Words, t.Marked)).ToList();

        var shortRuns = new HashSet<string>(StringComparer.Ordinal);
        for (var h = 0; h < hadiths.Count; h++)
            foreach (var m in candidates[h].Where(m => m.Length < Formula))
                shortRuns.Add(Join(hadiths[h], m.HadithStart, m.Length));

        _hadithFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var words in hadiths)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var n = 3; n < Formula; n++)
                for (var i = 0; i + n <= words.Count; i++)
                {
                    var run = Join(words, i, n);
                    if (shortRuns.Contains(run) && seen.Add(run)) _hadithFrequency[run] = _hadithFrequency.GetValueOrDefault(run) + 1;
                }
        }

        return texts.Select((t, h) => Keep(t.Words, t.Marked, candidates[h])).ToList();
    }

    private List<Match> Candidates(IReadOnlyList<string> words, IReadOnlyList<bool> marked)
    {
        var found = new List<Match>();
        for (var i = 0; i + Gram <= words.Count; i++)
        {
            if (!_grams.TryGetValue(Join(words, i, Gram), out var hits)) continue;
            foreach (var (v, p) in hits)
            {
                var verse = _verses[v];
                if (i > 0 && p > 0 && words[i - 1] == verse[p - 1]) continue;     // part of a run already found
                var length = Gram;
                while (i + length < words.Count && p + length < verse.Count && words[i + length] == verse[p + length]) length++;
                found.Add(new Match(v, i, p, length));
            }
        }

        for (var i = 0; i + 3 <= words.Count; i++)
            if (_threeWordVerses.TryGetValue(Join(words, i, 3), out var vs))
                foreach (var v in vs) found.Add(new Match(v, i, 0, 3));

        // A run of two or three words the edition sets in braces: too short to find unmarked, but
        // the editors say it is the Quran (Bukhari's Book of Tafsir heads a hadith with a verse's
        // first words). Kept when it is in at most MaxVerses verses.
        for (var i = 0; i < words.Count; i++)
        {
            if (!marked[i] || (i > 0 && marked[i - 1])) continue;
            var end = i;
            while (end < words.Count && marked[end]) end++;
            var length = end - i;
            if (length < 2 || length >= Gram) continue;
            if (_shortGrams.TryGetValue(Join(words, i, length), out var hits) && hits.Count <= MaxVerses)
                foreach (var (v, p) in hits) found.Add(new Match(v, i, p, length));
        }

        return found;
    }

    private IReadOnlyList<Found> Keep(IReadOnlyList<string> words, IReadOnlyList<bool> marked, List<Match> candidates)
    {
        // A run the edition sets in braces: the editors say it is the Quran.
        bool Marked(Match m) => Enumerable.Range(m.HadithStart, m.Length).All(i => marked[i]);

        var kept = new List<Match>();
        foreach (var m in candidates)
        {
            if (candidates.Any(o => o.HadithStart <= m.HadithStart && m.HadithStart + m.Length <= o.HadithStart + o.Length && o.Length > m.Length)) continue;
            if (Marked(m)) { kept.Add(m); continue; }

            var run = words.Skip(m.HadithStart).Take(m.Length).ToList();
            if (run.Count(w => !Particles.Contains(w)) < 2) continue;
            if (m.Length < 8 && VersesHolding(run) > MaxVerses) continue;
            if (m.Length < Formula && _hadithFrequency.GetValueOrDefault(string.Join(' ', run)) > MaxHadiths) continue;
            if (m.Length < Quotation && Narration(run)) continue;

            kept.Add(m);
        }

        // A run under eight words that recurs in many hadith is the Prophet's or a narrator's formula
        // in the Quran's words ("من كان يؤمن بالله واليوم الآخر"): kept, as a shared phrase.
        bool Recurs(Match m) => m.Length < Formula && _hadithFrequency.GetValueOrDefault(Join(words, m.HadithStart, m.Length)) > QuotationMaxHadiths;

        return kept.GroupBy(m => m.Verse).Select(g =>
        {
            var covered = g.SelectMany(m => Enumerable.Range(m.VerseStart, m.Length)).Distinct().Count();
            var longest = g.OrderByDescending(m => m.Length).First();
            var whole   = covered >= _verses[g.Key].Count;
            var quotes  = g.Any(m => Marked(m) || (!Recurs(m) && (m.Length >= Quotation || whole)));
            return new Found(g.Key, covered, Join(words, longest.HadithStart, longest.Length), quotes);
        }).OrderBy(f => f.Verse).ToList();
    }

    /// <summary>A short run naming the Prophet is the narrator speaking ("فقال لهم رسول الله"), whatever verse shares it.</summary>
    private static bool Narration(IReadOnlyList<string> run)
    {
        for (var i = 0; i < run.Count; i++)
        {
            if (run[i] == "النبي" || run[i] == "والنبي") return true;
            if (i + 1 < run.Count && run[i + 1] == "الله" && (run[i] == "رسول" || run[i] == "ورسول" || run[i] == "برسول")) return true;
        }
        return false;
    }

    private int VersesHolding(IReadOnlyList<string> run)
    {
        if (run.Count < Gram) return _threeWordVerses.TryGetValue(string.Join(' ', run), out var vs) ? vs.Count : 0;
        if (!_grams.TryGetValue(Join(run, 0, Gram), out var hits)) return 0;
        var n = 0;
        foreach (var (v, p) in hits)
        {
            var verse = _verses[v];
            if (p + run.Count > verse.Count) continue;
            var same = true;
            for (var k = Gram; k < run.Count && same; k++) same = verse[p + k] == run[k];
            if (same) n++;
        }
        return n;
    }

    private static string Join(IReadOnlyList<string> words, int from, int count)
        => string.Join(' ', Enumerable.Range(from, count).Select(i => words[i]));

    private static void Add<T>(Dictionary<string, List<T>> into, string key, T value)
    {
        if (!into.TryGetValue(key, out var list)) into[key] = list = new List<T>();
        list.Add(value);
    }
}
