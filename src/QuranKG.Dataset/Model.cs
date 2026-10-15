namespace QuranKG.Dataset;

/// <summary>A verse reference. The join key of every source: <c>surah:ayah</c>.</summary>
public readonly record struct VerseRef(int Surah, int Ayah) : IComparable<VerseRef>
{
    public string Key => $"{Surah}:{Ayah}";

    public static VerseRef Parse(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var s) || !int.TryParse(parts[1], out var a))
            throw new FormatException($"Not a verse key: '{key}'");
        return new VerseRef(s, a);
    }

    public int CompareTo(VerseRef other) => Surah != other.Surah ? Surah.CompareTo(other.Surah) : Ayah.CompareTo(other.Ayah);

    public override string ToString() => Key;
}

public sealed class Surah
{
    public required int    Number           { get; init; }
    public required string NameArabic       { get; init; }
    /// <summary>Tanzil's ASCII transliteration, e.g. "Al-Baqara".</summary>
    public required string NameLatin        { get; init; }
    /// <summary>Kaggle's scholarly transliteration, title-cased, e.g. "Al-Baqarah"; empty when absent.</summary>
    public required string NameScholarly    { get; init; }
    public required string NameEnglish      { get; init; }
    /// <summary>"Meccan" or "Medinan".</summary>
    public required string RevelationPlace  { get; init; }
    public required int    RevelationOrder  { get; init; }
    public required int    AyahCount        { get; init; }
    public required int    RukuCount        { get; init; }
    /// <summary>Mushaf position (1–6,236) of the surah's first verse.</summary>
    public required int    FirstVerseIndex  { get; init; }
    /// <summary>Kaggle's English introduction to the surah; empty when absent.</summary>
    public required string Introduction     { get; init; }
    public string Key => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class Verse
{
    public required VerseRef Ref             { get; init; }
    /// <summary>Position in the mushaf, 1–6,236.</summary>
    public required int      MushafIndex     { get; init; }
    /// <summary>Tanzil Uthmani text with diacritics and pause marks, Bismillah prefix removed. For display.</summary>
    public required string   TextUthmani     { get; init; }
    /// <summary>Tanzil Simple-Clean: standard spelling, no diacritics, Bismillah prefix removed. For search and embeddings.</summary>
    public required string   TextSimple      { get; init; }
    public required string   EnglishQarai    { get; init; }
    public required string   EnglishSaheeh   { get; init; }
    /// <summary>Kaggle's Latin transliteration; empty when absent.</summary>
    public required string   Transliteration { get; init; }
    public required int      Juz             { get; init; }
    public required int      Hizb            { get; init; }
    public required int      HizbQuarter     { get; init; }
    public required int      Manzil          { get; init; }
    public required int      Ruku            { get; init; }
    public required int      Page            { get; init; }
    /// <summary>"", "recommended" or "obligatory".</summary>
    public required string   SajdaType       { get; init; }
    public required string   RevelationPlace { get; init; }
    public string Key => Ref.Key;
}

/// <summary>One morphological segment of a word: a prefix, the stem, or a suffix.</summary>
public sealed record Segment(string Form, string Tag, IReadOnlyList<string> Features)
{
    public bool IsPrefix => Features.Contains("PREF");
    public bool IsSuffix => Features.Contains("SUFF");
    public bool IsStem   => !IsPrefix && !IsSuffix;
}

public sealed class Word
{
    public required VerseRef               Verse          { get; init; }
    /// <summary>1-based position of the word in its verse (the corpus numbering).</summary>
    public required int                    Position       { get; init; }
    /// <summary>The word as the corpus spells it: its segments joined, Uthmani orthography, with diacritics.</summary>
    public required string                 TextArabic     { get; init; }
    /// <summary><see cref="TextArabic"/> without diacritics — the form distinct words are counted by.</summary>
    public required string                 TextPlain      { get; init; }
    public required IReadOnlyList<Segment> Segments       { get; init; }
    /// <summary>Root letters of the stem, e.g. "رحم"; null for particles and words without a root.</summary>
    public required string?                Root           { get; init; }
    /// <summary>Dictionary form of the stem, e.g. "رَحْمٰن".</summary>
    public required string?                Lemma          { get; init; }
    /// <summary>The Lemma node key: lemma, root and coarse tag, because 13 lemma spellings name two different words.</summary>
    public required string?                LemmaKey       { get; init; }
    /// <summary>Coarse tag of the stem: N, V or P.</summary>
    public required string                 PosTag         { get; init; }
    /// <summary>Readable part of speech of the stem, e.g. "Proper noun", "Imperfect verb", "Preposition".</summary>
    public required string                 PartOfSpeech   { get; init; }
    /// <summary>The stem's morphological features without ROOT/LEM, e.g. "IMPF|VF:1|1P|MOOD:IND".</summary>
    public required string                 Features       { get; init; }
    /// <summary>Verb form 1–12 when the stem carries one, else 0.</summary>
    public required int                    VerbForm       { get; init; }
    public string Key => $"{Verse.Surah}:{Verse.Ayah}:{Position}";
}

public sealed class Root
{
    public required string Letters { get; init; }
    /// <summary>The letters separated by spaces, e.g. "ر ح م" — how a root is written for a reader.</summary>
    public string Spaced => string.Join(' ', Letters.EnumerateRunes().Select(r => r.ToString()));
}

public sealed class Lemma
{
    public required string  Key          { get; init; }
    public required string  Text         { get; init; }
    public required string  TextPlain    { get; init; }
    public required string? Root         { get; init; }
    public required string  PosTag       { get; init; }
    public required string  PartOfSpeech { get; init; }
}

public sealed class Juz
{
    public required int      Number     { get; init; }
    public required VerseRef FirstVerse { get; init; }
    public string Key => Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class Topic
{
    public required string                  Id          { get; init; }
    public required string                  Name        { get; init; }
    public required string                  NameArabic  { get; init; }
    /// <summary>"Ontology", "Thematic" or "General" in QUL's taxonomy.</summary>
    public required string                  Family      { get; init; }
    public required string?                 ParentId    { get; init; }
    public required string                  Description { get; init; }
    /// <summary>The verses QUL assigns to this topic directly.</summary>
    public required IReadOnlyList<VerseRef> Verses      { get; init; }
}

/// <summary>
/// Two verses Ibn Kathir's tafsir relates (QurSim). The answer key for the similarity work:
/// it is scored against, never used as a signal and never shown to the AI.
/// </summary>
public sealed class QurSimPair
{
    /// <summary>The earlier verse in mushaf order.</summary>
    public required VerseRef A      { get; init; }
    public required VerseRef B      { get; init; }
    /// <summary>0 = relation not obvious, 1 = related, 2 = strongly related (the highest grade seen for the pair).</summary>
    public required int      Degree { get; init; }
    public string Key => $"{A.Key}~{B.Key}";
}

/// <summary>A tafsir in one language — an edition. Key: "mukhtasar-ar", "ibn-kathir-en" ….</summary>
public sealed class TafsirEdition
{
    public required string Id          { get; init; }
    /// <summary>The work, shared by its editions: "mukhtasar", "jalalayn", "ibn-kathir".</summary>
    public required string Work        { get; init; }
    /// <summary>"ar" or "en".</summary>
    public required string Language    { get; init; }
    /// <summary>Its directory under data/raw/tafsir/.</summary>
    public required string Directory   { get; init; }
    public required string Name        { get; init; }
    public required string NameArabic  { get; init; }
    public required string Author      { get; init; }
    public required string Description { get; init; }
    public required string Attribution { get; init; }
}

/// <summary>
/// One commentary of an edition, on one verse or on a run of verses it explains together. Text for
/// people to read: never a similarity signal, and never handed to the AI — Ibn Kathir's is where
/// QurSim, the similarity answer key, was drawn from.
/// </summary>
public sealed class TafsirEntry
{
    public required string   Edition { get; init; }
    public required VerseRef First   { get; init; }
    public required VerseRef Last    { get; set; }
    /// <summary>Plain text, paragraphs separated by a blank line.</summary>
    public required string   Text    { get; init; }
    public string Key => First == Last ? $"{Edition}:{First.Key}" : $"{Edition}:{First.Key}-{Last.Ayah}";
    public IEnumerable<VerseRef> Verses => Enumerable.Range(First.Ayah, Last.Ayah - First.Ayah + 1).Select(a => new VerseRef(First.Surah, a));
}
