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

internal static class TafsirIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        var c = ctx.Corpus;

        foreach (var (edition, rank) in c.TafsirEditions.Select((e, i) => (e, i + 1)))
            ctx.Write(new Tafsir
            {
                TafsirId         = edition.Id,
                Work             = edition.Work,
                Language         = edition.Language,
                TafsirName       = edition.Name,
                TafsirNameArabic = edition.NameArabic,
                Author           = edition.Author,
                Description      = edition.Description,
                Attribution      = edition.Attribution,
                Rank             = rank,
            }, edition.Id);

        var language = c.TafsirEditions.ToDictionary(e => e.Id, e => e.Language, StringComparer.Ordinal);

        foreach (var e in c.TafsirEntries)
        {
            var node = ctx.Write(new TafsirEntry
            {
                EntryKey      = e.Key,
                TafsirId      = e.Edition,
                Language      = language[e.Edition],
                SurahNumber   = e.First.Surah,
                FirstVerseKey = e.First.Key,
                LastVerseKey  = e.Last.Key,
                FirstAyah     = e.First.Ayah,
                LastAyah      = e.Last.Ayah,
                Text          = e.Text,
            }, e.Key);

            ctx.Link(node, nameof(Tafsir), e.Edition, Edges.EntryOfTafsir, Edges.TafsirHasEntry);
            foreach (var v in e.Verses)
                ctx.Link(node, nameof(Verse), v.Key, Edges.ExplainsVerse, Edges.HasTafsirEntry);
        }

        await ctx.CommitAsync();
    }
}

internal static class HadithIngest
{
    public static async Task RunAsync(ConnectorContext ctx)
    {
        var c = ctx.Corpus;
        if (c.Hadiths.Count == 0) return;

        foreach (var col in c.HadithCollections)
            ctx.Write(new HadithCollection
            {
                CollectionId         = col.Id,
                CollectionName       = col.Name,
                CollectionNameArabic = col.NameArabic,
                Compiler             = col.Compiler,
                Translator           = col.Translator,
            }, col.Id);

        var bookName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in c.HadithBooks)
        {
            bookName[b.Key] = b.Name;
            var node = ctx.Write(new HadithBook
            {
                BookKey        = b.Key,
                CollectionId   = b.Collection,
                BookNumber     = b.Number,
                BookName       = b.Name,
                BookNameArabic = b.NameArabic,
            }, b.Key);
            ctx.Link(node, nameof(HadithCollection), b.Collection, Edges.BookOfCollection, Edges.CollectionHasBook);
        }

        foreach (var h in c.Hadiths)
        {
            var bookKey = h.Collection + ":" + h.Book.ToString(CultureInfo.InvariantCulture);
            var node = ctx.Write(new Hadith
            {
                HadithKey       = h.Key,
                CollectionId    = h.Collection,
                Number          = h.Number,
                Sequence        = h.Sequence,
                Reference       = h.Reference,
                BookNumber      = h.Book,
                BookName        = bookName.GetValueOrDefault(bookKey, ""),
                NumberInBook    = h.NumberInBook,
                TextArabic      = h.TextArabic,
                Matn            = h.Matn,
                IsVariant       = h.IsVariant,
                TextEnglish     = h.TextEnglish,
                TranslatorCites = string.Join(", ", h.TranslatorCites.Select(v => v.Key)),
                Keywords        = string.Join(" ", h.Keywords),
            }, h.Key);

            ctx.Link(node, nameof(HadithCollection), h.Collection, Edges.InCollection, Edges.CollectionHasHadith);
            if (bookName.ContainsKey(bookKey)) ctx.Link(node, nameof(HadithBook), bookKey, Edges.InHadithBook, Edges.HadithBookHasHadith);
        }

        // Every link carries this run's id: a link an older detector found and this one does not is
        // deleted afterwards by qkg/hadith/prune-quotes (scripts/ingest runs it).
        var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        foreach (var q in c.QuranQuotes)
        {
            var node = ctx.Write(new QuranQuote
            {
                QuoteKey   = q.Key,
                HadithKey  = q.HadithKey,
                VerseKey   = q.Verse.Key,
                Kind       = q.Kind,
                Words      = q.Words,
                VerseWords = q.VerseWords,
                Quoted     = q.Quoted,
                RunId      = runId,
            }, q.Key);

            ctx.Link(node, nameof(Hadith), q.HadithKey, Edges.QuoteInHadith, Edges.HasQuranQuote);
            ctx.Link(node, nameof(Verse),  q.Verse.Key, Edges.QuoteOfVerse,  Edges.VerseHasQuote);
            if (q.Kind != "translator")
                ctx.Link(Curiosity.Library.Node.FromKey(nameof(Hadith), q.HadithKey), nameof(Verse), q.Verse.Key, Edges.HadithQuotesVerse, Edges.VerseQuotedInHadith);
        }

