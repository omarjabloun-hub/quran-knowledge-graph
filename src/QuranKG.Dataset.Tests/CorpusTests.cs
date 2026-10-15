using Xunit;

namespace QuranKG.Dataset.Tests;

/// <summary>
/// The joined corpus, on the real sources pinned in data/manifest.json. Skipped until
/// `just fetch` has filled data/raw.
/// </summary>
public class CorpusTests
{
    private static readonly Lazy<Corpus?> Loaded = new(() =>
    {
        var layout = DataLayout.Discover(AppContext.BaseDirectory);
        return File.Exists(layout.Morphology) ? CorpusLoader.Load(layout) : null;
    });

    private static Corpus Corpus
    {
        get
        {
            Assert.SkipWhen(Loaded.Value is null, "data/raw is empty — run `just fetch` first.");
            return Loaded.Value!;
        }
    }

    [Fact]
    public void Counts_match_the_pinned_sources()
    {
        Assert.Equal(114,    Corpus.Surahs.Count);
        Assert.Equal(6_236,  Corpus.Verses.Count);
        Assert.Equal(77_429, Corpus.Words.Count);
        Assert.Equal(1_651,  Corpus.Roots.Count);
        Assert.Equal(4_776,  Corpus.Lemmas.Count);
        Assert.Equal(30,     Corpus.Juzs.Count);
        Assert.Equal(7_295,  Corpus.QurSimPairs.Count);
    }

    [Fact]
    public void Verse_one_of_al_baqarah_is_alif_lam_mim_without_the_bismillah()
    {
        var v = Corpus.Verses.Single(x => x.Key == "2:1");
        Assert.Equal("الم", v.TextSimple);
        Assert.Equal("الٓمٓ", v.TextUthmani);
    }

    [Fact]
    public void Al_fatiha_keeps_the_bismillah_because_it_is_its_first_verse()
        => Assert.Equal("بسم الله الرحمن الرحيم", Corpus.Verses.Single(x => x.Key == "1:1").TextSimple);

