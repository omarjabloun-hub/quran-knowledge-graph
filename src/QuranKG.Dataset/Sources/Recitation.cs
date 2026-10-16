using System.Globalization;
using System.Text.Json;
using QuranKG.Dataset.Text;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// Recitations: each surah's recording and where each verse starts in it, so a reader can follow
/// the verse being recited.
///
/// Sheikh Mohamed Latrech — "Hamadi" in Tunisia — recites in the reading of Qalun from Nafi', the
/// reading of Tunisia's mosques, and its recordings count the verses the Madani way: 6,214 verses,
/// not the 6,236 of the corpus's Hafs text. Al-Fatiha's first verse is «الحمد لله رب العالمين» and
/// its seventh starts at «غير المغضوب عليهم»; Al-Baqarah has 285 verses, Al-Ma'idah 122. So the
/// recording's verse marks (qaloun.app: <c>data/raw/recitation/{reciter}/{surah}.json</c>) cannot be
/// read as the corpus's verses. They are carried over through the text: the King Fahd Complex's
/// Qalun text (<c>data/raw/recitation/qaloun-kfgqpc.json</c>) numbers its verses as the recording
/// does, its words are aligned with the corpus's (<see cref="Align"/>), and each recorded verse's
/// start becomes a word of the corpus. A corpus verse that starts where a recorded verse starts
/// takes its time exactly (6,156 of 6,236); one that starts inside a recorded verse is placed by
/// the letters recited before it (2:2 «ذلك الكتاب»: Qalun does not count «الم» as a verse, so its
/// 2:1 runs on to «هدى للمتقين»).
/// </summary>
public static class RecitationSource
{
    public static readonly IReadOnlyList<Reciter> Reciters =
    [
        new()
        {
            Id           = "mohamed-latrech",
            Name         = "Mohamed (Hamadi) Latrech",
            NameArabic   = "محمد الأطرش",
            Riwaya       = "Qalun from Nafi'",
            RiwayaArabic = "قالون عن نافع",
            Country      = "Tunisia",
            Style        = "Murattal",
            Source       = "qaloun.app",
            SourceUrl    = "https://qaloun.app/en/reciters/mohamed-latrech",
            Attribution  = "Recitation by Sheikh Mohamed Latrech; audio and verse timings from qaloun.app. Qalun text and verse numbering: King Fahd Glorious Quran Printing Complex.",
        },
    ];

    public sealed record Result(IReadOnlyList<Recitation> Recitations, int Exact, int Estimated, IReadOnlyList<string> Problems);

    /// <param name="hafs">Each surah's verses in the corpus, in order, as Simple-Clean text (no Bismillah prefix).</param>
    public static Result Read(string qalounTextPath, string timingsDir, Reciter reciter, IReadOnlyDictionary<int, IReadOnlyList<string>> hafs)
    {
        var qalun = QalunVerses(qalounTextPath);
        var recitations = new List<Recitation>();
        var problems = new List<string>();
        int exact = 0, estimated = 0;

        for (var surah = 1; surah <= 114; surah++)
        {
            var path = Path.Combine(timingsDir, surah.ToString(CultureInfo.InvariantCulture) + ".json");
            if (!File.Exists(path)) { problems.Add($"{reciter.Id}: no timings for surah {surah}"); continue; }

            var (audio, duration, marks) = Timings(path);
            var recorded = qalun.GetValueOrDefault(surah) ?? [];
            if (marks.Count != recorded.Count)
            {
                problems.Add($"{reciter.Id}: surah {surah} has {marks.Count} verse marks, the Qalun text {recorded.Count} verses");
                continue;
            }
            if (!hafs.TryGetValue(surah, out var verses) || verses.Count == 0) continue;

            var timed = Time(verses, recorded, marks, duration);
            exact     += timed.Count(v => v.Exact);
            estimated += timed.Count(v => !v.Exact);

            recitations.Add(new Recitation
            {
                Reciter        = reciter.Id,
                Surah          = surah,
                AudioUrl       = audio,
                Duration       = duration,
                RecordedVerses = recorded.Count,
                Marks          = marks,
                Verses         = timed,
            });
        }

        return new Result(recitations, exact, estimated, problems);
    }