        await ctx.CommitAsync();
    }
}

/// <summary>
/// The reciters and their recited surahs, each linked to its surah: the times are written as
/// milliseconds, one per verse of the corpus, in the recitation node itself — a surah's whole
/// timing is read at once, never a verse alone.
/// </summary>
/// <summary>The readings, and the verses of each one that is not the corpus's own text, linked to Hafs's.</summary>
internal static class RiwayaIngest
{
    public static Task RunAsync(ConnectorContext ctx)
    {
        var c    = ctx.Corpus;
        var hizb = c.Verses.ToDictionary(v => v.Ref, v => v.Hizb);

        foreach (var r in c.Riwayat)
            ctx.Write(new Riwaya
            {
                RiwayaId         = r.Id,
                RiwayaName       = r.Name,
                RiwayaNameArabic = r.NameArabic,
                Qari             = r.Qari,
                Rawi             = r.Rawi,
                VerseCount       = r.VerseCount,
                Region           = r.Region,
                Description      = r.Description,
                Attribution      = r.Attribution,
                Rank             = r.Rank,
                IsCorpusText     = r.IsCorpusText,
            }, r.Id);

        foreach (var v in c.RiwayaVerses)
        {
            var node = ctx.Write(new RiwayaVerse
            {
                RiwayaVerseKey = v.Key,
                RiwayaId       = v.Riwaya,
                SurahNumber    = v.Surah,
                AyahNumber     = v.Ayah,
                VerseKey       = v.VerseKey,
                Text           = v.Text,
                Page           = v.Page,
                Juz            = v.Juz,
                Hizb           = hizb.GetValueOrDefault(v.HafsVerses[0]),
                LineStart      = v.LineStart,
                LineEnd        = v.LineEnd,
                HafsVerseKeys  = string.Join(' ', v.HafsVerses.Select(h => h.Key)),
                WordLinks      = string.Join(' ', v.Words.Select(w => w is null ? "-" : $"{w.Value.Verse.Key}:{w.Value.Position}")),
            }, v.Key);
            ctx.Link(node, nameof(Riwaya), v.Riwaya, Edges.InRiwaya, Edges.RiwayaHasVerse);
            ctx.Link(node, nameof(Surah), v.Surah.ToString(CultureInfo.InvariantCulture), Edges.RiwayaVerseInSurah, Edges.SurahHasRiwayaVerse);
            foreach (var h in v.HafsVerses)
                ctx.Link(node, nameof(Verse), h.Key, Edges.ReadsVerse, Edges.VerseReadIn);
        }

        return Task.CompletedTask;
    }
}

internal static class RecitationIngest
{
    public static Task RunAsync(ConnectorContext ctx)
    {
        var c = ctx.Corpus;

        foreach (var r in c.Reciters)
            ctx.Write(new Reciter
            {
                ReciterId         = r.Id,
                ReciterName       = r.Name,
                ReciterNameArabic = r.NameArabic,
                Riwaya            = r.Riwaya,
                RiwayaArabic      = r.RiwayaArabic,
                Country           = r.Country,
                Style             = r.Style,
                Source            = r.Source,
                SourceUrl         = r.SourceUrl,
                Attribution       = r.Attribution,
            }, r.Id);

        foreach (var r in c.Recitations)
        {
            var node = ctx.Write(new Recitation
            {
                RecitationKey  = r.Key,
                ReciterId      = r.Reciter,
                SurahNumber    = r.Surah,
                AudioUrl       = r.AudioUrl,
                DurationMs     = (int)Math.Round(r.Duration * 1000),
                RecordedVerses = r.RecordedVerses,
                VerseStartsMs  = string.Join(' ', r.Verses.Select(v => ((int)Math.Round(v.Start * 1000)).ToString(CultureInfo.InvariantCulture))),
                EstimatedAyahs = string.Join(' ', r.Verses.Where(v => !v.Exact).Select(v => v.Ayah.ToString(CultureInfo.InvariantCulture))),
                RecordedStartsMs = string.Join(' ', r.Marks.Select(m => ((int)Math.Round(m * 1000)).ToString(CultureInfo.InvariantCulture))),
            }, r.Key);
            ctx.Link(node, nameof(Surah), r.Surah.ToString(CultureInfo.InvariantCulture), Edges.RecitesSurah, Edges.SurahRecitedIn);
            ctx.Link(node, nameof(Reciter), r.Reciter, Edges.RecitedBy, Edges.ReciterOf);
        }

        return Task.CompletedTask;
    }
}
