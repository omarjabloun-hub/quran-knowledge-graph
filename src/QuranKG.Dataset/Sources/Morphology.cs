using System.Globalization;
using QuranKG.Dataset.Text;

namespace QuranKG.Dataset.Sources;

/// <summary>
/// The Quranic Arabic Corpus morphology (mustafa0x's Arabic-script fork of v0.4). One line per
/// segment: <c>surah:ayah:word:segment  form  tag  features</c>, e.g.
/// <c>1:1:3:2	رَّحْمَٰنِ	N	ROOT:رحم|LEM:رَحْمٰن|MS|GEN|ADJ</c>.
///
/// A word's root and lemma are taken from its <b>stem</b> only. Prefix and suffix lemmas
/// (wa-, bi-, al-, pronoun endings) would otherwise become hubs linked to nearly every verse.
/// </summary>
public static class Morphology
{
    public static IReadOnlyList<Word> Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Missing source file {path} — run `just fetch`.", path);

        var segments = new SortedDictionary<(int S, int A, int W), List<(int G, Segment Seg)>>();
        foreach (var raw in File.ReadLines(path))
        {
            if (raw.Length == 0 || raw[0] == '#') continue;

            var parts = raw.Split('\t');
            if (parts.Length != 4) throw new FormatException($"morphology: expected 4 tab-separated columns: {raw}");

            var loc = parts[0].Split(':');
            if (loc.Length != 4) throw new FormatException($"morphology: bad location '{parts[0]}'");

            var s = int.Parse(loc[0], CultureInfo.InvariantCulture);
            var a = int.Parse(loc[1], CultureInfo.InvariantCulture);
            var w = int.Parse(loc[2], CultureInfo.InvariantCulture);
            var g = int.Parse(loc[3], CultureInfo.InvariantCulture);

            var features = parts[3].Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (!segments.TryGetValue((s, a, w), out var list)) segments[(s, a, w)] = list = [];
            list.Add((g, new Segment(parts[1], parts[2], features)));
        }

        var words = new List<Word>(segments.Count);
        foreach (var ((s, a, w), list) in segments)
        {
            var segs = list.OrderBy(x => x.G).Select(x => x.Seg).ToList();
            words.Add(ToWord(new VerseRef(s, a), w, segs));
        }

        return words;
    }

    internal static Word ToWord(VerseRef verse, int position, IReadOnlyList<Segment> segs)
    {
        var stems = segs.Where(x => x.IsStem).ToList();
        // A few words carry two stems (e.g. يومئذ split in two); the one with a root is the word's.
        var stem  = stems.FirstOrDefault(x => Feature(x, "ROOT:") is not null) ?? stems.FirstOrDefault() ?? segs[0];

        var root  = Feature(stem, "ROOT:");
        var lemma = Feature(stem, "LEM:");
        var text  = string.Concat(segs.Select(x => x.Form));
        var vf    = Feature(stem, "VF:");

        return new Word
        {
            Verse        = verse,
            Position     = position,
            TextArabic   = text,
            TextPlain    = Arabic.StripTashkeel(text),
            Segments     = segs,
            Root         = root,
            Lemma        = lemma,
            LemmaKey     = lemma is null ? null : LemmaKeyOf(lemma, root, stem.Tag),
            PosTag       = stem.Tag,
            PartOfSpeech = PartOfSpeech(stem),
            Features     = string.Join('|', stem.Features.Where(f => !f.StartsWith("ROOT:", StringComparison.Ordinal) && !f.StartsWith("LEM:", StringComparison.Ordinal))),
            VerbForm     = int.TryParse(vf, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0,
        };
    }

    public static string LemmaKeyOf(string lemma, string? root, string posTag) => $"{lemma}|{root ?? ""}|{posTag}";

    private static string? Feature(Segment seg, string prefix)
        => seg.Features.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static readonly (string Code, string Name)[] NounKinds =
    [
        ("PN", "Proper noun"), ("PRON", "Pronoun"), ("DEM", "Demonstrative pronoun"), ("REL", "Relative pronoun"),
        ("ADJ", "Adjective"), ("ACT_PCPL", "Active participle"), ("PASS_PCPL", "Passive participle"),
        ("VN", "Verbal noun"), ("NV", "Verbal noun (ism fiʿl)"), ("T", "Time adverb"), ("LOC", "Location adverb"),
        ("INTG", "Interrogative noun"), ("COND", "Conditional noun"),
    ];

    private static readonly Dictionary<string, string> ParticleKinds = new(StringComparer.Ordinal)
    {
        ["P"] = "Preposition", ["NEG"] = "Negative particle", ["ACC"] = "Accusative particle",
        ["COND"] = "Conditional particle", ["CONJ"] = "Coordinating conjunction", ["SUB"] = "Subordinating conjunction",
        ["RES"] = "Restriction particle", ["CERT"] = "Particle of certainty", ["PRO"] = "Prohibition particle",
        ["RET"] = "Retraction particle", ["EXP"] = "Exceptive particle", ["INTG"] = "Interrogative particle",
        ["EXL"] = "Explanation particle", ["AMD"] = "Amendment particle", ["ANS"] = "Answer particle",
        ["INC"] = "Inceptive particle", ["INT"] = "Particle of interpretation", ["FUT"] = "Future particle",
        ["INL"] = "Quranic initials", ["VOC"] = "Vocative particle", ["EMPH"] = "Emphatic particle",
        ["SUP"] = "Supplemental particle", ["CIRC"] = "Circumstantial particle", ["RSLT"] = "Result particle",
        ["REM"] = "Resumption particle", ["PREV"] = "Preventive particle", ["CAUS"] = "Particle of cause",
        ["EQ"] = "Equalization particle", ["AVR"] = "Aversion particle", ["SUR"] = "Surprise particle",
        ["ATT"] = "Attention particle",
    };

    /// <summary>A reader-friendly part of speech for a stem segment.</summary>
    public static string PartOfSpeech(Segment stem)
    {
        var f = stem.Features;
        switch (stem.Tag)
        {
            case "V":
                if (f.Contains("PERF")) return "Perfect verb";
                if (f.Contains("IMPF")) return "Imperfect verb";
                if (f.Contains("IMPV")) return "Imperative verb";
                return "Verb";
            case "N":
                foreach (var (code, name) in NounKinds)
                    if (f.Contains(code)) return name;
                return "Noun";
            default:
                var first = f.FirstOrDefault(x => x is not ("PREF" or "SUFF") && !x.Contains(':'));
                return first is not null && ParticleKinds.TryGetValue(first, out var p) ? p : "Particle";
        }
    }
}
