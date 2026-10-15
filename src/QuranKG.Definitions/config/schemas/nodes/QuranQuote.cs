[schema: Curiosity.Schemas.Key("QuoteKey")]

public class QuranQuote
{
    public string QuoteKey { get; set; }

    public string HadithKey { get; set; }

    public string Kind { get; set; }

    public string Quoted { get; set; }

    public string RunId { get; set; }

    public string VerseKey { get; set; }

    public int VerseWords { get; set; }

    public int Words { get; set; }
}