    /// <summary>
    /// The corpus's verses timed against a recording. <paramref name="recorded"/> is the recording's
    /// text verse by verse, <paramref name="marks"/> where each of its verses starts (seconds).
    /// </summary>
    internal static List<RecitedVerse> Time(IReadOnlyList<string> verses, IReadOnlyList<string> recorded, IReadOnlyList<double> marks, double duration)
    {
        // The corpus's words, and where each verse starts among them.
        var words  = new List<string>();
        var starts = new List<int>();
        foreach (var verse in verses)
        {
            starts.Add(words.Count);
            words.AddRange(Tokens(verse));
        }

        // Letters before each word: what an estimated time is proportional to.
        var letters = new int[words.Count + 1];
        for (var i = 0; i < words.Count; i++) letters[i + 1] = letters[i] + words[i].Length;

        // Where each recorded verse starts, as a word of the corpus — kept in order.
        var recordedStarts = Align(words, recorded.Select(r => (IReadOnlyList<string>)Tokens(r)).ToList());
        for (var q = 1; q < recordedStarts.Count; q++) recordedStarts[q] = Math.Max(recordedStarts[q], recordedStarts[q - 1]);

        var timed = new List<RecitedVerse>(verses.Count);
        for (var v = 0; v < verses.Count; v++)
        {
            var w = starts[v];
            double start;
            bool   isExact;

            if (recordedStarts.Count == 0 || w < recordedStarts[0])
            {
                // Before the recording's first verse: Al-Fatiha's Bismillah, a verse of Hafs and
                // not of Qalun, recited from the start.
                start = 0; isExact = true;
            }
            else
            {
                var q = recordedStarts.FindLastIndex(s => s <= w);
                if (recordedStarts[q] == w) { start = marks[q]; isExact = true; }
                else
                {
                    var fromWord = recordedStarts[q];
                    var toWord   = q + 1 < recordedStarts.Count ? recordedStarts[q + 1] : words.Count;
                    var toTime   = q + 1 < marks.Count ? marks[q + 1] : duration;
                    var share    = letters[toWord] == letters[fromWord] ? 0 : (double)(letters[w] - letters[fromWord]) / (letters[toWord] - letters[fromWord]);
                    start   = marks[q] + share * (toTime - marks[q]);
                    isExact = false;
                }
            }

            timed.Add(new RecitedVerse(v + 1, Math.Round(start, 3), 0, isExact));
        }

        for (var v = 0; v < timed.Count; v++)
            timed[v] = timed[v] with { End = v + 1 < timed.Count ? timed[v + 1].Start : Math.Round(duration, 3) };

        return timed;
    }

    // ---- aligning two texts word by word ---------------------------------------------------------

    /// <summary>
    /// The word of <paramref name="words"/> (the corpus's text of a surah) each verse of
    /// <paramref name="verses"/> (another text of it, its own verse division) starts at.
    ///
    /// The two texts differ in spelling and, here and there, in a word (Qalun reads «ملك يوم الدين»,
    /// Hafs «مالك»): the longest runs of words they share are matched (Ratcliff–Obershelp, as
    /// Python's difflib), and a verse whose first word is unmatched starts at the word, between the
    /// matched words around it, spelled most like it. The corpus writes «يا أيها» as two words and
    /// the Uthmani script as one: a vocative particle is joined to the word after it for the match.
    /// </summary>
    internal static List<int> Align(IReadOnlyList<string> words, IReadOnlyList<IReadOnlyList<string>> verses)
    {
        // The corpus's words with each vocative joined to the next; Original maps back.
        var joined   = new List<string>();
        var original = new List<int>();
        for (var i = 0; i < words.Count; i++)
        {
            if (Vocatives.Contains(words[i]) && i + 1 < words.Count) { joined.Add(words[i] + words[i + 1]); original.Add(i); i++; }
            else { joined.Add(words[i]); original.Add(i); }
        }
        original.Add(words.Count);

        var other  = verses.SelectMany(v => v).ToList();
        var firsts = new List<int>();
        for (int v = 0, n = 0; v < verses.Count; n += verses[v].Count, v++) firsts.Add(n);

        var match = new int[other.Count];
        Array.Fill(match, -1);
        foreach (var (a, b, size) in MatchingBlocks(other, joined))
            for (var k = 0; k < size; k++) match[a + k] = b + k;

        var result = new List<int>(verses.Count);
        foreach (var i in firsts)
        {
            if (i >= other.Count) { result.Add(words.Count); continue; }
            if (match[i] >= 0) { result.Add(original[match[i]]); continue; }

            var p = i - 1;
            while (p >= 0 && match[p] < 0) p--;
            var n = i + 1;
            while (n < other.Count && match[n] < 0) n++;
            var lo = p >= 0 ? match[p] + 1 : 0;
            var hi = n < other.Count ? match[n] : joined.Count;

            if (lo >= hi) { result.Add(original[Math.Min(hi, joined.Count)]); continue; }

            var best = lo;
            var bestScore = -1.0;
            for (var h = lo; h < hi; h++)
            {
                var score = Similarity(other[i], joined[h]);
                if (score > bestScore) { bestScore = score; best = h; }
            }
            result.Add(original[best]);
        }
        return result;
    }

    private static readonly HashSet<string> Vocatives = new(StringComparer.Ordinal) { "يا", "ويا", "ها" };

