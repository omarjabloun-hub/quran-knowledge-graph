[schema: Curiosity.Schemas.Key("RecitationKey")]

public class Recitation
{
    public string RecitationKey { get; set; }

    public string AudioUrl { get; set; }

    public int DurationMs { get; set; }

    public string EstimatedAyahs { get; set; }

    public string ReciterId { get; set; }

    public int RecordedVerses { get; set; }

    public int SurahNumber { get; set; }

    public string VerseStartsMs { get; set; }
}
