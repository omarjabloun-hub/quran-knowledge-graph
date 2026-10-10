using Curiosity.Library;
using QuranKG.Dataset;

namespace QuranKG.Connector;

/// <summary>
/// Everything a handler needs: the graph, the loaded corpus and the run's counters.
///
/// A dry run has no graph at all. Curiosity.Library's WithDryRun still starts its commit thread,
/// which posts to the workspace and crashes the process on a placeholder token — so in a dry run
/// nothing is handed to the library: nodes become key references and only the counters move.
/// </summary>
internal sealed class ConnectorContext(Graph? graph, Corpus corpus)
{
    private readonly Dictionary<string, int> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _edges = new(StringComparer.Ordinal);

    public Corpus Corpus { get; } = corpus;
    public bool   DryRun => graph is null;

    /// <summary>Writes the node with its whole content — once per run (AddOrUpdate replaces the content).</summary>
    public Node Write<T>(T node, string key) where T : class
    {
        CountNode(typeof(T).Name);
        return graph is null ? Node.FromKey(typeof(T).Name, key) : graph.AddOrUpdate(node);
    }

    /// <summary>
    /// Links to a node by key without fetching it. The edge activates once both ends exist, so a
    /// handler can link forward to a type written later in the run.
    /// </summary>
    public void Link(Node from, string toType, string toKey, string forward, string reverse)
    {
        if (string.IsNullOrWhiteSpace(toKey)) return;
        CountEdge(forward);
        graph?.Link(from, Node.FromKey(toType, toKey), forward, reverse);
    }

    public Task CommitAsync() => graph is null ? Task.CompletedTask : graph.CommitPendingAsync();

    public void CountNode(string type) => _nodes[type] = _nodes.GetValueOrDefault(type) + 1;
    public void CountEdge(string edge) => _edges[edge] = _edges.GetValueOrDefault(edge) + 1;

    public int TotalNodes => _nodes.Values.Sum();
    public int TotalEdges => _edges.Values.Sum();

    public void PrintSummary()
    {
        Console.WriteLine("\nNODES");
        foreach (var (type, count) in _nodes.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {type,-24} {count,9:N0}");

        Console.WriteLine("\nEDGES (forward direction; each is written with its reverse)");
        foreach (var (edge, count) in _edges.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {edge,-24} {count,9:N0}");
    }
}
