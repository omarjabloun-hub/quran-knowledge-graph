using System.Text.Json;
using QuranKG.Dataset.Text;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// The readings (riwayat) the Quran is read in here. Qalun from Nafi' is the main one — the reading
/// of Tunisia's mosques and schools — and Hafs from Asim, the corpus's own text, is one switch away.
///
/// A reading other than Hafs comes from the King Fahd Complex's text of it
/// (<c>data/raw/recitation/qaloun-kfgqpc.json</c> for Qalun): each verse in its own numbering, with
/// its page, juz and lines in that reading's mushaf. Everything the graph knows sits on Hafs's
/// verses and words (the morphology, the tafsirs, the hadith links, the passages), so each word of
/// the reading is linked to the word of Hafs it is (<see cref="Link"/>): the longest runs of words
/// the two texts share are matched, then the words left between two runs are paired by how alike
/// they are spelled — «مَلِكِ» with «مَٰلِكِ», «ءَٰا۬نذَرْتَهُمْ» with «ءَأَنذَرْتَهُمْ». A word with no counterpart
/// (Hafs's Bismillah of Al-Fatiha, a verse of Hafs and not of Qalun) is linked to nothing.
/// </summary>
public static class RiwayaSource
{
    public static readonly Riwaya Qalun = new()
    {
        Id          = "qalun",
        Name        = "Qalun from Nafi'",
        NameArabic  = "قالون عن نافع",
        Qari        = "Nafi' al-Madani (d. 169 AH)",
        Rawi        = "Qalun, Isa ibn Mina (d. 220 AH)",
        VerseCount  = "Madani",
        Region      = "Tunisia, Libya, parts of Mauritania",
        Description = "The reading of Tunisia and Libya. It counts the verses the Madani way (Al-Fatiha's first verse is «الحمد لله رب العالمين», «الم» is no verse of its own), reads «ملك يوم الدين», eases the second of two hamzas, and is written here as the King Fahd Complex prints it: the separated madd short and the plural mim without sila.",
        Attribution = "Qalun text, verse numbering and mushaf pages: King Fahd Glorious Quran Printing Complex (qurancomplex.gov.sa), via thetruetruth/quran-data-kfgqpc",
        Rank        = 1,
    };

    public static readonly Riwaya Hafs = new()
    {
        Id           = "hafs",
        Name         = "Hafs from Asim",
        NameArabic   = "حفص عن عاصم",
        Qari         = "Asim al-Kufi (d. 127 AH)",
        Rawi         = "Hafs ibn Sulayman (d. 180 AH)",
        VerseCount   = "Kufi",
        Region       = "the Mashriq, South Asia, most of the world",
        Description  = "The most widely read reading today, and the text the graph is built on: its verses carry the morphology, the tafsirs, the hadith links and the passages.",
        Attribution  = "Uthmani text: Tanzil.net; mushaf pages: the Madinah mushaf",
        Rank         = 2,
        IsCorpusText = true,
    };

    public static readonly IReadOnlyList<Riwaya> Readings = [Qalun, Hafs];

    public sealed record Result(IReadOnlyList<RiwayaVerse> Verses, int LinkedWords, int Words, IReadOnlyList<string> Problems);

    /// <param name="hafsWords">The corpus's words of each surah, in order.</param>
    public static Result Read(string path, Riwaya riwaya, IReadOnlyDictionary<int, IReadOnlyList<Word>> hafsWords)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing {path} — run `just fetch`.", path);

        var rows = new List<(int Surah, int Ayah, string Text, int Page, int Juz, int LineStart, int LineEnd)>();
        using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            foreach (var v in doc.RootElement.EnumerateArray())
                rows.Add((v.GetProperty("sura_no").GetInt32(), v.GetProperty("aya_no").GetInt32(), v.GetProperty("aya_text").GetString() ?? "",
                          int.Parse((v.GetProperty("page").ToString()).Split('-')[0]), v.GetProperty("jozz").GetInt32(),
                          v.GetProperty("line_start").GetInt32(), v.GetProperty("line_end").GetInt32()));

        var verses   = new List<RiwayaVerse>(rows.Count);
        var problems = new List<string>();
        int linked = 0, total = 0;

