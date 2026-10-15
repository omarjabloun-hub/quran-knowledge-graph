[tools: Curiosity.ChatAITools.UID("ConsuLt111111111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Consult")]
[tools: Curiosity.ChatAITools.Description("Reads one record of the Quran graph in full by its id - a verse, surah, word, root, lemma, juz, passage or theme - attached to this conversation, found by Search Records or listed by the Quran tools.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-book-open-cover")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// The consult readers: one method per node type the assistant may read, Consult<NodeType>(scope,
// uid). The server builds the consult tool around them, for this UID only. Helpers must not start
// with "Consult". A broken reader is silent: after an import check
// GET chatai/tools/get/function-names?toolUID=ConsuLt111111111111111 is not [].
//
// THE ANSWER KEYS HAVE NO READER AND NO READER RETURNS THEM (CLAUDE.md, non-negotiable 2):
//   QurSimPair       no reader; the verse reader does not follow InQurSimPair
//   Topic            no reader; the verse reader does not follow HasTopic / InTopicBranch
//   Tafsir, TafsirEntry  no reader; the verse reader does not follow HasTafsirEntry (Ibn Kathir's
//                    tafsir is the text QurSim was drawn from)
//   Verse.Ruku       not among the verse's fields — the ruku' score the passages
//   Surah.RukuCount  not among the surah's fields
// A type with no reader is "not consultable" to the model, which is the point.

Dictionary<string, object> Fields(ReadOnlyNode node, params (string Name, string Field, bool IsInt)[] names)
{
    var values = new Dictionary<string, object>(StringComparer.Ordinal);
    foreach (var (name, field, isInt) in names)
    {
        if (isInt) { var i = node.GetInt(field); if (i != 0) values[name] = i; }
        else       { var s = node.GetString(field); if (!string.IsNullOrWhiteSpace(s)) values[name] = s; }
    }
    return values;
}

(string, string, bool) S(string name, string field) => (name, field, false);
(string, string, bool) I(string name, string field) => (name, field, true);

object[] Refs(ToolScope scope, UID128 uid, string type, string edge, Func<ReadOnlyNode, string> label, int cap)
    => scope.Q().StartAt(uid).Out(type, edge).AsEnumerable().Take(cap)
            .Select(n => (object)new { id = n.UID.ToString(), label = label(n) }).ToArray();

string VerseLabel(ReadOnlyNode v) => v.GetString(N.Verse.VerseKey);
string ClusterLabel(ReadOnlyNode c) => c.GetString(N.Cluster.LabelArabic) + " / " + c.GetString(N.Cluster.Label);

object Missing(string kind) => new { ok = false, error = kind + " not found" };

// ── the readers ──────────────────────────────────────────────────────────────────────────────

object ConsultVerse(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var v)) return Missing("verse");
    scope.SetToolCallDisplayName("Reading verse " + v.GetString(N.Verse.VerseKey));

    var words = scope.Q().StartAt(uid).Out(N.Word.Type, E.HasWord).AsEnumerable()
                     .OrderBy(w => w.GetInt(N.Word.Position))
                     .Select(w => new
                     {
                         id       = w.UID.ToString(),
                         position = w.GetInt(N.Word.Position),
                         form     = w.GetString(N.Word.TextArabic),
                         lemma    = w.GetString(N.Word.LemmaText),
                         root     = w.GetString(N.Word.RootLetters),
                         pos      = w.GetString(N.Word.PartOfSpeech),
                         features = w.GetString(N.Word.Features),
                     }).ToArray();

    var passage = scope.Q().StartAt(uid).Out(N.Cluster.Type, E.InCluster).AsEnumerable().FirstOrDefault();
    var theme   = passage == null ? null : scope.Q().StartAt(passage.UID).Out(N.Cluster.Type, E.InTheme).AsEnumerable().FirstOrDefault();

    return new
    {
        ok = true, id = uid.ToString(), type = v.Type,
        fields = Fields(v,
            S("verseKey", N.Verse.VerseKey), I("surah", N.Verse.SurahNumber), I("ayah", N.Verse.AyahNumber), S("surahName", N.Verse.SurahName),
            S("arabicUthmani", N.Verse.TextUthmani), S("arabicPlain", N.Verse.TextSimple),
            S("englishSaheeh", N.Verse.EnglishSaheeh), S("englishQarai", N.Verse.EnglishQarai), S("transliteration", N.Verse.Transliteration),
            S("revelationPlace", N.Verse.RevelationPlace), I("juz", N.Verse.Juz), I("hizb", N.Verse.Hizb), I("page", N.Verse.Page),
            I("mushafIndex", N.Verse.MushafIndex), S("sajda", N.Verse.SajdaType)),
        related = new
        {
            surah   = Refs(scope, uid, N.Surah.Type, E.InSurah, s => s.GetString(N.Surah.NameScholarly), 1),
            passage = passage == null ? null : new { id = passage.UID.ToString(), label = ClusterLabel(passage) },
            theme   = theme == null ? null : new { id = theme.UID.ToString(), label = ClusterLabel(theme) },
            words,
        },
        note = "The English is a reading aid; the Arabic is the text.",
    };
}

