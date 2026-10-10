using Mosaik;
using Tesserae;
using static Mosaik.UI;
using Node = Mosaik.Schema.Node;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// Everything of the given types that touches this node, in one faceted search area. No edge
    /// filter on purpose: the edges the connector wrote and the ones the workspace's NLP adds
    /// (<c>_Mentions</c> / <c>_AppearsIn</c>) both belong here — a reader should not have to know
    /// which mechanism found a connection.
    /// </summary>
    internal static class View360
    {
        public static IComponent For(Node node, string[] targetTypes)
            => SearchArea()
               .WithFacets()
               .OnSearch(sr => sr.WithTargetQuery(Mosaik.API.Query.StartAt(node.UID).Out(nodeTypes: targetTypes).TakeAll()));
    }
}
