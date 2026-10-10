using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Helpers;
using Mosaik.Schema;
using QuranKG.FrontEnd.Schema;
using Tesserae;
using TNT;
using UID;
using static Mosaik.UI;
using static Tesserae.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;
using NodeRendererDefinitionPreview = Mosaik.Components.NodeRendering.NodeRendererDefinitionPreview;

namespace QuranKG.FrontEnd.Renderers
{
    /// <summary>
    /// The shape every node type of this application shares: the card it is drawn as in a list,
    /// the sheet it opens into, and the tabs of that sheet — the type's own tabs first
    /// (<see cref="LeadingTabs"/>: the reading view of a verse, the profile of a surah), then
    /// Overview (its properties, a reference drawn as the node it names), Graph and Connected
    /// (everything that touches it, faceted).
    ///
    /// <para>ABSTRACT on purpose: auto-discovery instantiates every non-abstract INodeRenderer it
    /// finds. Every concrete renderer has a public parameter-less constructor for the same
    /// reason.</para>
    /// </summary>
    public abstract class QuranRendererBase : NodeRendererBase
    {
        private const int LONG_VALUE_THRESHOLD    = 200;
        private const int LONG_VALUE_MAX_HEIGHT   = 160;
        private const int MAX_FOOTER_VALUE_LENGTH = 48;

        protected QuranRendererBase(SchemaStyleInfo style) : base(style)
        {
            ShortDisplayName = DisplayName;
        }

        /// <summary>What the card's ~34px icon tile says: up to five characters, four preferred.</summary>
        protected string ShortDisplayName { get; set; }

        /// <summary>Override where one type holds things a reader tells apart at a glance (Meccan / Medinan).</summary>
        protected virtual string TileText(Node node) => ShortDisplayName;

        /// <summary>The node types the Connected tab lists. Everything, by default.</summary>
        protected virtual string[] Related360Types => Groups.AllTypes;

        protected virtual UnitSize ModalWidth  => 92.vw();
        protected virtual UnitSize ModalHeight => 90.vh();

        // -----------------------------------------------------------------------------------
        // the card
        // -----------------------------------------------------------------------------------

        public override OmniResult<Node> CompactView(Node node)
        {
            var custom = GetCustomRendererIfAny();
            if (custom is object) return NodeRendererDefinitionPreview.WithoutRenderedItemTracking.CompactView(custom, node);

            return WithFooter(WithTile(NodeResult.For(this, node), node).SetContent(CardBody(node)), node);
        }

        /// <summary>
        /// The ONE thing the card shows under its label — the Arabic of a verse, the meaning of a
        /// surah's name — or null to collapse the body. The base would draw every property as a
        /// row, which is thirty rows inside one row of a result list.
        /// </summary>
        protected virtual IComponent CardBody(Node node) => null;

        /// <summary>
        /// The facts that place a node without opening it, read from its own properties (a footer
        /// that walked edges would cost a round-trip per card). Unlabelled, the property name as
        /// tooltip. Return values already formatted: a number read through GetString arrives as text.
        /// </summary>
        protected virtual IEnumerable<string> FooterFacts(Node node) => Enumerable.Empty<string>();

        private OmniResult<Node> WithTile(OmniResult<Node> result, Node node)
        {
            var text = TileText(node) ?? ShortDisplayName;
            return result.SetIcon(text, Color, text.Length > 4 ? TextSize.Tiny : (TextSize?)null);
        }

        /// <summary>
        /// REPLACES the footer NodeResult.For built. Its one entry is the node's timestamp, which
        /// here is only when the connector last ran — nothing a reader of a verse should be told.
        /// </summary>
        private OmniResult<Node> WithFooter(OmniResult<Node> result, Node node)
        {
            result.SetFooterEntries(new InlineLabel[0]);

            foreach (var fact in FooterFacts(node))
            {
                if (string.IsNullOrWhiteSpace(fact) || fact.Length > MAX_FOOTER_VALUE_LENGTH) continue;
                result.AddFooterEntry(InlineLabel(fact));
            }

            return result;
        }

        public override async Task<OmniResult<Node>> PreviewAsync(Node node, Parameters state)
        {
            // A renderer configured in the workspace still wins.
            var custom = GetCustomRendererIfAny();
            if (custom is object) return await NodeRendererDefinitionPreview.WithoutRenderedItemTracking.PreviewAsync(custom, node, state);

            var groups = BuildRelationshipGroups(await LoadSummaryAsync(node));

            return WithFooter(WithTile(NodeResult.For(this, node), node), node)
               .SetModalContent(new NodeView(this, node, state, groups))
               .ModalSize(ModalWidth, ModalHeight);
        }

        // -----------------------------------------------------------------------------------
        // the tabs
        // -----------------------------------------------------------------------------------

        /// <summary>The type's own tabs, shown before the shared ones.</summary>
        protected virtual IEnumerable<NodeTab> LeadingTabs(Node node) => Enumerable.Empty<NodeTab>();

        /// <summary>A tab; every one carries a TabTitle, or the node view throws and takes every node page down.</summary>
        protected static NodeTab Tab(string id, string label, System.Func<IComponent> content, bool scrolls = true)
            => new NodeTab { Id = id, Label = label, Scrolls = scrolls, TabTitle = SegmentTitle(label), TabContent = content };

        // The per-relationship tabs of the base are left out: a verse alone has eight edge groups,
        // and the Connected tab lists all of them in one faceted search.
        protected override IEnumerable<NodeTab> BuildTabs(Node node, IReadOnlyList<RelationshipGroup> groups, NodeView nodeView)
        {
            foreach (var tab in LeadingTabs(nodeView.Node)) yield return tab;

            if (GetDisplayPropertyNames(nodeView.Node).Any())
                yield return Tab("properties", "Overview".t(), () => RenderPropertiesSegment(nodeView.Node));

            yield return Tab("graph",   "Graph".t(),     () => RenderGraphSegment(nodeView.Node, nodeView.OpenGraphNodePeek), scrolls: false);
            yield return Tab("view360", "Connected".t(), () => Components.View360.For(nodeView.Node, Related360Types),     scrolls: false);
        }

        // -----------------------------------------------------------------------------------
        // properties — a reference renders as the node it names
        // -----------------------------------------------------------------------------------

        protected override IComponent RenderProperties(Node node)
        {
            var grid = DetailsGrid().WS().LabelWidth(160.px());

            foreach (var propertyName in GetDisplayPropertyNames(node))
                grid.Row(propertyName.InsertSpaceBeforeUpperCase(), PropertyCell(node, propertyName));

            return grid;
        }

        private IComponent PropertyCell(Node node, string propertyName)
        {
            var link  = PropertyLinks.For(NodeType, propertyName);
            var value = GetValueForProperty(node, propertyName);

            if (link is object)
                return NeighborsLinks(node.UID, link.NodeTypes, link.EdgeType, wrap: true, maxItemsToShow: 12, showIfNone: () => PlainValue(value));

            if (value is object && (value.Length > LONG_VALUE_THRESHOLD || value.Split('\n').Length > 4))
                return VStack().WS().MaxHeight(LONG_VALUE_MAX_HEIGHT.px()).ScrollY()
                   .Style(s => s.setProperty("overflow-wrap", "anywhere"))
                   .Children(TextBlock(value, selectable: true));

            return PlainValue(value);
        }

        private static IComponent PlainValue(string value) => InlineLabel(value).NoMark();
    }
}
