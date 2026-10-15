using QuranKG.Dataset.Sources;
using QuranKG.Dataset.Text;

namespace QuranKG.Dataset;

/// <summary>The joined, validated corpus every downstream project reads.</summary>
public sealed class Corpus
{
    public required IReadOnlyList<Surah>      Surahs      { get; init; }
    public required IReadOnlyList<Verse>      Verses      { get; init; }
    public required IReadOnlyList<Word>       Words       { get; init; }
    public required IReadOnlyList<Root>       Roots       { get; init; }
    public required IReadOnlyList<Lemma>      Lemmas      { get; init; }
    public required IReadOnlyList<Juz>        Juzs        { get; init; }
    public required IReadOnlyList<Topic>      Topics      { get; init; }
    public required IReadOnlyList<QurSimPair> QurSimPairs { get; init; }
    public required IReadOnlyList<TafsirEdition> TafsirEditions { get; init; }
    public required IReadOnlyList<TafsirEntry>   TafsirEntries  { get; init; }
    public required IReadOnlyList<HadithCollection> HadithCollections { get; init; }
    public required IReadOnlyList<HadithBook>       HadithBooks       { get; init; }
    public required IReadOnlyList<Hadith>           Hadiths           { get; init; }
    public required IReadOnlyList<QuranQuote>       QuranQuotes       { get; init; }
    /// <summary>What the loader cleaned, and the optional sources it did without.</summary>
    public required IReadOnlyList<string>     Notes       { get; init; }

    public IReadOnlyDictionary<VerseRef, IReadOnlyList<Word>> WordsByVerse =>
        _wordsByVerse ??= Words.GroupBy(w => w.Verse).ToDictionary(g => g.Key, g => (IReadOnlyList<Word>)g.OrderBy(w => w.Position).ToList());

    private IReadOnlyDictionary<VerseRef, IReadOnlyList<Word>>? _wordsByVerse;
}

