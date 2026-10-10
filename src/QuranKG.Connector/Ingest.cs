// One handler per part of the corpus. Each node is written ONCE per run, carrying everything
// (AddOrUpdate writes the whole content — a second partial write would blank the rest), and
// every link is made by key.
//
// The connector writes only relationships the sources state. Similarity, clusters and NLP
// mentions are discovered inside the workspace and arrive as their own edges.

using System.Globalization;
using static QuranKG.Connector.Nodes;

namespace QuranKG.Connector;

internal static class StructureIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        var c = ctx.Corpus;

        foreach (var place in c.Surahs.Select(s => s.RevelationPlace).Distinct(StringComparer.Ordinal))
            ctx.Write(new Revelation { PlaceName = place }, place);

        foreach (var juz in c.Juzs)
            ctx.Write(new Juz { JuzId = juz.Key, Number = juz.Number, FirstVerseKey = juz.FirstVerse.Key }, juz.Key);

        // The juz a surah spans, from where its verses fall.
        var juzOfSurah = c.Verses.GroupBy(v => v.Ref.Surah).ToDictionary(g => g.Key, g => g.Select(v => v.Juz).Distinct().ToList());

        foreach (var s in c.Surahs)
        {
            var node = ctx.Write(new Surah
            {
                SurahId         = s.Key,
                Number          = s.Number,
                NameArabic      = s.NameArabic,
                NameLatin       = s.NameLatin,
                NameScholarly   = s.NameScholarly,
                NameEnglish     = s.NameEnglish,
                RevelationPlace = s.RevelationPlace,
                RevelationOrder = s.RevelationOrder,
                AyahCount       = s.AyahCount,
                RukuCount       = s.RukuCount,
                FirstVerseIndex = s.FirstVerseIndex,
                Introduction    = s.Introduction,
            }, s.Key);

            ctx.Link(node, nameof(Revelation), s.RevelationPlace, Edges.SurahRevealedIn, Edges.RevelationOfSurah);
            foreach (var juz in juzOfSurah[s.Number])
                ctx.Link(node, nameof(Juz), juz.ToString(CultureInfo.InvariantCulture), Edges.SurahInJuz, Edges.JuzHasSurah);
        }

        await ctx.CommitAsync();
    }
}

internal static class LanguageIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var root in ctx.Corpus.Roots)
            ctx.Write(new Root { RootLetters = root.Letters, Spaced = root.Spaced }, root.Letters);

        foreach (var lemma in ctx.Corpus.Lemmas)
        {
            var node = ctx.Write(new Lemma
            {
                LemmaKey     = lemma.Key,
                LemmaText    = lemma.Text,
                LemmaPlain   = lemma.TextPlain,
                RootLetters  = lemma.Root ?? "",
                PosTag       = lemma.PosTag,
                PartOfSpeech = lemma.PartOfSpeech,
            }, lemma.Key);

            if (lemma.Root is not null) ctx.Link(node, nameof(Root), lemma.Root, Edges.LemmaHasRoot, Edges.RootOfLemma);
        }

        await ctx.CommitAsync();
    }
}

internal static class VerseIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        var c         = ctx.Corpus;
        var surahName = c.Surahs.ToDictionary(s => s.Number, s => s.NameScholarly is "" ? s.NameLatin : s.NameScholarly);

        foreach (var v in c.Verses)
        {
            var node = ctx.Write(new Verse
            {
                VerseKey        = v.Key,
                SurahNumber     = v.Ref.Surah,
                AyahNumber      = v.Ref.Ayah,
                MushafIndex     = v.MushafIndex,
                SurahName       = surahName[v.Ref.Surah],
                TextUthmani     = v.TextUthmani,
                TextSimple      = v.TextSimple,
                EnglishQarai    = v.EnglishQarai,
                EnglishSaheeh   = v.EnglishSaheeh,
                Transliteration = v.Transliteration,
                Juz             = v.Juz,
                Hizb            = v.Hizb,
                HizbQuarter     = v.HizbQuarter,
                Manzil          = v.Manzil,
                Ruku            = v.Ruku,
                Page            = v.Page,
                SajdaType       = v.SajdaType,
                RevelationPlace = v.RevelationPlace,
            }, v.Key);

            ctx.Link(node, nameof(Surah),      v.Ref.Surah.ToString(CultureInfo.InvariantCulture), Edges.InSurah,    Edges.HasVerse);
            ctx.Link(node, nameof(Juz),        v.Juz.ToString(CultureInfo.InvariantCulture),       Edges.InJuz,      Edges.JuzHasVerse);
            ctx.Link(node, nameof(Revelation), v.RevelationPlace,                                  Edges.RevealedIn, Edges.RevelationOfVerse);

            var words = c.WordsByVerse[v.Ref];
            foreach (var root in words.Where(w => w.Root is not null).Select(w => w.Root!).Distinct(StringComparer.Ordinal))
                ctx.Link(node, nameof(Root), root, Edges.VerseHasRoot, Edges.RootInVerse);
            foreach (var lemma in words.Where(w => w.LemmaKey is not null).Select(w => w.LemmaKey!).Distinct(StringComparer.Ordinal))
                ctx.Link(node, nameof(Lemma), lemma, Edges.VerseHasLemma, Edges.LemmaInVerse);
        }

        await ctx.CommitAsync();
    }
}

