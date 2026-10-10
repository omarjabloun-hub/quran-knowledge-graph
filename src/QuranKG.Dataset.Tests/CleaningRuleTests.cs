using QuranKG.Dataset.Sources;
using QuranKG.Dataset.Text;
using Xunit;

namespace QuranKG.Dataset.Tests;

/// <summary>The cleaning rules, on small inputs — no data/raw needed.</summary>
public class CleaningRuleTests
{
    [Fact]
    public void Kaggle_verse_dict_parses_both_quote_styles_and_keeps_the_id_verbatim()
    {
        var d = PythonLiteral.ParseDict("{'id': 100.2, 'content': 'الم', 'translation_eng': \"Abraham's creed\", 'transliteration': 'a\\'b\\\\c'}");

        Assert.Equal("100.2", d["id"]);
        Assert.Equal("الم", d["content"]);
        Assert.Equal("Abraham's creed", d["translation_eng"]);
        Assert.Equal("a'b\\c", d["transliteration"]);
    }

    [Theory]
    [InlineData("1.2", 1)]      // ayah 1 of surah 2
    [InlineData("100.2", 100)]
    [InlineData("1.10", 1)]     // ayah 1 of surah 10 — as a float this would read 1.1
    [InlineData("11.10", 11)]
    public void Kaggle_ayah_is_the_integer_part_of_the_id(string id, int ayah)
        => Assert.Equal(ayah, KaggleDataset.AyahFromId(id));

    [Theory]
    [InlineData("AL-FĀTIḤAH", "Al-Fātiḥah")]
    [InlineData("ĀL ʿIMRĀN", "Āl ʿImrān")]
    [InlineData("THE OPENING", "The Opening")]
    public void Kaggle_names_are_title_cased(string raw, string expected)
        => Assert.Equal(expected, KaggleDataset.TitleCase(raw));

    [Fact]
    public void Bismillah_prefix_is_stripped_from_simple_clean_text()
    {
        var (text, stripped) = Arabic.StripBismillahPrefix("بسم الله الرحمن الرحيم الم");
        Assert.True(stripped);
        Assert.Equal("الم", text);
    }

    [Fact]
    public void Bismillah_prefix_is_stripped_from_uthmani_text_with_its_diacritics_and_tatweel()
    {
        var (text, stripped) = Arabic.StripBismillahPrefix("بِسْمِ ٱللَّهِ ٱلرَّحْمَـٰنِ ٱلرَّحِيمِ الٓمٓ");
        Assert.True(stripped);
        Assert.Equal("الٓمٓ", text);
    }

    [Fact]
    public void A_verse_without_the_bismillah_is_left_alone()
    {
        var (text, stripped) = Arabic.StripBismillahPrefix("الحمد لله رب العالمين");
        Assert.False(stripped);
        Assert.Equal("الحمد لله رب العالمين", text);
    }

    [Fact]
    public void Pause_marks_are_not_words()
        => Assert.Equal(3, Arabic.WordTokens("ذَٰلِكَ ٱلْكِتَـٰبُ لَا ۛ").Count);

    [Fact]
    public void Diacritics_and_alef_wasla_are_removed()
        => Assert.Equal("الرحمن", Arabic.StripTashkeel("ٱلرَّحْمَٰنِ"));

    [Fact]
    public void Division_numbers_come_from_the_start_verses()
    {
        var verses = new[] { new VerseRef(1, 1), new VerseRef(1, 2), new VerseRef(2, 1), new VerseRef(2, 2) };
        var starts = new[] { new VerseRef(1, 1), new VerseRef(2, 2) };

        Assert.Equal(new[] { 1, 1, 1, 2 }, TanzilMetadata.Assign(verses, starts));
    }

    [Fact]
    public void A_word_takes_its_root_and_lemma_from_the_stem_not_the_prefix()
    {
        var word = Morphology.ToWord(new VerseRef(1, 1), 3,
        [
            new Segment("ٱل", "P", ["DET", "PREF", "LEM:ال"]),
            new Segment("رَّحْمَٰنِ", "N", ["ROOT:رحم", "LEM:رَحْمٰن", "MS", "GEN", "ADJ"]),
        ]);

        Assert.Equal("رحم", word.Root);
        Assert.Equal("رَحْمٰن", word.Lemma);
        Assert.Equal("رَحْمٰن|رحم|N", word.LemmaKey);
        Assert.Equal("Adjective", word.PartOfSpeech);
        Assert.Equal("ٱلرَّحْمَٰنِ", word.TextArabic);
        Assert.Equal("الرحمن", word.TextPlain);
        Assert.Equal("MS|GEN|ADJ", word.Features);
    }

    [Fact]
    public void Verb_form_and_aspect_are_read_from_the_stem()
    {
        var word = Morphology.ToWord(new VerseRef(1, 5), 4,
            [new Segment("نَسْتَعِينُ", "V", ["IMPF", "VF:10", "ROOT:عون", "LEM:اسْتَعِينُ", "1P", "MOOD:IND"])]);

        Assert.Equal(10, word.VerbForm);
        Assert.Equal("Imperfect verb", word.PartOfSpeech);
    }

    [Fact]
    public void QurSim_drops_bad_rows_and_merges_duplicates_keeping_the_highest_grade()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join('\n',
            "ID,Source_Sura,Source_Verse,target_sura,Target_verse,common_roots,relevance_degree",
            "1,1,1,1,2,0,1",      // kept
            "2,1,2,1,1,0,2",      // same pair, reversed, higher grade -> one pair, degree 2
            "3,1,3,1,3,0,2",      // self pair -> dropped
            "4,1,4,,5,0,1",       // malformed -> dropped
            "5,1,4,200,1,0,1",    // verse does not exist -> dropped
            "6,1,4,1,5,0,`relevance`")); // malformed grade -> dropped

        var result = QurSim.Read(path, v => v.Surah == 1 && v.Ayah is >= 1 and <= 7);

        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new VerseRef(1, 1), pair.A);
        Assert.Equal(new VerseRef(1, 2), pair.B);
        Assert.Equal(2, pair.Degree);
        Assert.Equal(6, result.RowsRead);
        Assert.Equal(4, result.RowsDropped);
    }
}