object ConsultSurah(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var s)) return Missing("surah");
    scope.SetToolCallDisplayName("Reading surah " + s.GetString(N.Surah.NameScholarly));

    var verses   = scope.Q().StartAt(uid).Out(N.Verse.Type, E.HasVerse).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.AyahNumber)).ToList();
    var passages = scope.Q().StartAt(uid).Out(N.Cluster.Type, E.HasCluster).AsEnumerable().OrderBy(p => p.GetInt(N.Cluster.Ordinal)).ToList();

    return new
    {
        ok = true, id = uid.ToString(), type = s.Type,
        fields = Fields(s,
            I("number", N.Surah.Number), S("nameArabic", N.Surah.NameArabic), S("nameScholarly", N.Surah.NameScholarly),
            S("nameLatin", N.Surah.NameLatin), S("nameEnglish", N.Surah.NameEnglish), S("revelationPlace", N.Surah.RevelationPlace),
            I("revelationOrder", N.Surah.RevelationOrder), I("verses", N.Surah.AyahCount), S("introduction", N.Surah.Introduction)),
        related = new
        {
            words    = scope.Q().StartAt(uid).Out(N.Word.Type, E.SurahHasWord).Count(),
            passages = passages.Select(p => new { id = p.UID.ToString(), from = p.GetString(N.Cluster.FirstVerseKey), to = p.GetString(N.Cluster.LastVerseKey), label = ClusterLabel(p) }).ToArray(),
            verses   = verses.Take(300).Select(v => new { id = v.UID.ToString(), key = v.GetString(N.Verse.VerseKey), arabic = v.GetString(N.Verse.TextSimple) }).ToArray(),
        },
        note = verses.Count > 300 ? $"showing 300 of {verses.Count} verses; ReadVerses reads any range" : null,
    };
}

object ConsultWord(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var w)) return Missing("word");
    scope.SetToolCallDisplayName("Reading word " + w.GetString(N.Word.WordKey));
    return new
    {
        ok = true, id = uid.ToString(), type = w.Type,
        fields = Fields(w,
            S("wordKey", N.Word.WordKey), S("verseKey", N.Word.VerseKey), I("position", N.Word.Position),
            S("form", N.Word.TextArabic), S("plain", N.Word.TextPlain), S("segments", N.Word.Segments), S("segmentTags", N.Word.SegmentTags),
            S("root", N.Word.RootLetters), S("lemma", N.Word.LemmaText), S("partOfSpeech", N.Word.PartOfSpeech), S("posTag", N.Word.PosTag),
            S("features", N.Word.Features), I("verbForm", N.Word.VerbForm)),
        related = new
        {
            verse = Refs(scope, uid, N.Verse.Type, E.InVerse, VerseLabel, 1),
            root  = Refs(scope, uid, N.Root.Type, E.HasRoot, r => r.GetString(N.Root.RootLetters), 1),
            lemma = Refs(scope, uid, N.Lemma.Type, E.HasLemma, l => l.GetString(N.Lemma.LemmaText), 1),
        },
    };
}

object ConsultRoot(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var r)) return Missing("root");
    scope.SetToolCallDisplayName("Reading root " + r.GetString(N.Root.Spaced));
    var lemmas = scope.Q().StartAt(uid).Out(N.Lemma.Type, E.RootOfLemma).AsEnumerable()
                      .Select(l => new { id = l.UID.ToString(), lemma = l.GetString(N.Lemma.LemmaText), pos = l.GetString(N.Lemma.PartOfSpeech), occurrences = scope.Q().StartAt(l.UID).Out(N.Word.Type, E.LemmaOfWord).Count() })
                      .OrderByDescending(l => l.occurrences).ToArray();
    return new
    {
        ok = true, id = uid.ToString(), type = r.Type,
        fields = Fields(r, S("letters", N.Root.RootLetters), S("spaced", N.Root.Spaced)),
        related = new
        {
            occurrences = scope.Q().StartAt(uid).Out(N.Word.Type, E.RootOfWord).Count(),
            verses      = scope.Q().StartAt(uid).Out(N.Verse.Type, E.RootInVerse).Count(),
            lemmas,
        },
        note = "Occurrences are words built on the root. CountTerm lists where they are.",
    };
}

