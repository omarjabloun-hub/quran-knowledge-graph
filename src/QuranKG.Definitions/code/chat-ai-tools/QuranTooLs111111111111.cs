[tools: Curiosity.ChatAITools.UID("QuranTooLs111111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Quran Tools")]
[tools: Curiosity.ChatAITools.Description("Reads and counts the Quran's Arabic from the graph: counts a lemma, root, word form or text (CountTerm, with every verse it counted and how many other lemmas or roots share that count), finds the verses where terms occur together (FindVerses), reads verses with their words' morphology (ReadVerses), lists the lemmas or roots that occur an exact number of times (SameCount), and ranks similar verses and similar surahs (SimilarVerses, SimilarSurahs). Use these for any question about how often, where, or with what a word occurs, INSTEAD OF answering from memory. Every verse row carries its key and uid.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-book-alt")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// The assistant's instruments for studying the Quran: the counts and the readings a claim about
// the text stands or falls on, computed live from the graph the dashboard reads.
//
// One snapshot of the corpus — the 77,429 words with their verse, lemma and root, the 6,236 verses,
// the lemmas and roots — is read once and kept (static) until the number of words changes. Every
// tool answers from it in milliseconds. It holds what the morphology says and nothing else.
//
// THE ANSWER KEYS STAY OUT (CLAUDE.md, non-negotiable 2): nothing here reads QurSimPair, Topic,
// Verse.Ruku or Surah.RukuCount, and SimilarVerses / SimilarSurahs call qkg/verse/similar and
// qkg/surah/similar, which do not either. qkg/verse/words returns QurSim pairs: never call it here.
//
// COUNTING RULES are stated in every answer, because a count without its rule is not a fact:
//   lemma  words whose lemma (Quranic Arabic Corpus) is the term — ابن، أبناء، بنين are one lemma
//   root   words built on the root — every lemma of the family
//   form   words whose letters, diacritics removed, are exactly the term — a prefix (و، ب، ال) or
//          a suffix makes another form, so "form" is the strictest and the most rule-dependent count
//   text   occurrences of the letters inside the verses' plain Arabic (Verse.TextSimple), across word
//          boundaries — the loosest
// Matching ignores diacritics and folds alef forms (أ إ آ ٱ → ا) and ى → ي, so a reader may type
// آدم, ءادم or ادم.

[ToolSystemPrompt("""
                  Counts, places and co-occurrences in the Quran come from the Quran tools, never
                  from memory or a commentary: the tools read every word of the Quranic Arabic
                  Corpus. State the counting rule with every number (lemma, root, form or text) —
                  the same word gives different counts under different rules, and a claim is only
                  as good as the rule it was counted with. When two counts look remarkable, report
                  how many other lemmas or roots share that count (CountTerm returns it) before
                  calling it significant. Every verse row carries its key (2:31) and uid: consult
                  the uid to read the verse in full.
                  """)]
public class QuranTools
{
    // ── the snapshot ─────────────────────────────────────────────────────────────────────────

    private sealed class Verse
    {
        public UID128 Uid; public string Key; public int Surah; public int Ayah; public int Index;
        public string Uthmani; public string Simple; public string Folded; public string English; public string Place;
    }

    private sealed class Entry   // a lemma or a root
    {
        public UID128 Uid; public string Label; public string Folded; public string Root; public string Pos; public string Kind;
        public List<int> Words = new List<int>();
    }

    private sealed class Corpus
    {
        public int        WordCount;
        public Verse[]    Verses;                       // mushaf order
        public Dictionary<string, int> VerseByKey;
        public int[]      WordVerse;                    // word → verse index
        public int[]      WordPosition;
        public string[]   WordForm;                     // Uthmani form
        public string[]   WordFolded;                   // folded letters
        public int[]      WordLemma;                    // → Lemmas index, -1 none
        public int[]      WordRoot;                     // → Roots index, -1 none
        public string[]   WordPos;
        public Entry[]    Lemmas;
        public Entry[]    Roots;
        public Dictionary<int, (UID128 Uid, string Arabic, string Name)> Surahs;
    }

    private static Corpus _corpus;
    private static readonly object _gate = new object();

    private static Corpus Load(ToolScope scope)
    {
        var words = scope.Q().StartAt(N.Word.Type).Count();
        var c = _corpus;
        if (c != null && c.WordCount == words) return c;

        lock (_gate)
        {
            if (_corpus != null && _corpus.WordCount == words) return _corpus;

            scope.SetToolCallDisplayName("Reading the corpus (once)");

            var corpus = new Corpus { WordCount = words };

            corpus.Surahs = scope.Q().StartAt(N.Surah.Type).AsEnumerable().ToDictionary(
                s => s.GetInt(N.Surah.Number),
                s => (s.UID, s.GetString(N.Surah.NameArabic), s.GetString(N.Surah.NameScholarly)));

            corpus.Verses = scope.Q().StartAt(N.Verse.Type).AsEnumerable()
                .Select(v => new Verse
                {
                    Uid     = v.UID,
                    Key     = v.GetString(N.Verse.VerseKey),
                    Surah   = v.GetInt(N.Verse.SurahNumber),
                    Ayah    = v.GetInt(N.Verse.AyahNumber),
                    Index   = v.GetInt(N.Verse.MushafIndex),
                    Uthmani = v.GetString(N.Verse.TextUthmani),
                    Simple  = v.GetString(N.Verse.TextSimple),
                    English = v.GetString(N.Verse.EnglishSaheeh),
                    Place   = v.GetString(N.Verse.RevelationPlace),
                })
                .OrderBy(v => v.Index).ToArray();
            foreach (var v in corpus.Verses) v.Folded = Fold(v.Simple);
            corpus.VerseByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < corpus.Verses.Length; i++) corpus.VerseByKey[corpus.Verses[i].Key] = i;

            var lemmaIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var lemmas     = new List<Entry>();
            foreach (var l in scope.Q().StartAt(N.Lemma.Type).AsEnumerable())
            {
                lemmaIndex[l.GetString(N.Lemma.LemmaKey)] = lemmas.Count;
                lemmas.Add(new Entry
                {
                    Uid = l.UID, Kind = "lemma", Label = l.GetString(N.Lemma.LemmaText), Folded = Fold(l.GetString(N.Lemma.LemmaPlain)),
                    Root = l.GetString(N.Lemma.RootLetters), Pos = l.GetString(N.Lemma.PartOfSpeech),
                });
            }

            var rootIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var roots     = new List<Entry>();
            foreach (var r in scope.Q().StartAt(N.Root.Type).AsEnumerable())
            {
                var letters = r.GetString(N.Root.RootLetters);
                rootIndex[letters] = roots.Count;
                roots.Add(new Entry { Uid = r.UID, Kind = "root", Label = letters, Folded = Fold(letters), Root = letters });
            }

            var all = scope.Q().StartAt(N.Word.Type).AsEnumerable()
                .Select(w => (Verse:    corpus.VerseByKey.TryGetValue(w.GetString(N.Word.VerseKey), out var vi) ? vi : -1,
                              Position: w.GetInt(N.Word.Position),
                              Form:     w.GetString(N.Word.TextArabic),
                              Plain:    w.GetString(N.Word.TextPlain),
                              Lemma:    w.GetString(N.Word.LemmaText),
                              Root:     w.GetString(N.Word.RootLetters),
                              Tag:      w.GetString(N.Word.PosTag),
                              Pos:      w.GetString(N.Word.PartOfSpeech)))
                .Where(w => w.Verse >= 0)
                .OrderBy(w => w.Verse).ThenBy(w => w.Position)
                .ToArray();

            corpus.WordVerse    = new int[all.Length];
            corpus.WordPosition = new int[all.Length];
            corpus.WordForm     = new string[all.Length];
            corpus.WordFolded   = new string[all.Length];
            corpus.WordLemma    = new int[all.Length];
            corpus.WordRoot     = new int[all.Length];
            corpus.WordPos      = new string[all.Length];

            for (int i = 0; i < all.Length; i++)
            {
                var w = all[i];
                corpus.WordVerse[i]    = w.Verse;
                corpus.WordPosition[i] = w.Position;
                corpus.WordForm[i]     = w.Form;
                corpus.WordFolded[i]   = Fold(w.Plain);
                corpus.WordPos[i]      = w.Pos;
                // The lemma's key is lemma|root|tag, exactly as the connector and qkg/root/profile build it.
                var key = (w.Lemma ?? "") + "|" + (w.Root ?? "") + "|" + (w.Tag ?? "");
                corpus.WordLemma[i] = !string.IsNullOrEmpty(w.Lemma) && lemmaIndex.TryGetValue(key, out var li) ? li : -1;
                corpus.WordRoot[i]  = !string.IsNullOrEmpty(w.Root) && rootIndex.TryGetValue(w.Root, out var ri) ? ri : -1;
                if (corpus.WordLemma[i] >= 0) lemmas[corpus.WordLemma[i]].Words.Add(i);
                if (corpus.WordRoot[i]  >= 0) roots[corpus.WordRoot[i]].Words.Add(i);
            }

            corpus.Lemmas = lemmas.ToArray();
            corpus.Roots  = roots.ToArray();
            _corpus = corpus;
            return corpus;
        }
    }

    // ── Arabic ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Letters only: diacritics, Quranic marks and tatweel removed, alef forms → ا, ى → ي.</summary>
    private static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == 'ٱ' || ch == 'أ' || ch == 'إ' || ch == 'آ') sb.Append('ا');
            else if (ch == 'ى') sb.Append('ي');
            else if ((ch >= 'ء' && ch <= 'غ') || (ch >= 'ف' && ch <= 'ي')) sb.Append(ch);
            else if (ch == ' ') sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>"ءادم" (the corpus' spelling of آدم) folds to "اادم"; a reader types آدم → "ادم". Both are tried.</summary>
    private static IEnumerable<string> Spellings(string term)
    {
        var f = Fold(term).Replace(" ", "");
        yield return f;
        if (f.StartsWith("ا")) { yield return "ء" + f; yield return "ا" + f; }
        if (f.StartsWith("اا")) yield return f.Substring(1);
        if (f.StartsWith("ءا")) yield return "ا" + f.Substring(1);
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new System.Text.Json.JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // Arabic as Arabic, not \u escapes
    };

    private static string Out(object o) => System.Text.Json.JsonSerializer.Serialize(o, Json);

    private static string[] Terms(string terms) =>
        (terms ?? "").Split(new[] { ',', '،', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static object VerseRow(Corpus c, int v, int count = 0) => new
    {
        key   = c.Verses[v].Key,
        uid   = c.Verses[v].Uid.ToString(),
        count = count > 0 ? count : (int?)null,
    };

    private const string RULE_LEMMA = "lemma: words whose lemma in the Quranic Arabic Corpus is this one (all its inflections and attached prefixes/suffixes)";
    private const string RULE_ROOT  = "root: words built on this root (every lemma of the family)";
    private const string RULE_FORM  = "form: words whose letters, without diacritics, are exactly this (a prefix or suffix makes another form)";
    private const string RULE_TEXT  = "text: occurrences of these letters inside the verses' plain Arabic, across word boundaries";

    /// <summary>The lemmas, roots or forms a term names under a kind; "auto" tries lemma, then root, then form.</summary>
    private static List<(string Kind, Entry Entry, List<int> Words, string Label)> Resolve(Corpus c, string term, string kind)
    {
        kind = (kind ?? "auto").Trim().ToLowerInvariant();
        var spellings = Spellings(term).Distinct().ToList();
        var found = new List<(string, Entry, List<int>, string)>();

        if (kind == "lemma" || kind == "auto")
        {
            // A lemma is a headword without the article: المسيح is the lemma مسيح.
            var headwords = spellings.Concat(spellings.Where(x => x.StartsWith("ال") && x.Length > 3).Select(x => x.Substring(2))).Distinct().ToList();
            foreach (var e in c.Lemmas.Where(x => headwords.Contains(x.Folded)).OrderByDescending(x => x.Words.Count))
                found.Add(("lemma", e, e.Words, e.Label));
        }
        if (found.Count > 0 || kind == "lemma") return found;

        if (kind == "root" || kind == "auto")
        {
            var letters = Fold(term).Replace(" ", "");
            foreach (var e in c.Roots.Where(x => x.Folded == letters || x.Root == term.Replace(" ", "")))
                found.Add(("root", e, e.Words, e.Label));
        }
        if (found.Count > 0 || kind == "root") return found;

        if (kind == "form" || kind == "auto")
        {
            var words = new List<int>();
            for (int i = 0; i < c.WordFolded.Length; i++) if (spellings.Contains(c.WordFolded[i])) words.Add(i);
            if (words.Count > 0) found.Add(("form", null, words, Fold(term)));
        }
        return found;
    }

    private static object Peers(Corpus c, string kind, int count)
    {
        var table = kind == "root" ? c.Roots : c.Lemmas;
        var same  = table.Where(e => e.Words.Count == count).ToList();
        return new
        {
            rule    = "how many " + kind + "s of the " + table.Length + " occur exactly " + count + " times",
            others  = same.Count - 1,
            examples = same.OrderBy(e => e.Label, StringComparer.Ordinal).Take(15).Select(e => e.Label).ToArray(),
        };
    }

    // ── the tools ────────────────────────────────────────────────────────────────────────────

    [Tool("Counts how often a word occurs in the Quran, by an explicit rule, and lists where. `terms` is one or more Arabic terms separated by commas (آدم, عيسى). `kind` is lemma (the dictionary word with all its inflections - the usual meaning of 'mentioned N times'), root (the whole family), form (exact spelling without diacritics), text (letters anywhere in the verse text) or auto (lemma, else root, else form). Returns per term: each matching lemma/root/form with its uid, occurrences, verses, surahs, the count per surah, the verses (key, uid, occurrences), and how many other lemmas or roots occur exactly as often - the baseline for calling an equal count remarkable. Use it for every 'how many times' question. Use FindVerses for verses where several terms occur together.")]
    public static string CountTerm(ToolScope scope,
        [Parameter("One or more Arabic terms, comma-separated, e.g. 'آدم, عيسى' or 'رحم'. Diacritics optional.", required: true)] string terms,
        [Parameter("lemma | root | form | text | auto (default auto).", required: false)] string kind = "auto",
        [Parameter("Restrict to one surah by number (1-114). Empty for the whole Quran.", required: false)] int surah = 0,
        [Parameter("How many verses to list per match (default 60, at most 400).", required: false)] int limit = 60)
    {
        var c = Load(scope);
        if (limit <= 0) limit = 60;
        limit = Math.Min(limit, 400);
        kind = string.IsNullOrWhiteSpace(kind) ? "auto" : kind.Trim().ToLowerInvariant();
        scope.SetToolCallDisplayName("Counting " + terms);

        var results = new List<object>();
        foreach (var term in Terms(terms))
        {
            if (kind == "text")
            {
                var needle = Fold(term);
                var perVerse = new List<(int V, int N)>();
                for (int v = 0; v < c.Verses.Length; v++)
                {
                    if (surah > 0 && c.Verses[v].Surah != surah) continue;
                    int n = 0, at = 0;
                    while (needle.Length > 0 && (at = c.Verses[v].Folded.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
                    if (n > 0) perVerse.Add((v, n));
                }
                results.Add(new
                {
                    term, kind = "text", rule = RULE_TEXT,
                    occurrences = perVerse.Sum(p => p.N), verses = perVerse.Count,
                    surahs = perVerse.Select(p => c.Verses[p.V].Surah).Distinct().Count(),
                    perSurah = perVerse.GroupBy(p => c.Verses[p.V].Surah).Select(g => new { surah = g.Key, name = c.Surahs[g.Key].Name, count = g.Sum(p => p.N) }).ToArray(),
                    verseList = perVerse.Take(limit).Select(p => VerseRow(c, p.V, p.N)).ToArray(),
                    truncated = perVerse.Count > limit ? $"showing {limit} of {perVerse.Count} verses" : null,
                });
                continue;
            }

            var matches = Resolve(c, term, kind);
            if (matches.Count == 0)
            {
                results.Add(new { term, kind, error = "No " + (kind == "auto" ? "lemma, root or form" : kind) + " matches '" + term + "'. Try another kind, or kind=text." });
                continue;
            }

            foreach (var m in matches)
            {
                var words = surah > 0 ? m.Words.Where(w => c.Verses[c.WordVerse[w]].Surah == surah).ToList() : m.Words;
                var perVerse = words.GroupBy(w => c.WordVerse[w]).Select(g => (V: g.Key, N: g.Count())).OrderBy(p => p.V).ToList();
                results.Add(new
                {
                    term,
                    kind  = m.Kind,
                    rule  = m.Kind == "lemma" ? RULE_LEMMA : m.Kind == "root" ? RULE_ROOT : RULE_FORM,
                    label = m.Label,
                    uid   = m.Entry?.Uid.ToString(),
                    root  = m.Kind == "lemma" ? m.Entry.Root : null,
                    partOfSpeech = m.Kind == "lemma" ? m.Entry.Pos : null,
                    occurrences = words.Count,
                    verses      = perVerse.Count,
                    surahs      = perVerse.Select(p => c.Verses[p.V].Surah).Distinct().Count(),
                    forms       = words.GroupBy(w => c.WordForm[w]).OrderByDescending(g => g.Count()).Take(12).Select(g => new { form = g.Key, count = g.Count() }).ToArray(),
                    perSurah    = perVerse.GroupBy(p => c.Verses[p.V].Surah).Select(g => new { surah = g.Key, name = c.Surahs[g.Key].Name, uid = c.Surahs[g.Key].Uid.ToString(), count = g.Sum(p => p.N) }).ToArray(),
                    verseList   = perVerse.Take(limit).Select(p => VerseRow(c, p.V, p.N)).ToArray(),
                    truncated   = perVerse.Count > limit ? $"showing {limit} of {perVerse.Count} verses" : null,
                    sameCount   = surah == 0 && m.Kind != "form" ? Peers(c, m.Kind, words.Count) : null,
                });
            }
            if (matches.Count > 1)
                results.Add(new { term, note = matches.Count + " different " + matches[0].Kind + "s are spelled '" + term + "' (listed above, most frequent first): say which one you count, or report each." });
        }

        return Out(new { surah = surah > 0 ? surah : (int?)null, results });
    }

    [Tool("Finds the verses where Arabic terms occur together, by lemma or root. `all` must all occur in the verse, `any` at least one of them, `none` none of them; each is a comma-separated list of Arabic terms. Returns each verse's key, uid and plain Arabic, in mushaf order, and the number of verses found. Use it for co-occurrence ('verses that mention both Adam and Jesus', 'verses with the root رحم but not the lemma رحمن'). Narrow with surah or place (Meccan/Medinan).")]
    public static string FindVerses(ToolScope scope,
        [Parameter("Terms that must ALL occur in the verse, comma-separated.", required: false)] string all = null,
        [Parameter("Terms of which at least ONE must occur, comma-separated.", required: false)] string any = null,
        [Parameter("Terms that must NOT occur, comma-separated.", required: false)] string none = null,
        [Parameter("lemma | root | form | auto (default auto) - how each term is matched.", required: false)] string kind = "auto",
        [Parameter("Restrict to one surah by number (1-114).", required: false)] int surah = 0,
        [Parameter("Meccan or Medinan, to restrict by revelation place.", required: false)] string place = null,
        [Parameter("How many verses to return (default 40, at most 300).", required: false)] int limit = 40)
    {
        var c = Load(scope);
        if (limit <= 0) limit = 40;
        limit = Math.Min(limit, 300);
        var notes = new List<string>();

        HashSet<int> VersesOf(string term)
        {
            var m = Resolve(c, term, kind);
            if (m.Count == 0) { notes.Add("'" + term + "' matched nothing"); return new HashSet<int>(); }
            notes.Add("'" + term + "' = " + string.Join(" + ", m.Select(x => x.Kind + " " + x.Label + " (" + x.Words.Count + ")")));
            return new HashSet<int>(m.SelectMany(x => x.Words).Select(w => c.WordVerse[w]));
        }

        var allSets  = Terms(all).Select(VersesOf).ToList();
        var anySets  = Terms(any).Select(VersesOf).ToList();
        var noneSets = Terms(none).Select(VersesOf).ToList();
        if (allSets.Count == 0 && anySets.Count == 0) return Out(new { error = "Give at least one term in all or any." });

        scope.SetToolCallDisplayName("Finding verses: " + string.Join(", ", Terms(all).Concat(Terms(any))));

        IEnumerable<int> hits = Enumerable.Range(0, c.Verses.Length);
        foreach (var s in allSets) hits = hits.Where(s.Contains);
        if (anySets.Count > 0) hits = hits.Where(v => anySets.Any(s => s.Contains(v)));
        foreach (var s in noneSets) hits = hits.Where(v => !s.Contains(v));
        if (surah > 0) hits = hits.Where(v => c.Verses[v].Surah == surah);
        if (!string.IsNullOrWhiteSpace(place)) hits = hits.Where(v => string.Equals(c.Verses[v].Place, place.Trim(), StringComparison.OrdinalIgnoreCase));

        var list = hits.ToList();
        return Out(new
        {
            matched   = notes,
            verses    = list.Count,
            surahs    = list.Select(v => c.Verses[v].Surah).Distinct().Count(),
            results   = list.Take(limit).Select(v => new { key = c.Verses[v].Key, uid = c.Verses[v].Uid.ToString(), arabic = c.Verses[v].Simple }).ToArray(),
            truncated = list.Count > limit ? $"showing {limit} of {list.Count} verses" : null,
        });
    }

    [Tool("Reads verses: the Uthmani Arabic, an English reading aid (Saheeh International) and, with words=true, every word with its lemma, root and part of speech. `refs` takes verse keys and ranges, comma-separated: '3:59', '2:30-39', '112' (a whole surah), '3:59, 19:30-34'. At most 60 verses per call. Use it to quote the text a claim rests on; the Arabic is the text, the English only a gloss.")]
    public static string ReadVerses(ToolScope scope,
        [Parameter("Verse keys or ranges, comma-separated: '3:59', '2:30-39', '112'.", required: true)] string refs,
        [Parameter("Include each word's lemma, root and part of speech (default false).", required: false)] bool words = false)
    {
        var c = Load(scope);
        var picked = new List<int>();
        var errors = new List<string>();

        foreach (var r in Terms(refs))
        {
            var parts = r.Split(':');
            if (parts.Length == 1 && int.TryParse(parts[0], out var whole) && c.Surahs.ContainsKey(whole))
            {
                picked.AddRange(Enumerable.Range(0, c.Verses.Length).Where(v => c.Verses[v].Surah == whole));
                continue;
            }
            if (parts.Length != 2 || !int.TryParse(parts[0], out var s)) { errors.Add("'" + r + "' is not a verse key (surah:ayah)"); continue; }
            var range = parts[1].Split('-');
            if (!int.TryParse(range[0], out var from)) { errors.Add("'" + r + "' is not a verse key"); continue; }
            var to = range.Length > 1 && int.TryParse(range[1], out var t) ? t : from;
            for (int a = from; a <= to; a++)
            {
                if (c.VerseByKey.TryGetValue(s + ":" + a, out var v)) picked.Add(v);
                else { errors.Add(s + ":" + a + " does not exist"); break; }
            }
        }

        var distinct = picked.Distinct().ToList();
        var shown = distinct.Take(60).ToList();
        scope.SetToolCallDisplayName("Reading " + refs);

        Dictionary<int, List<int>> wordsOf = null;
        if (words)
        {
            var wanted = new HashSet<int>(shown);
            wordsOf = new Dictionary<int, List<int>>();
            for (int i = 0; i < c.WordVerse.Length; i++)
                if (wanted.Contains(c.WordVerse[i])) { if (!wordsOf.TryGetValue(c.WordVerse[i], out var l)) wordsOf[c.WordVerse[i]] = l = new List<int>(); l.Add(i); }
        }

        return Out(new
        {
            errors = errors.Count > 0 ? errors : null,
            truncated = distinct.Count > 60 ? $"showing 60 of {distinct.Count} verses" : null,
            verses = shown.Select(v => new
            {
                key     = c.Verses[v].Key,
                uid     = c.Verses[v].Uid.ToString(),
                surah   = c.Surahs[c.Verses[v].Surah].Name,
                place   = c.Verses[v].Place,
                arabic  = c.Verses[v].Uthmani,
                english = c.Verses[v].English,
                words   = wordsOf == null ? null : (wordsOf.TryGetValue(v, out var ws) ? ws : new List<int>()).Select(w => new
                {
                    position = c.WordPosition[w],
                    form     = c.WordForm[w],
                    lemma    = c.WordLemma[w] >= 0 ? c.Lemmas[c.WordLemma[w]].Label : null,
                    root     = c.WordRoot[w] >= 0 ? c.Roots[c.WordRoot[w]].Label : null,
                    pos      = c.WordPos[w],
                }).ToArray(),
            }).ToArray(),
        });
    }

    [Tool("Lists the lemmas or roots that occur exactly `count` times (or between `count` and `max`), with their uids - the baseline for any claim that two words occurring equally often is remarkable. Also returns how the counts are distributed: how many lemmas/roots there are, and how many share their count with at least one other.")]
    public static string SameCount(ToolScope scope,
        [Parameter("The number of occurrences.", required: true)] int count,
        [Parameter("lemma | root (default lemma).", required: false)] string kind = "lemma",
        [Parameter("Upper bound, for a range of counts. Empty for exactly `count`.", required: false)] int max = 0,
        [Parameter("Only nouns / proper nouns / verbs... (a part of speech as the lemmas name it, e.g. 'Proper noun'). Empty for all.", required: false)] string partOfSpeech = null)
    {
        var c = Load(scope);
        kind = string.Equals(kind, "root", StringComparison.OrdinalIgnoreCase) ? "root" : "lemma";
        var table = (kind == "root" ? c.Roots : c.Lemmas).AsEnumerable();
        if (kind == "lemma" && !string.IsNullOrWhiteSpace(partOfSpeech)) table = table.Where(e => string.Equals(e.Pos, partOfSpeech.Trim(), StringComparison.OrdinalIgnoreCase));
        var pool = table.ToList();
        var hi = max >= count ? max : count;
        var hits = pool.Where(e => e.Words.Count >= count && e.Words.Count <= hi).OrderBy(e => e.Words.Count).ThenBy(e => e.Label, StringComparer.Ordinal).ToList();
        var byCount = pool.GroupBy(e => e.Words.Count).ToDictionary(g => g.Key, g => g.Count());

        scope.SetToolCallDisplayName($"{kind}s occurring {count}{(hi > count ? "-" + hi : "")} times");

        return Out(new
        {
            kind,
            partOfSpeech,
            pool = pool.Count,
            poolSharingTheirCount = pool.Count(e => byCount[e.Words.Count] > 1),
            distinctCounts = byCount.Count,
            matches = hits.Count,
            results = hits.Take(200).Select(e => new { label = e.Label, uid = e.Uid.ToString(), occurrences = e.Words.Count, verses = e.Words.Select(w => c.WordVerse[w]).Distinct().Count(), partOfSpeech = e.Pos, root = kind == "lemma" ? e.Root : null }).ToArray(),
            partsOfSpeech = kind == "lemma" ? c.Lemmas.Select(e => e.Pos).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p).ToArray() : null,
        });
    }

    [Tool("Ranks the verses most similar to one verse by meaning and wording: the Arabic embedding (AraModernBert-STS) plus shared lemmas and shared roots, with each signal's contribution and the roots they share. The same ranking as the verse page's 'Similar verses'. Takes a verse key (2:255).")]
    public static async Task<string> SimilarVerses(ToolScope scope,
        [Parameter("The verse key, e.g. 3:59.", required: true)] string verse,
        [Parameter("How many to return (default 15, at most 50).", required: false)] int count = 15)
    {
        var c = Load(scope);
        if (!c.VerseByKey.TryGetValue((verse ?? "").Trim(), out var v)) return Out(new { error = "No verse " + verse + ": use surah:ayah, e.g. 3:59." });
        count = count <= 0 ? 15 : Math.Min(count, 50);
        scope.SetToolCallDisplayName("Verses similar to " + verse);
        var body = "{\"Uid\":\"" + c.Verses[v].Uid + "\",\"Count\":" + count + "}";
        var answer = await Endpoint(scope, "qkg/verse/similar", body);
        return answer?.ToString(Newtonsoft.Json.Formatting.None) ?? Out(new { error = "qkg/verse/similar returned nothing" });
    }

    [Tool("Ranks the surahs most similar to one surah: shared distinctive lemmas and roots plus how many of its verses have a verse of the other surah among their nearest in meaning, with the shared lemmas and the closest verse pairs as reasons. The same ranking as the surah page's 'Similar surahs'. Takes a surah number (2 for Al-Baqarah).")]
    public static async Task<string> SimilarSurahs(ToolScope scope,
        [Parameter("The surah number, 1-114.", required: true)] int surah,
        [Parameter("How many to return (default 10, at most 30).", required: false)] int count = 10)
    {
        var c = Load(scope);
        if (!c.Surahs.TryGetValue(surah, out var s)) return Out(new { error = "No surah " + surah + ": give a number from 1 to 114." });
        count = count <= 0 ? 10 : Math.Min(count, 30);
        scope.SetToolCallDisplayName("Surahs similar to " + s.Name);
        var answer = await Endpoint(scope, "qkg/surah/similar", "{\"Uid\":\"" + s.Uid + "\"}");
        if (answer == null) return Out(new { error = "qkg/surah/similar returned nothing" });

        // The endpoint returns every candidate with every counted verse pair: keep the top `count`
        // and their five closest pairs, which is what a reply can use.
        if (answer["Surahs"] is Newtonsoft.Json.Linq.JArray list)
        {
            var top = new Newtonsoft.Json.Linq.JArray();
            foreach (var row in list.Take(count))
            {
                var copy = (Newtonsoft.Json.Linq.JObject)row.DeepClone();
                if (copy["CloseVerses"] is Newtonsoft.Json.Linq.JArray pairs)
                {
                    copy["CloseVerseCount"] = pairs.Count;
                    copy["CloseVerses"] = new Newtonsoft.Json.Linq.JArray(pairs.Take(5));
                }
                top.Add(copy);
            }
            answer["Surahs"] = top;
        }
        return answer.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>
    /// One of the workspace's endpoints, as the user. Read as a JSON object: RunEndpointAsync&lt;string&gt;
    /// returns null for an endpoint that answers with a DTO (it converts only a string response).
    /// </summary>
    private static Task<Newtonsoft.Json.Linq.JObject> Endpoint(ToolScope scope, string path, string body)
        => scope.RunEndpointAsync<Newtonsoft.Json.Linq.JObject>(new EndpointUID(path), body, cancellationToken: scope.CancellationToken);
}

return new QuranTools();
