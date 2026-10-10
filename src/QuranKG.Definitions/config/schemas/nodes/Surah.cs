[schema: Curiosity.Schemas.Key("SurahId")]

public class Surah
{
    public string SurahId { get; set; }

    public int AyahCount { get; set; }

    public int FirstVerseIndex { get; set; }

    public string Introduction { get; set; }

    public string NameArabic { get; set; }

    public string NameEnglish { get; set; }

    public string NameLatin { get; set; }

    public string NameScholarly { get; set; }

    public int Number { get; set; }

    public int RevelationOrder { get; set; }

    public string RevelationPlace { get; set; }

    public int RukuCount { get; set; }
}