        foreach (var surah in rows.GroupBy(r => r.Surah).OrderBy(g => g.Key))
        {
            var ordered = surah.OrderBy(r => r.Ayah).ToList();
            var texts   = ordered.Select(r => WithoutNumber(r.Text)).ToList();
            var words   = texts.Select(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(Arabic.HasLetter).ToList()).ToList();
            var hafs    = hafsWords.GetValueOrDefault(surah.Key) ?? [];

            var links = Link(words.SelectMany(w => w).ToList(), hafs);
            var at    = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var mine = links.Skip(at).Take(words[i].Count).ToList();
                at += words[i].Count;

                var covered = mine.Where(l => l is not null).Select(l => l!.Value.Verse).Distinct().ToList();
                if (covered.Count == 0)
                {
                    problems.Add($"riwaya {riwaya.Id}: {surah.Key}:{ordered[i].Ayah} has no word in common with the corpus");
                    continue;
                }

                linked += mine.Count(l => l is not null);
                total  += mine.Count;
                verses.Add(new RiwayaVerse
                {
                    Riwaya     = riwaya.Id,
                    Surah      = surah.Key,
                    Ayah       = ordered[i].Ayah,
                    Text       = texts[i],
                    Page       = ordered[i].Page,
                    Juz        = ordered[i].Juz,
                    LineStart  = ordered[i].LineStart,
                    LineEnd    = ordered[i].LineEnd,
                    Words      = mine,
                    HafsVerses = covered,
                });
            }
        }

        return new Result(verses, linked, total, problems);
    }

    /// <summary>The verse's text without the number it ends with («… لِّلْمُتَّقِينَ ١»).</summary>
    internal static string WithoutNumber(string text)
    {
        var t = (text ?? "").Replace(' ', ' ').Replace("‏", "").Trim();
        var end = t.Length;
        while (end > 0 && (char.IsWhiteSpace(t[end - 1]) || (t[end - 1] >= '٠' && t[end - 1] <= '٩'))) end--;
        return t[..end];
    }

    /// <summary>
    /// Each word of a reading's surah linked to the corpus's word it is, or null. The shared runs
    /// first (<see cref="RecitationSource.MatchingBlocks"/>), then each gap between two runs paired
    /// in order by spelling (<see cref="PairGap"/>).
    /// </summary>
    internal static List<WordLink?> Link(IReadOnlyList<string> words, IReadOnlyList<Word> hafs)
    {
        var a = words.Select(Match).ToList();
        var b = hafs.Select(w => Match(w.TextArabic)).ToList();

        var link = new int[a.Count];
        Array.Fill(link, -1);
        var blocks = RecitationSource.MatchingBlocks(a, b);
        foreach (var (i, j, k) in blocks)
            for (var n = 0; n < k; n++) link[i + n] = j + n;

        // The gaps: before the first run, between two runs, after the last.
        int ai = 0, bj = 0;
        foreach (var (i, j, k) in blocks.Append((a.Count, b.Count, 0)))
        {
            if (i > ai && j > bj) PairGap(a, ai, i, b, bj, j, link);
            ai = i + k; bj = j + k;
        }

        return link.Select(j => j < 0 ? (WordLink?)null : new WordLink(hafs[j].Verse, hafs[j].Position)).ToList();
    }

    /// <summary>
    /// The words of a[alo..ahi) and b[blo..bhi) paired in order, each pair as alike in spelling as
    /// can be (half the letters at least): the best monotonic pairing, by dynamic programming.
    /// </summary>
    private static void PairGap(List<string> a, int alo, int ahi, List<string> b, int blo, int bhi, int[] link)
    {
        int m = ahi - alo, n = bhi - blo;
        if ((long)m * n > 250_000) return;   // a gap this long is two different texts, not variants

        var score = new double[m + 1, n + 1];
        for (var i = 1; i <= m; i++)
            for (var j = 1; j <= n; j++)
            {
                var s    = Similarity(a[alo + i - 1], b[blo + j - 1]);
                var pair = s >= 0.5 ? score[i - 1, j - 1] + s : double.MinValue;
                score[i, j] = Math.Max(pair, Math.Max(score[i - 1, j], score[i, j - 1]));
            }

        for (int i = m, j = n; i > 0 && j > 0;)
        {
            var s = Similarity(a[alo + i - 1], b[blo + j - 1]);
            if (s >= 0.5 && Math.Abs(score[i, j] - (score[i - 1, j - 1] + s)) < 1e-9) { link[alo + i - 1] = blo + j - 1; i--; j--; }
            else if (score[i - 1, j] >= score[i, j - 1]) i--;
            else j--;
        }
    }

    /// <summary>A word in the spelling two texts are compared in (<see cref="RecitationSource"/>'s).</summary>
    public static string Match(string word) => string.Concat(RecitationSource.Tokens(word));

    private static double Similarity(string x, string y)
    {
        if (x.Length == 0 || y.Length == 0) return x == y ? 1 : 0;
        var prev = new int[y.Length + 1];
        var cur  = new int[y.Length + 1];
        for (var i = 1; i <= x.Length; i++)
        {
            for (var j = 1; j <= y.Length; j++)
                cur[j] = x[i - 1] == y[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
            Array.Clear(cur);
        }
        return 2.0 * prev[y.Length] / (x.Length + y.Length);
    }
}
