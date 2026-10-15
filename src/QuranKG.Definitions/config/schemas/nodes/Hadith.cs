[schema: Curiosity.Schemas.Key("HadithKey")]

public class Hadith
{
    public string HadithKey { get; set; }

    public string BookName { get; set; }

    public int BookNumber { get; set; }

    public string CollectionId { get; set; }

    public bool IsVariant { get; set; }

    public string Keywords { get; set; }

    public string Matn { get; set; }

    public string Number { get; set; }

    public int NumberInBook { get; set; }

    public string Reference { get; set; }

    public int Sequence { get; set; }

    public string TextArabic { get; set; }

    public string TextEnglish { get; set; }

    public string TranslatorCites { get; set; }
}