    [Fact]
    public void Kaggle_transliteration_is_joined_by_ayah_not_by_row()
    {
        // Row 36 of 2.csv is verse 2:130 (rows are sorted by id as text). Joined by row, 2:36
        // would get 2:130's transliteration.
        var v = Corpus.Verses.Single(x => x.Key == "2:130");
        Assert.Contains("ibrāhīm", v.Transliteration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void English_is_clean_qarai_without_inline_footnotes()
    {
        var v = Corpus.Verses.Single(x => x.Key == "1:2");
        Assert.Equal("All praise belongs to Allah, Lord of all the worlds,", v.EnglishQarai);
    }

    [Fact]
    public void Divisions_are_assigned_in_mushaf_order()
    {
        Assert.Equal(1,   Corpus.Verses[0].Juz);
        Assert.Equal(2,   Corpus.Verses.Single(x => x.Key == "2:142").Juz);
        Assert.Equal(30,  Corpus.Verses[^1].Juz);
        Assert.Equal(604, Corpus.Verses[^1].Page);
        Assert.Equal(60,  Corpus.Verses[^1].Hizb);
        Assert.Equal(15,  Corpus.Verses.Count(v => v.SajdaType != ""));
    }

    [Fact]
    public void Every_word_has_a_verse_and_roots_come_from_stems()
    {
        var bismillah = Corpus.WordsByVerse[new VerseRef(1, 1)];
        Assert.Equal(new[] { "سمو", "أله", "رحم", "رحم" }, bismillah.Select(w => w.Root));
        Assert.Equal(new[] { 1, 2, 3, 4 }, bismillah.Select(w => w.Position));
    }

    [Fact]
    public void Surahs_carry_revelation_place_and_order()
    {
        var baqarah = Corpus.Surahs[1];
        Assert.Equal("Medinan", baqarah.RevelationPlace);
        Assert.Equal(87, baqarah.RevelationOrder);
        Assert.Equal(286, baqarah.AyahCount);
        Assert.StartsWith("The surah that mentions the story of The Cow", baqarah.Introduction);
        Assert.Equal(86, Corpus.Surahs.Count(s => s.RevelationPlace == "Meccan"));
    }

    [Fact]
    public void Every_verse_has_al_mukhtasar_in_arabic_and_english()
    {
        foreach (var edition in new[] { "mukhtasar-ar", "mukhtasar-en" })
        {
            var covered = Corpus.TafsirEntries.Where(e => e.Edition == edition).SelectMany(e => e.Verses).ToList();
            Assert.Equal(6_236, covered.Count);
            Assert.Equal(6_236, covered.Distinct().Count());
        }

        var ikhlas = Corpus.TafsirEntries.Single(e => e.Edition == "mukhtasar-en" && e.First.Key == "112:1");
        Assert.StartsWith("Say, O Messenger: He is Allah", ikhlas.Text);
    }

    [Fact]
    public void Ibn_kathir_explains_runs_of_verses_once_without_the_editors_notes()
    {
        var entries = Corpus.TafsirEntries.Where(e => e.Edition == "ibn-kathir-ar").ToList();
        Assert.Equal(6_236, entries.Sum(e => e.Verses.Count()));
        Assert.Contains(entries, e => e.Last.Ayah - e.First.Ayah >= 10);
        Assert.DoesNotContain(entries, e => e.Text.Contains("[["));

        // An entry never crosses into the next surah.
        Assert.All(entries, e => Assert.Equal(e.First.Surah, e.Last.Surah));
    }

    [Fact]
    public void Al_jalalayn_passes_over_some_verses_and_that_is_not_an_error()
    {
        var arabic = Corpus.TafsirEntries.Where(e => e.Edition == "jalalayn-ar").SelectMany(e => e.Verses).ToHashSet();
        Assert.DoesNotContain(new VerseRef(2, 82), arabic);
        Assert.Contains(new VerseRef(2, 83), arabic);
    }

    [Fact]
    public void Bukhari_and_muslim_are_numbered_as_they_are_cited()
    {
        Assert.Equal(7_580, Corpus.Hadiths.Count(h => h.Collection == "bukhari"));
        Assert.Equal(7_360, Corpus.Hadiths.Count(h => h.Collection == "muslim"));

        var neighbour = Corpus.Hadiths.Single(h => h.Key == "bukhari:6018");
        Assert.Equal("Bukhari 6018", neighbour.Reference);
        Assert.Contains("neighbo", neighbour.TextEnglish);

        Assert.Contains(Corpus.Hadiths, h => h.Reference == "Muslim 8a");
        Assert.All(Corpus.HadithBooks.Where(b => b.Number > 0), b => Assert.NotEqual("", b.NameArabic));
    }

    [Fact]
    public void The_matn_leaves_the_chain_of_narrators_out()
    {
        var pillars = Corpus.Hadiths.Single(h => h.Key == "bukhari:8");
        Assert.Contains("بني الإسلام على خمس", pillars.Matn);
        Assert.DoesNotContain("حدثنا", pillars.Matn);
    }

    [Fact]
    public void A_hadith_quoting_a_verse_is_linked_to_it_and_narration_is_not_a_quotation()
    {
        Quote("bukhari:125", "17:85", "quotation");     // ويسألونك عن الروح … whole verse
        Quote("bukhari:3525", "26:214", "quotation");   // وأنذر عشيرتك الأقربين — a verse of three words

        // "من كان يؤمن بالله واليوم الآخر" opens many hadith: a formula in 65:2's words, a phrase, not a quotation.
        Assert.DoesNotContain(Corpus.QuranQuotes, q => q.HadithKey == "bukhari:6018" && q.Verse == new VerseRef(65, 2) && q.Kind == "quotation");

        // "فقال لهم رسول الله" is narration, not 91:13.
        Assert.DoesNotContain(Corpus.QuranQuotes, q => q.Verse == new VerseRef(91, 13) && q.Kind != "translator" && q.Quoted == "فقال لهم رسول الله");
    }

    [Fact]
    public void A_hadith_keeps_its_distinctive_words_not_its_framing()
    {
        var neighbour = Corpus.Hadiths.Single(h => h.Key == "bukhari:6018");
        Assert.Contains("جار", neighbour.Keywords);      // جاره → جار
        Assert.Contains("ضيف", neighbour.Keywords);      // ضيفه → ضيف
        Assert.DoesNotContain("الله", neighbour.Keywords);
    }

    [Fact]
    public void Every_verse_has_its_place_in_the_recitation()
    {
        Assert.Equal(114, Corpus.Recitations.Count);
        Assert.Equal(6_214, Corpus.Recitations.Sum(r => r.RecordedVerses));   // Qalun's Madani count
        foreach (var r in Corpus.Recitations)
        {
            Assert.Equal(Corpus.Surahs[r.Surah - 1].AyahCount, r.Verses.Count);
            for (var i = 0; i < r.Verses.Count; i++)
            {
                Assert.Equal(i + 1, r.Verses[i].Ayah);
                Assert.True(r.Verses[i].End >= r.Verses[i].Start, $"{r.Key} verse {i + 1} ends before it starts");
                if (i > 0) Assert.Equal(r.Verses[i - 1].End, r.Verses[i].Start);
            }
            Assert.Equal(Math.Round(r.Duration, 3), r.Verses[^1].End);
        }
    }

    [Fact]
    public void Al_fatiha_recited_in_qalun_keeps_each_verse_on_its_own_words()
    {
        // Qalun does not count the Bismillah: its verse 1 is «الحمد لله», which is the corpus's 1:2,
        // and its 7th starts at «غير المغضوب عليهم», inside the corpus's 1:7.
        var f = Recited(1);
        Assert.Equal(0, f.Verses[0].Start);
        Assert.Equal(3.08, f.Verses[1].Start);
        Assert.Equal(19.76, f.Verses[6].Start);
        Assert.All(f.Verses, v => Assert.True(v.Exact));
    }

    [Fact]
    public void A_verse_that_starts_inside_a_recorded_verse_is_estimated()
    {
        // Qalun does not count «الم» as a verse: its 2:1 runs on to «هدى للمتقين», so the corpus's 2:2
        // starts inside it — after «الم», before the recording's 2:2.
        var baqarah = Recited(2);
        Assert.True(baqarah.Verses[0].Exact);
        Assert.False(baqarah.Verses[1].Exact);
        Assert.InRange(baqarah.Verses[1].Start, baqarah.Verses[0].Start + 0.5, baqarah.Verses[2].Start - 0.5);

        // Ayat al-Kursi starts where the recording's 2:253 does: Qalun ends that verse at «الحي القيوم»
        // and counts the rest of 2:255 as its 2:254, so the start is the recording's own.
        Assert.True(baqarah.Verses[254].Exact);

        // Qalun's 11:81 ends at «من سجيل»; the corpus's 11:82 starts where it does, its 11:83 inside Qalun's 11:82.
        Assert.True(Recited(11).Verses[81].Exact);
        Assert.False(Recited(11).Verses[82].Exact);
    }

    [Fact]
    public void Two_texts_are_aligned_word_by_word_across_spelling()
    {
        string[] words = ["يا", "ايها", "الناس", "اعبدوا", "ربكم", "الذي", "خلقكم"];
        IReadOnlyList<string>[] verses = [["ياايها", "الناس", "اعبدوا"], ["ربكم", "الذي", "خلقكم"]];
        Assert.Equal([0, 4], Sources.RecitationSource.Align(words, verses));

        // A verse whose first word is spelled differently starts at the word most like it.
        string[] hafs = ["الحمد", "لله", "مالك", "يوم", "الدين"];
        IReadOnlyList<string>[] qalun = [["الحمد", "لله"], ["ملك", "يوم", "الدين"]];
        Assert.Equal([0, 2], Sources.RecitationSource.Align(hafs, qalun));
    }

    private static Recitation Recited(int surah) => Corpus.Recitations.Single(r => r.Surah == surah);

    private static void Quote(string hadith, string verse, string kind)
    {
        var q = Corpus.QuranQuotes.SingleOrDefault(x => x.HadithKey == hadith && x.Verse.Key == verse);
        Assert.NotNull(q);
        Assert.Equal(kind, q.Kind);
    }
}
