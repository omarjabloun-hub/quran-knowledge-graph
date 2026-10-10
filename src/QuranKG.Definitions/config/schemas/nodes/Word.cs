[schema: Curiosity.Schemas.Key("WordKey")]

public class Word
{
    public string WordKey { get; set; }

    public int AyahNumber { get; set; }

    public string Features { get; set; }

    public string LemmaText { get; set; }

    public string PartOfSpeech { get; set; }

    public string PosTag { get; set; }

    public int Position { get; set; }

    public string RootLetters { get; set; }

    public string SegmentTags { get; set; }

    public string Segments { get; set; }

    public int SurahNumber { get; set; }

    public string TextArabic { get; set; }

    public string TextPlain { get; set; }

    public int VerbForm { get; set; }

    public string VerseKey { get; set; }
}
