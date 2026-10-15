[schema: Curiosity.Schemas.Key("EntryKey")]

public class TafsirEntry
{
    public string EntryKey { get; set; }

    public int FirstAyah { get; set; }

    public string FirstVerseKey { get; set; }

    public string Language { get; set; }

    public int LastAyah { get; set; }

    public string LastVerseKey { get; set; }

    public int SurahNumber { get; set; }

    public string TafsirId { get; set; }

    public string Text { get; set; }
}