    /// <summary>The runs (start in a, start in b, length) two sequences share, longest first, recursively on each side — difflib's matching blocks without its junk heuristic.</summary>
    internal static List<(int A, int B, int Size)> MatchingBlocks(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var b2j = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var j = 0; j < b.Count; j++)
        {
            if (!b2j.TryGetValue(b[j], out var list)) b2j[b[j]] = list = [];
            list.Add(j);
        }

        var blocks = new List<(int, int, int)>();
        var queue  = new Stack<(int, int, int, int)>();
        queue.Push((0, a.Count, 0, b.Count));
        while (queue.Count > 0)
        {
            var (alo, ahi, blo, bhi) = queue.Pop();
            var (i, j, k) = LongestMatch(a, b2j, alo, ahi, blo, bhi);
            if (k == 0) continue;
            blocks.Add((i, j, k));
            if (alo < i && blo < j) queue.Push((alo, i, blo, j));
            if (i + k < ahi && j + k < bhi) queue.Push((i + k, ahi, j + k, bhi));
        }
        blocks.Sort();
        return blocks;
    }

    private static (int I, int J, int K) LongestMatch(IReadOnlyList<string> a, Dictionary<string, List<int>> b2j, int alo, int ahi, int blo, int bhi)
    {
        int bestI = alo, bestJ = blo, bestK = 0;
        var lengths = new Dictionary<int, int>();
        for (var i = alo; i < ahi; i++)
        {
            var next = new Dictionary<int, int>();
            if (b2j.TryGetValue(a[i], out var js))
                foreach (var j in js)
                {
                    if (j < blo) continue;
                    if (j >= bhi) break;
                    var k = lengths.GetValueOrDefault(j - 1) + 1;
                    next[j] = k;
                    if (k > bestK) { bestI = i - k + 1; bestJ = j - k + 1; bestK = k; }
                }
            lengths = next;
        }
        return (bestI, bestJ, bestK);
    }

    /// <summary>How alike two words are spelled: twice their longest common subsequence of letters over their total length.</summary>
    private static double Similarity(string x, string y)
    {
        if (x.Length == 0 || y.Length == 0) return 0;
        var prev = new int[y.Length + 1];
        var cur  = new int[y.Length + 1];
        for (var i = 1; i <= x.Length; i++)
        {
            for (var j = 1; j <= y.Length; j++)
                cur[j] = x[i - 1] == y[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
        }
        return 2.0 * prev[y.Length] / (x.Length + y.Length);
    }

    /// <summary>
    /// A text's words in the matching spelling. The King Fahd Complex's Qalun text writes some
    /// yā' as ے and carries marks <see cref="Arabic.MatchTokens"/> does not know (U+08F0–08FF).
    /// </summary>
    public static List<string> Tokens(string text)
    {
        var t = new string((text ?? "").Where(c => c < 'ࣰ' || c > 'ࣿ').Select(c => c == 'ے' ? 'ي' : c).ToArray());
        return Arabic.MatchTokens(t).Select(w => w.Replace("ء", "")).Where(w => w.Length > 0).ToList();
    }

    // ---- reading the files ------------------------------------------------------------------

    /// <summary>The King Fahd Complex's Qalun text: each surah's verses, in its own numbering.</summary>
    private static Dictionary<int, List<string>> QalunVerses(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing {path} — run `just fetch`.", path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var bySurah = new Dictionary<int, List<(int Ayah, string Text)>>();
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            var surah = v.GetProperty("sura_no").GetInt32();
            if (!bySurah.TryGetValue(surah, out var list)) bySurah[surah] = list = [];
            list.Add((v.GetProperty("aya_no").GetInt32(), v.GetProperty("aya_text").GetString() ?? ""));
        }
        return bySurah.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(x => x.Ayah).Select(x => x.Text).ToList());
    }

    /// <summary>
    /// A surah's recording: the fetcher keeps <c>"audioUrl":…,"downloadName":…,"ayahMarks":[{number,
    /// start}…],"knownDuration":…</c> from its page — the members of an object, without its braces.
    /// </summary>
    private static (string Audio, double Duration, List<double> Marks) Timings(string path)
    {
        using var doc = JsonDocument.Parse("{" + File.ReadAllText(path) + "}");
        var root  = doc.RootElement;
        var marks = root.GetProperty("ayahMarks").EnumerateArray()
                        .Select(m => (Number: m.GetProperty("number").GetInt32(), Start: m.GetProperty("start").GetDouble()))
                        .OrderBy(m => m.Number).Select(m => m.Start).ToList();
        return (root.GetProperty("audioUrl").GetString() ?? "", root.GetProperty("knownDuration").GetDouble(), marks);
    }
}