internal static class WordIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var w in ctx.Corpus.Words)
        {
            var node = ctx.Write(new Word
            {
                WordKey      = w.Key,
                VerseKey     = w.Verse.Key,
                SurahNumber  = w.Verse.Surah,
                AyahNumber   = w.Verse.Ayah,
                Position     = w.Position,
                TextArabic   = w.TextArabic,
                TextPlain    = w.TextPlain,
                Segments     = string.Join(" + ", w.Segments.Select(s => s.Form)),
                SegmentTags  = string.Join(" + ", w.Segments.Select(s => s.Tag)),
                RootLetters  = w.Root ?? "",
                LemmaText    = w.Lemma ?? "",
                PosTag       = w.PosTag,
                PartOfSpeech = w.PartOfSpeech,
                Features     = w.Features,
                VerbForm     = w.VerbForm,
            }, w.Key);

            ctx.Link(node, nameof(Verse), w.Verse.Key, Edges.InVerse, Edges.HasWord);
            ctx.Link(node, nameof(Surah), w.Verse.Surah.ToString(CultureInfo.InvariantCulture), Edges.WordInSurah, Edges.SurahHasWord);
            if (w.Root is not null)     ctx.Link(node, nameof(Root),  w.Root,     Edges.HasRoot,  Edges.RootOfWord);
            if (w.LemmaKey is not null) ctx.Link(node, nameof(Lemma), w.LemmaKey, Edges.HasLemma, Edges.LemmaOfWord);
        }

        await ctx.CommitAsync();
    }
}

internal static class TopicIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        var topics = ctx.Corpus.Topics;
        if (topics.Count == 0) return;

        var byId = topics.ToDictionary(t => t.Id, StringComparer.Ordinal);

        foreach (var t in topics)
        {
            var node = ctx.Write(new Topic
            {
                TopicId         = t.Id,
                TopicName       = t.Name,
                TopicNameArabic = t.NameArabic,
                TopicFamily     = t.Family,
                ParentTopicId   = t.ParentId ?? "",
                Description     = t.Description,
            }, t.Id);

            if (t.ParentId is not null && byId.ContainsKey(t.ParentId))
                ctx.Link(node, nameof(Topic), t.ParentId, Edges.ParentTopic, Edges.ChildTopic);
        }

        // Linked at every level: a verse joins its own topic directly and each ancestor over
        // InTopicBranch, so "Stories" gathers every story verse while a verse page still lists
        // only the topics QUL gave it.
        foreach (var t in topics)
        {
            var ancestors = Ancestors(t, byId).ToList();
            foreach (var verse in t.Verses)
            {
                var vNode = Curiosity.Library.Node.FromKey(nameof(Verse), verse.Key);
                ctx.Link(vNode, nameof(Topic), t.Id, Edges.HasTopic, Edges.TopicOfVerse);
                foreach (var a in ancestors)
                    ctx.Link(vNode, nameof(Topic), a, Edges.InTopicBranch, Edges.TopicBranchOfVerse);
            }
        }

        await ctx.CommitAsync();
    }

    private static IEnumerable<string> Ancestors(Dataset.Topic topic, IReadOnlyDictionary<string, Dataset.Topic> byId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { topic.Id };
        var next = topic.ParentId;
        while (next is not null && byId.TryGetValue(next, out var parent) && seen.Add(parent.Id))
        {
            yield return parent.Id;
            next = parent.ParentId;
        }
    }
}

internal static class QurSimIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        foreach (var p in ctx.Corpus.QurSimPairs)
        {
            var node = ctx.Write(new QurSimPair { PairKey = p.Key, VerseA = p.A.Key, VerseB = p.B.Key, Degree = p.Degree }, p.Key);
            ctx.Link(node, nameof(Verse), p.A.Key, Edges.QurSimPairVerse, Edges.InQurSimPair);
            ctx.Link(node, nameof(Verse), p.B.Key, Edges.QurSimPairVerse, Edges.InQurSimPair);
        }

        await ctx.CommitAsync();
    }
}
