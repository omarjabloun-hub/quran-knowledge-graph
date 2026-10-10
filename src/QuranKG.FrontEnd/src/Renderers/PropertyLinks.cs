using System.Collections.Generic;
using QuranKG.FrontEnd.Schema;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>A property that is really a reference: the node types and the edge that reach what it names.</summary>
    internal sealed class PropertyLink
    {
        public PropertyLink(string[] nodeTypes, string edgeType)
        {
            NodeTypes = nodeTypes;
            EdgeType  = edgeType;
        }

        public string[] NodeTypes { get; }
        public string   EdgeType  { get; }
    }

    /// <summary>
    /// Which properties are references, and over which edge. A verse's <c>SurahName</c> is the
    /// surah, a word's <c>RootLetters</c> is the root: drawn as text they are dead ends, drawn as
    /// the node they open it. The property stays on the node — it is what search reads, and what
    /// the row falls back to when the edge is missing.
    ///
    /// <para>This mapping cannot be derived: that a property is reached by a given edge is a
    /// decision the connector made (src/QuranKG.Connector/Ingest.cs). Every entry is written with
    /// N and E constants, so a rename on either side is a build error.</para>
    /// </summary>
    internal static class PropertyLinks
    {
        private static readonly Dictionary<string, PropertyLink> _links = new Dictionary<string, PropertyLink>
        {
            { Key(N.Surah.Type, N.Surah.RevelationPlace), To(N.Revelation.Type, E.SurahRevealedIn) },

            { Key(N.Verse.Type, N.Verse.SurahName),       To(N.Surah.Type,      E.InSurah)         },
            { Key(N.Verse.Type, N.Verse.Juz),             To(N.Juz.Type,        E.InJuz)           },
            { Key(N.Verse.Type, N.Verse.RevelationPlace), To(N.Revelation.Type, E.RevealedIn)      },

            { Key(N.Word.Type, N.Word.VerseKey),          To(N.Verse.Type,      E.InVerse)         },
            { Key(N.Word.Type, N.Word.SurahNumber),       To(N.Surah.Type,      E.WordInSurah)     },
            { Key(N.Word.Type, N.Word.RootLetters),       To(N.Root.Type,       E.HasRoot)         },
            { Key(N.Word.Type, N.Word.LemmaText),         To(N.Lemma.Type,      E.HasLemma)        },

            { Key(N.Lemma.Type, N.Lemma.RootLetters),     To(N.Root.Type,       E.LemmaHasRoot)    },

            { Key(N.Topic.Type, N.Topic.ParentTopicId),   To(N.Topic.Type,      E.ParentTopic)     },
        };

        /// <summary>The link for this property of this type, or null when it is a plain value.</summary>
        public static PropertyLink For(string nodeType, string propertyName)
            => _links.TryGetValue(Key(nodeType, propertyName), out var link) ? link : null;

        private static string Key(string nodeType, string propertyName) => nodeType + "." + propertyName;

        private static PropertyLink To(string nodeType, string edgeType) => new PropertyLink(new[] { nodeType }, edgeType);
    }
}
