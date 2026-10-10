[schema: Curiosity.Schemas.Key("TopicId")]

public class Topic
{
    public string TopicId { get; set; }

    public string Description { get; set; }

    public string ParentTopicId { get; set; }

    public string TopicFamily { get; set; }

    public string TopicName { get; set; }

    public string TopicNameArabic { get; set; }
}
