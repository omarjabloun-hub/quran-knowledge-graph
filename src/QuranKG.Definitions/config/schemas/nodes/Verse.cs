[schema: Curiosity.Schemas.Key("VerseKey")]

public class Verse
{
    public string VerseKey { get; set; }

    public int AyahNumber { get; set; }

    public string EnglishQarai { get; set; }

    public string EnglishSaheeh { get; set; }

    public int Hizb { get; set; }

    public int HizbQuarter { get; set; }

    public int Juz { get; set; }

    public int Manzil { get; set; }

    public int MushafIndex { get; set; }

    public int Page { get; set; }

    public string RevelationPlace { get; set; }

    public int Ruku { get; set; }

    public string SajdaType { get; set; }

    public string SurahName { get; set; }

    public int SurahNumber { get; set; }

    public string TextSimple { get; set; }

    public string TextUthmani { get; set; }

    public string Transliteration { get; set; }
}
