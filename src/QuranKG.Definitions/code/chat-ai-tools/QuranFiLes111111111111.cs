[tools: Curiosity.ChatAITools.UID("QuranFiLes111111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Quran Sandbox")]
[tools: Curiosity.ChatAITools.Description("The whole Quran as tab-separated files - every word with its verse, position, lemma, root and part of speech; every verse with its Arabic; the surahs, lemmas and roots with their counts - with bash, awk, jq and python over them, for the questions no fixed tool answers.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-terminal")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// A sandbox: the AI tool's code returns an ISandbox, and the workspace gives the model bash, awk,
// jq and python (in process: no host disk, no network) over the files built here. It is for the
// open question — patterns across the whole text, distributions, a statistic and its baseline,
// a permutation test — where the Quran tools answer the fixed ones (count, co-occur, read).
//
//   /quran   words.tsv, verses.tsv, surahs.tsv, lemmas.tsv, roots.tsv — built once per session
//            from the graph, the same data the Quran tools and the dashboard count
//   /docs    README.md: the columns, the counting rules, the method for a claim
//   /work    scratch, kept with the conversation
//   /artifacts  a report the model writes and `publish`es beside the chat
//
// THE ANSWER KEYS STAY OUT (CLAUDE.md, non-negotiable 2): no file carries QurSimPair, Topic,
// Verse.Ruku or Surah.RukuCount; search and consult are fenced to the types below; `app` lists
// only endpoints that read none of them (qkg/verse/words, which returns QurSim pairs, is absent,
// and so is qkg/clusters/build, which writes). There is no `query` command: the query engine emits
// whole nodes, and a verse node carries its ruku'.

public class QuranSandbox : ISandbox
{
    private static readonly string[] RecordTypes = { N.Verse.Type, N.Surah.Type, N.Word.Type, N.Root.Type, N.Lemma.Type, N.Juz.Type, N.Cluster.Type };

    private static readonly string[] Endpoints =
    {
        "qkg/dashboard/corpus", "qkg/surah/profile", "qkg/root/profile", "qkg/verse/similar",
        "qkg/surah/similar", "qkg/surah/passages", "qkg/clusters/themes", "qkg/cluster/profile",
    };

    public string FunctionName => "quran";

    public string Description => "The Quran as files, for counting, comparing and testing across the whole text: /quran/words.tsv has every word (verse, position, Uthmani form, plain letters, lemma, root, part of speech), /quran/verses.tsv every verse's Arabic, and surahs/lemmas/roots.tsv the units with their counts. Use bash, awk or python for distributions, patterns, positions, sequences and baselines (how often a pattern occurs by chance). Read /docs/README.md first. Not for reading one verse (ReadVerses or consult) or one count (CountTerm).";

    public int MaxDurationSeconds => 180;

    public SandboxMount[] Mounts => new[]
    {
        SandboxMount.Cached("/quran", "words.tsv, verses.tsv, surahs.tsv, lemmas.tsv, roots.tsv — tab-separated, a header line first; see /docs/README.md", BuildAsync),
        SandboxMount.Files("/docs", "README.md: the files' columns, the counting rules, and how to test a claim", SandboxFile.Text("README.md", Readme)),
        SandboxMount.Scratch("/work", "scratch space - kept for this conversation"),
    };

    public SandboxCommand[] Commands => new[]
    {
        SandboxCommand.Search(RecordTypes),
        SandboxCommand.Consult(RecordTypes),
        SandboxCommand.Create(
            "app",
            "app <endpoint> [json-body]: calls one of the workspace's own read-only endpoints and prints its JSON. `app list` prints them. Ids are uids from the tsv files, e.g. app qkg/root/profile '{\"Uid\":\"<root uid>\"}'.",
            async call =>
            {
                if (call.Arguments.Count == 0 || call.Arguments[0] == "list") return string.Join("\n", Endpoints);
                var endpoint = call.Arguments[0].Trim('/');
                if (!Endpoints.Contains(endpoint)) return call.Fail("unknown endpoint '" + endpoint + "' - `app list` prints the ones available");
                var body = call.Arguments.Count > 1 ? string.Join(" ", call.Arguments.Skip(1)) : (string.IsNullOrWhiteSpace(call.StdinText) ? "{}" : call.StdinText);
                // As a JSON object: RunEndpointAsync<string> returns null for an endpoint answering with a DTO.
                var answer = await call.Scope.RunEndpointAsync<Newtonsoft.Json.Linq.JToken>(new EndpointUID(endpoint), body, cancellationToken: call.CancellationToken);
                return answer?.ToString(Newtonsoft.Json.Formatting.Indented) ?? call.Fail(endpoint + " returned nothing - check the body: " + body);
            }),
    };

    public ArtifactType[] Artifacts => new[] { ArtifactType.Html, ArtifactType.Pdf };

    // ── /quran ───────────────────────────────────────────────────────────────────────────────

    private static string Cell(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static Task<IEnumerable<SandboxFile>> BuildAsync(ToolScope scope)
    {
        var surahs = scope.Q().StartAt(N.Surah.Type).AsEnumerable().OrderBy(s => s.GetInt(N.Surah.Number)).ToList();
        var verses = scope.Q().StartAt(N.Verse.Type).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.MushafIndex)).ToList();
        var words  = scope.Q().StartAt(N.Word.Type).AsEnumerable()
                          .Select(w => (Surah: w.GetInt(N.Word.SurahNumber), Ayah: w.GetInt(N.Word.AyahNumber), Position: w.GetInt(N.Word.Position), Node: w))
                          .OrderBy(w => w.Surah).ThenBy(w => w.Ayah).ThenBy(w => w.Position).ToList();

        var wordCount  = new Dictionary<int, int>();
        var lemmaCount = new Dictionary<string, (int Words, HashSet<string> Verses)>(StringComparer.Ordinal);
        var rootCount  = new Dictionary<string, (int Words, HashSet<string> Verses)>(StringComparer.Ordinal);

        var sb = new StringBuilder("word_key\tverse_key\tsurah\tayah\tposition\tform\tplain\tlemma\troot\tpos\tpos_tag\tverb_form\tfeatures\n");
        foreach (var (s, a, p, w) in words)
        {
            var verseKey = w.GetString(N.Word.VerseKey);
            var lemma    = w.GetString(N.Word.LemmaText);
            var root     = w.GetString(N.Word.RootLetters);
            var tag      = w.GetString(N.Word.PosTag);
            sb.Append(Cell(w.GetString(N.Word.WordKey))).Append('\t').Append(verseKey).Append('\t').Append(s).Append('\t').Append(a).Append('\t').Append(p).Append('\t')
              .Append(Cell(w.GetString(N.Word.TextArabic))).Append('\t').Append(Cell(w.GetString(N.Word.TextPlain))).Append('\t')
              .Append(Cell(lemma)).Append('\t').Append(Cell(root)).Append('\t').Append(Cell(w.GetString(N.Word.PartOfSpeech))).Append('\t')
              .Append(Cell(tag)).Append('\t').Append(w.GetInt(N.Word.VerbForm)).Append('\t').Append(Cell(w.GetString(N.Word.Features))).Append('\n');

            wordCount[s] = wordCount.TryGetValue(s, out var n) ? n + 1 : 1;
            if (!string.IsNullOrEmpty(lemma))
            {
                var key = lemma + "|" + root + "|" + tag;
                if (!lemmaCount.TryGetValue(key, out var lc)) lemmaCount[key] = lc = (0, new HashSet<string>(StringComparer.Ordinal));
                lc.Verses.Add(verseKey);
                lemmaCount[key] = (lc.Words + 1, lc.Verses);
            }
            if (!string.IsNullOrEmpty(root))
            {
                if (!rootCount.TryGetValue(root, out var rc)) rootCount[root] = rc = (0, new HashSet<string>(StringComparer.Ordinal));
                rc.Verses.Add(verseKey);
                rootCount[root] = (rc.Words + 1, rc.Verses);
            }
        }
        var wordsTsv = sb.ToString();

        sb = new StringBuilder("verse_key\tsurah\tayah\tmushaf_index\tuid\tplace\tjuz\tpage\twords\tarabic_plain\tarabic_uthmani\tenglish\n");
        var wordsPerVerse = words.GroupBy(w => w.Node.GetString(N.Word.VerseKey)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var v in verses)
        {
            var key = v.GetString(N.Verse.VerseKey);
            sb.Append(key).Append('\t').Append(v.GetInt(N.Verse.SurahNumber)).Append('\t').Append(v.GetInt(N.Verse.AyahNumber)).Append('\t')
              .Append(v.GetInt(N.Verse.MushafIndex)).Append('\t').Append(v.UID).Append('\t').Append(Cell(v.GetString(N.Verse.RevelationPlace))).Append('\t')
              .Append(v.GetInt(N.Verse.Juz)).Append('\t').Append(v.GetInt(N.Verse.Page)).Append('\t').Append(wordsPerVerse.TryGetValue(key, out var wc) ? wc : 0).Append('\t')
              .Append(Cell(v.GetString(N.Verse.TextSimple))).Append('\t').Append(Cell(v.GetString(N.Verse.TextUthmani))).Append('\t')
              .Append(Cell(v.GetString(N.Verse.EnglishSaheeh))).Append('\n');
        }
        var versesTsv = sb.ToString();

        sb = new StringBuilder("surah\tuid\tname_arabic\tname\tname_english\tplace\trevelation_order\tverses\twords\n");
        foreach (var s in surahs)
        {
            var number = s.GetInt(N.Surah.Number);
            sb.Append(number).Append('\t').Append(s.UID).Append('\t').Append(Cell(s.GetString(N.Surah.NameArabic))).Append('\t')
              .Append(Cell(s.GetString(N.Surah.NameScholarly))).Append('\t').Append(Cell(s.GetString(N.Surah.NameEnglish))).Append('\t')
              .Append(Cell(s.GetString(N.Surah.RevelationPlace))).Append('\t').Append(s.GetInt(N.Surah.RevelationOrder)).Append('\t')
              .Append(s.GetInt(N.Surah.AyahCount)).Append('\t').Append(wordCount.TryGetValue(number, out var n) ? n : 0).Append('\n');
        }
        var surahsTsv = sb.ToString();

        sb = new StringBuilder("lemma\tplain\troot\tpos\tpos_tag\tuid\toccurrences\tverses\n");
        foreach (var l in scope.Q().StartAt(N.Lemma.Type).AsEnumerable())
        {
            var key = l.GetString(N.Lemma.LemmaKey);
            var c   = lemmaCount.TryGetValue(key, out var lc) ? lc : (0, new HashSet<string>());
            sb.Append(Cell(l.GetString(N.Lemma.LemmaText))).Append('\t').Append(Cell(l.GetString(N.Lemma.LemmaPlain))).Append('\t')
              .Append(Cell(l.GetString(N.Lemma.RootLetters))).Append('\t').Append(Cell(l.GetString(N.Lemma.PartOfSpeech))).Append('\t')
              .Append(Cell(l.GetString(N.Lemma.PosTag))).Append('\t').Append(l.UID).Append('\t').Append(c.Item1).Append('\t').Append(c.Item2.Count).Append('\n');
        }
        var lemmasTsv = sb.ToString();

        sb = new StringBuilder("root\tuid\toccurrences\tverses\n");
        foreach (var r in scope.Q().StartAt(N.Root.Type).AsEnumerable())
        {
            var letters = r.GetString(N.Root.RootLetters);
            var c = rootCount.TryGetValue(letters, out var rc) ? rc : (0, new HashSet<string>());
            sb.Append(Cell(letters)).Append('\t').Append(r.UID).Append('\t').Append(c.Item1).Append('\t').Append(c.Item2.Count).Append('\n');
        }
        var rootsTsv = sb.ToString();

        IEnumerable<SandboxFile> files = new[]
        {
            SandboxFile.Text("words.tsv",  wordsTsv),
            SandboxFile.Text("verses.tsv", versesTsv),
            SandboxFile.Text("surahs.tsv", surahsTsv),
            SandboxFile.Text("lemmas.tsv", lemmasTsv),
            SandboxFile.Text("roots.tsv",  rootsTsv),
        };
        return Task.FromResult(files);
    }

    // ── /docs ────────────────────────────────────────────────────────────────────────────────

    private const string Readme = """
        # The Quran sandbox

        The Arabic text of the Quran with the morphology of the Quranic Arabic Corpus (version 0.4),
        as tab-separated files with a header line. Everything here is read from the workspace's
        graph: the same words the Quran tools and the dashboard count.

        ## /quran

        | File | One row per | Columns |
        |---|---|---|
        | `words.tsv` | word, in mushaf order | word_key (2:31:4), verse_key, surah, ayah, position (1-based in the verse), form (Uthmani, with diacritics), plain (letters without diacritics), lemma, root (letters, empty for particles and most proper nouns), pos (Noun, Verb, Proper noun, Particle...), pos_tag, verb_form (0 when none), features |
        | `verses.tsv` | verse, in mushaf order | verse_key, surah, ayah, mushaf_index, uid, place (Meccan/Medinan), juz, page, words, arabic_plain (Simple-Clean: no diacritics), arabic_uthmani, english (Saheeh International, a reading aid only) |
        | `surahs.tsv` | surah | surah, uid, name_arabic, name, name_english, place, revelation_order, verses, words |
        | `lemmas.tsv` | lemma | lemma, plain, root, pos, pos_tag, uid, occurrences (words), verses |
        | `roots.tsv` | root | root, uid, occurrences (words), verses |

        A word is one space-separated token of the text: و, ب, ال, pronoun suffixes are inside it.
        A lemma is a dictionary headword (ابن covers ابن، ابنا، أبناء، بنين، بني). Two lemmas may be
        spelled alike and are told apart by root and pos_tag.

        Read them with python:

        ```python
        import csv
        words = list(csv.DictReader(open('/quran/words.tsv', encoding='utf-8'), delimiter='\t'))
        ```

        or with awk: `awk -F'\t' '$8=="عِيسَى"' /quran/words.tsv | wc -l`.

        Commands beyond bash: `search <words> [--type Verse]`, `consult <uid>`, `app list` /
        `app <endpoint> '<json>'` (the workspace's own endpoints: surah and root profiles, similar
        verses and surahs, passages and themes). `publish` a report written to /artifacts.

        ## The counting rules — state one with every number

        | Rule | What it counts |
        |---|---|
        | lemma | words whose lemma is the term — the usual meaning of "mentioned N times" |
        | root | words of the whole family (proper nouns usually have no root) |
        | form | words spelled exactly so, without diacritics — a prefix or suffix makes another form |
        | text | the letters anywhere in the plain text — they can sit inside other words |

        The same word gives different numbers under different rules, and every published count
        comes with one. Before comparing two counts, fix the rule, and apply the same rule to both.

        ## How to test a claim

        1. **Fix the rule before counting** — lemma, form or text; with or without attached
           particles; one verse or each occurrence. Write it down.
        2. **Count both sides with that rule**, and list every occurrence (verse keys), so a reader
           can check each one.
        3. **Report the other reasonable rules too.** A match under one rule only is a finding about
           the rule.
        4. **Measure the baseline.** How many lemmas share a given count (lemmas.tsv); how often two
           lemmas of the same kind — two prophets' names, two nouns of similar frequency — match by
           chance; how many pairs could have been compared. Most lemmas share their count with
           another (count it in lemmas.tsv), so an equal count needs the baseline before it can be
           called remarkable.
        5. **Separate what the text says from what was counted.** A verse that compares two things
           (3:59) is a statement in the text; a count is a measurement of it. Both can be true, and
           only the first is the text's own claim.

        Report what was found, how, with which rule, and what chance alone would give.
        """;
}

return new QuranSandbox();
