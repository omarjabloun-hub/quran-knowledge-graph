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
}