public sealed class CorpusException(IReadOnlyList<string> problems)
    : Exception("The corpus failed validation:\n  - " + string.Join("\n  - ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

public static class CorpusLoader
{
    /// <summary>How many distinctive words a hadith keeps, and the share of hadith a word may be in and still count (قال, الله, رسول are in more).</summary>
    public const int    HadithKeywords        = 12;
    public const double HadithKeywordMaxShare = 0.05;

    /// <summary>Particles and pronouns the stemmer leaves whole (it does not cut a word of three letters).</summary>
    private static readonly HashSet<string> FunctionWords = new(StringComparer.Ordinal)
    {
        "ومن", "فمن", "فلا", "ولا", "وما", "فما", "فان", "وان", "فاذا", "واذا", "ولم", "فلم", "ثم", "اذا", "اذ", "لما", "كيف", "هل", "بل",
        "لكن", "حتي", "قد", "لقد", "فقد", "كل", "بعض", "غير", "عند", "بين", "بعد", "قبل", "اي", "ايضا", "منا", "منكم", "منهم", "عليكم",
        "عليهم", "عليه", "عليها", "اليه", "اليهم", "لنا", "لكم", "لهم", "انت", "انتم", "نحن", "انهم", "انها", "هذه", "هولاء", "ذلك",
        "تلك", "الذي", "التي", "الذين", "فيه", "فيها", "فيهم", "به", "بها", "بهم", "معه", "معهم", "قال", "فقال", "قالت", "يقول",
    };

    public static Corpus Load(DataLayout layout)
    {
        var problems = new List<string>();
        var notes    = new List<string>();

        // ---- the frame: 114 surahs and the verse list, from Tanzil's metadata -------------------
        var meta = TanzilMetadata.Read(layout.TanzilMetadata);
        if (meta.Surahs.Count != 114) problems.Add($"metadata: {meta.Surahs.Count} surahs, expected 114");

        var order = new List<VerseRef>();
        foreach (var s in meta.Surahs)
        {
            if (s.Start != order.Count) problems.Add($"metadata: surah {s.Index} starts at {s.Start}, expected {order.Count}");
            for (var a = 1; a <= s.Ayas; a++) order.Add(new VerseRef(s.Index, a));
        }
        var known = order.ToHashSet();

        // ---- the texts ----------------------------------------------------------------------------
        var uthmani = TanzilText.Read(layout.TanzilUthmani);
        var simple  = TanzilText.Read(layout.TanzilSimpleClean);
        var qarai   = TanzilText.Read(layout.TranslationQarai);
        var sahih   = TanzilText.Read(layout.TranslationSaheeh);

        foreach (var (name, text) in new[] { ("uthmani", uthmani), ("simple-clean", simple), ("en.qarai", qarai), ("en.sahih", sahih) })
        {
            var missing = order.Count(v => !text.ContainsKey(v));
            var extra   = text.Keys.Count(v => !known.Contains(v));
            if (missing > 0 || extra > 0) problems.Add($"{name}: {missing} verses missing, {extra} unknown verses");
        }

        // Tanzil prefixes the Bismillah to verse 1 of every surah but 1 (where it IS verse 1) and 9.
        int strippedU = 0, strippedS = 0;
        foreach (var s in meta.Surahs.Where(s => s.Index is not (1 or 9)))
        {
            var v = new VerseRef(s.Index, 1);
            if (uthmani.TryGetValue(v, out var u)) { var r = Arabic.StripBismillahPrefix(u); if (r.Stripped) { uthmani[v] = r.Text; strippedU++; } }
            if (simple.TryGetValue(v, out var p))  { var r = Arabic.StripBismillahPrefix(p); if (r.Stripped) { simple[v]  = r.Text; strippedS++; } }
        }
        if (strippedU != 112 || strippedS != 112) problems.Add($"Bismillah prefix stripped from {strippedU} Uthmani and {strippedS} Simple-Clean verses, expected 112 each");
        else notes.Add("Bismillah prefix removed from verse 1 of 112 surahs (Tanzil writes it into the verse; it is not part of it).");

        // ---- Kaggle: introductions, scholarly names, transliteration ------------------------------
        var kaggle = Directory.Exists(layout.KaggleCsvDir) ? KaggleDataset.Read(layout.KaggleCsvDir).ToDictionary(k => k.Number) : [];
        if (kaggle.Count == 0) notes.Add("Kaggle dataset not found: no surah introductions or transliteration.");
        else if (kaggle.Count != 114) problems.Add($"kaggle: {kaggle.Count} surah files, expected 114");
        var noTranslit = order.Count(v => !(kaggle.TryGetValue(v.Surah, out var k) && k.Transliteration.ContainsKey(v.Ayah)));
        if (kaggle.Count > 0 && noTranslit > 0) problems.Add($"kaggle: {noTranslit} verses without transliteration");

        // ---- divisions --------------------------------------------------------------------------
        var juz     = TanzilMetadata.Assign(order, meta.JuzStarts);
        var quarter = TanzilMetadata.Assign(order, meta.QuarterStarts);
        var manzil  = TanzilMetadata.Assign(order, meta.ManzilStarts);
        var ruku    = TanzilMetadata.Assign(order, meta.RukuStarts);
        var page    = TanzilMetadata.Assign(order, meta.PageStarts);
        if (juz[^1] != 30 || quarter[^1] != 240 || manzil[^1] != 7 || page[^1] != 604)
            problems.Add($"divisions: last verse is in juz {juz[^1]}, quarter {quarter[^1]}, manzil {manzil[^1]}, page {page[^1]} (expected 30/240/7/604)");

        var surahs = meta.Surahs.Select(s => new Surah
        {
            Number          = s.Index,
            NameArabic      = s.Name,
            NameLatin       = s.TName,
            NameScholarly   = kaggle.TryGetValue(s.Index, out var k) ? k.NameScholarly : "",
            NameEnglish     = s.EName,
            RevelationPlace = s.Type,
            RevelationOrder = s.Order,
            AyahCount       = s.Ayas,
            RukuCount       = s.Rukus,
            FirstVerseIndex = s.Start + 1,
            Introduction    = kaggle.TryGetValue(s.Index, out var k2) ? k2.Description : "",
        }).ToList();
        var place = surahs.ToDictionary(s => s.Number, s => s.RevelationPlace);

        var verses = order.Select((v, i) => new Verse
        {
            Ref             = v,
            MushafIndex     = i + 1,
            TextUthmani     = uthmani.GetValueOrDefault(v, ""),
            TextSimple      = simple.GetValueOrDefault(v, ""),
            EnglishQarai    = qarai.GetValueOrDefault(v, ""),
            EnglishSaheeh   = sahih.GetValueOrDefault(v, ""),
            Transliteration = kaggle.TryGetValue(v.Surah, out var k) ? k.Transliteration.GetValueOrDefault(v.Ayah, "") : "",
            Juz             = juz[i],
            HizbQuarter     = quarter[i],
            Hizb            = (quarter[i] + 3) / 4,
            Manzil          = manzil[i],
            Ruku            = ruku[i],
            Page            = page[i],
            SajdaType       = meta.Sajdas.GetValueOrDefault(v, ""),
            RevelationPlace = place[v.Surah],
        }).ToList();

        // ---- words, roots, lemmas -----------------------------------------------------------------
        var words = Morphology.Read(layout.Morphology);
        var byVerse = words.GroupBy(w => w.Verse).ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());

        var orphan = byVerse.Keys.Count(v => !known.Contains(v));
        if (orphan > 0) problems.Add($"morphology: words for {orphan} verses that do not exist");
        var wordless = order.Count(v => !byVerse.ContainsKey(v));
        if (wordless > 0) problems.Add($"morphology: {wordless} verses have no words");
        var gaps = byVerse.Count(kv => kv.Value.Select((w, i) => w.Position != i + 1).Any(b => b));
        if (gaps > 0) problems.Add($"morphology: {gaps} verses whose word positions are not 1..n");

        // The corpus numbers words as Tanzil's Uthmani text spaces them, with a handful of
        // exceptions (بعدما written as one word, إل ياسين as one). Words display the corpus's
        // own spelling, so nothing depends on the two agreeing — this only reports them.
        var misaligned = verses.Where(v => byVerse.TryGetValue(v.Ref, out var ws) && Arabic.WordTokens(v.TextUthmani).Count != ws.Count).Select(v => v.Ref).ToList();
        notes.Add($"Corpus word numbering differs from Tanzil's Uthmani spacing in {misaligned.Count} verse(s): {string.Join(", ", misaligned.Take(8))}.");

        var roots = words.Where(w => w.Root is not null).Select(w => w.Root!).Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal).Select(r => new Root { Letters = r }).ToList();

        var lemmas = words.Where(w => w.LemmaKey is not null).GroupBy(w => w.LemmaKey!, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                return new Lemma
                {
                    Key          = g.Key,
                    Text         = first.Lemma!,
                    TextPlain    = Arabic.StripTashkeel(first.Lemma!),
                    Root         = first.Root,
                    PosTag       = first.PosTag,
                    PartOfSpeech = g.GroupBy(w => w.PartOfSpeech).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key,
                };
            })
            .OrderBy(l => l.Key, StringComparer.Ordinal).ToList();

        var juzs = meta.JuzStarts.Select((v, i) => new Juz { Number = i + 1, FirstVerse = v }).ToList();

        // ---- QurSim: the answer key ----------------------------------------------------------------
        var qursim = QurSim.Read(layout.QurSim, known.Contains);
        notes.Add($"QurSim: {qursim.RowsRead:N0} rows → {qursim.Pairs.Count:N0} distinct verse pairs ({qursim.RowsDropped} malformed, self or out-of-range rows dropped; duplicates merged keeping the highest grade).");

        // ---- QUL topics: optional, behind a login --------------------------------------------------
        IReadOnlyList<Topic> topics = [];
        if (File.Exists(layout.QulTopics))
        {
            topics = QulTopics.Read(layout.QulTopics)
                .Select(t => new Topic { Id = t.Id, Name = t.Name, NameArabic = t.NameArabic, Family = t.Family, ParentId = t.ParentId, Description = t.Description, Verses = t.Verses.Where(known.Contains).ToList() })
                .ToList();
            var ids = topics.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var danglingParents = topics.Count(t => t.ParentId is not null && !ids.Contains(t.ParentId));
            notes.Add($"QUL topics: {topics.Count:N0} topics, {topics.Sum(t => t.Verses.Count):N0} verse assignments{(danglingParents > 0 ? $", {danglingParents} parents not found" : "")}.");
        }
        else
        {
            notes.Add($"QUL topics not found at {layout.QulTopics} — Topic nodes are skipped. See data/manifest.json for the manual download.");
        }

        // ---- tafsir: commentary for people to read ------------------------------------------------
        var editions = new List<TafsirEdition>();
        var entries  = new List<TafsirEntry>();
        foreach (var edition in TafsirSource.Editions)
        {
            if (!Directory.Exists(Path.Combine(layout.TafsirDir, edition.Directory)))
            {
                notes.Add($"Tafsir {edition.Id} not found under {layout.TafsirDir} — run `just fetch`; it is skipped.");
                continue;
            }

            var read = TafsirSource.Read(layout.RawDir, edition, order, problems);
            editions.Add(edition);
            entries.AddRange(read.Entries);
            var ranges = read.Entries.Count(e => e.First != e.Last);
            notes.Add($"Tafsir {edition.Id}: {read.Entries.Count:N0} entries ({ranges:N0} over several verses){(read.Uncommented.Count > 0 ? $", {read.Uncommented.Count} verses without commentary (e.g. {string.Join(", ", read.Uncommented.Take(3))})" : "")}.");
        }

        // ---- hadith: Bukhari and Muslim, and the verses they quote ---------------------------------
        var collections = new List<HadithCollection>();
        var books       = new List<HadithBook>();
        var hadiths     = new List<Hadith>();
        foreach (var collection in HadithSource.Collections)
        {
            if (!File.Exists(Path.Combine(layout.HadithDir, $"ara-{collection.Id}.json")))
            {
                notes.Add($"Hadith {collection.Id} not found under {layout.HadithDir} — run `just fetch`; it is skipped.");
                continue;
            }
            var read = HadithSource.Read(layout.HadithDir, collection, known.Contains);
            collections.Add(collection);
            books.AddRange(read.Books);
            hadiths.AddRange(read.Hadiths);
            notes.Add($"Hadith {collection.Name}: {read.Hadiths.Count:N0} hadith in {read.Books.Count} books ({read.Skipped} empty entries skipped, {read.Hadiths.Count(h => h.IsVariant):N0} that only refer to the one before).");
        }

        // Each hadith's distinctive words: tf·idf over the hadith texts, the rarest across them first.
        if (hadiths.Count > 0)
        {
            var stems = hadiths.Select(h => Arabic.MatchTokens(h.Matn).Select(Arabic.Stem).Where(w => w.Length > 1).ToList()).ToList();
            var df    = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ws in stems) foreach (var w in ws.Distinct(StringComparer.Ordinal)) df[w] = df.GetValueOrDefault(w) + 1;
            var n = (double)hadiths.Count;
            for (var i = 0; i < hadiths.Count; i++)
                hadiths[i].Keywords = stems[i].GroupBy(w => w, StringComparer.Ordinal)
                    .Select(g => (Word: g.Key, Df: df[g.Key], Score: (1 + Math.Log(g.Count())) * Math.Log(n / df[g.Key])))
                    .Where(x => x.Df <= n * HadithKeywordMaxShare && !FunctionWords.Contains(x.Word))
                    .OrderByDescending(x => x.Score).ThenBy(x => x.Word, StringComparer.Ordinal)
                    .Take(HadithKeywords).Select(x => x.Word).ToList();
        }

        var quotes = new List<QuranQuote>();
        var recall = new Dictionary<string, (int Hit, int Of)>(StringComparer.Ordinal);
        if (hadiths.Count > 0)
        {
            var verseWords = verses.Select(v => Arabic.MatchTokens(v.TextSimple)).ToList();
            var found      = new QuranQuotes(verseWords).FindAll(hadiths.Select(h => Arabic.MatchTokensMarked(h.TextArabic)).ToList());

            for (var i = 0; i < hadiths.Count; i++)
            {
                var h = hadiths[i];
                foreach (var f in found[i])
                    quotes.Add(new QuranQuote { HadithKey = h.Key, Verse = verses[f.Verse].Ref, Kind = f.IsQuotation ? "quotation" : "phrase", Words = f.Words, VerseWords = verseWords[f.Verse].Count, Quoted = f.Quoted });

                var quoted = found[i].Select(f => verses[f.Verse].Ref).ToHashSet();
                foreach (var cited in h.TranslatorCites.Where(c => !quoted.Contains(c)))
                    quotes.Add(new QuranQuote { HadithKey = h.Key, Verse = cited, Kind = "translator", Words = 0, VerseWords = verseWords[order.IndexOf(cited)].Count, Quoted = "" });

                // Measured, never read by the detector: does the Arabic quote a verse the translator cites?
                if (h.TranslatorCites.Count > 0)
                {
                    var hit = h.TranslatorCites.Any(quoted.Contains);
                    var key = h.Collection;
                    recall[key] = (recall.GetValueOrDefault(key).Hit + (hit ? 1 : 0), recall.GetValueOrDefault(key).Of + 1);
                }
            }

            foreach (var c in collections)
            {
                var mine = quotes.Where(q => q.HadithKey.StartsWith(c.Id + ":", StringComparison.Ordinal)).ToList();
                var r    = recall.GetValueOrDefault(c.Id);
                notes.Add($"Quran in {c.Name}: {mine.Where(q => q.Kind == "quotation").Select(q => q.HadithKey).Distinct().Count():N0} hadith quote a verse, {mine.Where(q => q.Kind == "phrase").Select(q => q.HadithKey).Distinct().Count():N0} share a phrase with one ({mine.Count(q => q.Kind != "translator"):N0} links); the translator cites a verse in {r.Of:N0} hadith and the Arabic quotes it in {r.Hit:N0} ({(r.Of == 0 ? 0 : 100.0 * r.Hit / r.Of):F0}%).");
            }
        }

        if (problems.Count > 0) throw new CorpusException(problems);

        return new Corpus
        {
            Surahs      = surahs,
            Verses      = verses,
            Words       = words,
            Roots       = roots,
            Lemmas      = lemmas,
            Juzs        = juzs,
            Topics      = topics,
            QurSimPairs = qursim.Pairs,
            TafsirEditions = editions,
            TafsirEntries  = entries,
            HadithCollections = collections,
            HadithBooks       = books,
            Hadiths           = hadiths,
            QuranQuotes       = quotes,
            Notes       = notes,
        };
    }
}