object ConsultLemma(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var l)) return Missing("lemma");
    scope.SetToolCallDisplayName("Reading lemma " + l.GetString(N.Lemma.LemmaText));
    var verses = scope.Q().StartAt(uid).Out(N.Verse.Type, E.LemmaInVerse).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.MushafIndex)).ToList();
    return new
    {
        ok = true, id = uid.ToString(), type = l.Type,
        fields = Fields(l, S("lemma", N.Lemma.LemmaText), S("plain", N.Lemma.LemmaPlain), S("root", N.Lemma.RootLetters), S("partOfSpeech", N.Lemma.PartOfSpeech), S("posTag", N.Lemma.PosTag)),
        related = new
        {
            occurrences = scope.Q().StartAt(uid).Out(N.Word.Type, E.LemmaOfWord).Count(),
            verseCount  = verses.Count,
            verses      = verses.Take(100).Select(v => new { id = v.UID.ToString(), key = v.GetString(N.Verse.VerseKey) }).ToArray(),
            root        = Refs(scope, uid, N.Root.Type, E.LemmaHasRoot, x => x.GetString(N.Root.RootLetters), 1),
        },
    };
}

object ConsultJuz(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var j)) return Missing("juz");
    return new
    {
        ok = true, id = uid.ToString(), type = j.Type,
        fields = Fields(j, I("number", N.Juz.Number), S("firstVerse", N.Juz.FirstVerseKey)),
        related = new { surahs = Refs(scope, uid, N.Surah.Type, E.JuzHasSurah, s => s.GetString(N.Surah.NameScholarly), 20), verses = scope.Q().StartAt(uid).Out(N.Verse.Type, E.JuzHasVerse).Count() },
    };
}

object ConsultCluster(ToolScope scope, UID128 uid)
{
    if (!scope.Graph.TryGet(uid, out var c)) return Missing("cluster");
    var kind = c.GetString(N.Cluster.Kind);
    scope.SetToolCallDisplayName("Reading " + kind.ToLowerInvariant() + " " + c.GetString(N.Cluster.LabelArabic));

    object related;
    if (kind == "Theme")
    {
        related = new
        {
            passages = scope.Q().StartAt(uid).Out(N.Cluster.Type, E.HasPassage).AsEnumerable()
                            .Select(p => new { id = p.UID.ToString(), surah = p.GetInt(N.Cluster.SurahNumber), from = p.GetString(N.Cluster.FirstVerseKey), to = p.GetString(N.Cluster.LastVerseKey), label = ClusterLabel(p) })
                            .OrderBy(p => p.surah).ToArray(),
        };
    }
    else
    {
        var theme = scope.Q().StartAt(uid).Out(N.Cluster.Type, E.InTheme).AsEnumerable().FirstOrDefault();
        related = new
        {
            surah  = Refs(scope, uid, N.Surah.Type, E.ClusterOfSurah, s => s.GetString(N.Surah.NameScholarly), 1),
            theme  = theme == null ? null : new { id = theme.UID.ToString(), label = ClusterLabel(theme) },
            verses = scope.Q().StartAt(uid).Out(N.Verse.Type, E.HasMember).AsEnumerable().OrderBy(v => v.GetInt(N.Verse.MushafIndex))
                          .Take(120).Select(v => new { id = v.UID.ToString(), key = v.GetString(N.Verse.VerseKey), arabic = v.GetString(N.Verse.TextSimple) }).ToArray(),
        };
    }

    return new
    {
        ok = true, id = uid.ToString(), type = c.Type,
        fields = Fields(c,
            S("kind", N.Cluster.Kind), S("labelArabic", N.Cluster.LabelArabic), S("label", N.Cluster.Label), S("description", N.Cluster.Description),
            I("surah", N.Cluster.SurahNumber), S("from", N.Cluster.FirstVerseKey), S("to", N.Cluster.LastVerseKey), S("method", N.Cluster.Method)),
        related,
        note = "Passages and themes are computed by the clusters build from the words and the embeddings, not from a tradition: the labels are the passage's most distinctive lemmas.",
    };
}
