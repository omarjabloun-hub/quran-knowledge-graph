[endpoint: Curiosity.Endpoints.Path("qkg/clusters/similar-score")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

// Scores a kind of cluster against QurSim (Ibn Kathir's related verses, QurSimPair), the answer
// key the build never reads. Body (optional): { "Kind": "SimilarGroup" } or "Theme".
//   Together   QurSim pairs whose two verses sit in the same group
//   Recall     Together / all QurSim pairs
//   Precision  Together / pairs with both verses in some group
//   Chance     the share of all verse pairs that share a group; Lift = Recall / Chance (1 = no better than chance)
// Run it after each Tau and keep the Tau with the best Lift at a recall you can live with.

class ScoreRequest { public string Kind { get; set; } }
var kind = ParseBody<ScoreRequest>()?.Kind;
if (string.IsNullOrEmpty(kind)) kind = "SimilarGroup";

var groupOf = new Dictionary<string, UID128>();
var sizes = new List<int>();
foreach (var g in Q().StartAt(N.Cluster.Type).AsEnumerable().Where(c => c.GetString(N.Cluster.Kind) == kind))
{
    var count = 0;
    foreach (var p in Q().StartAt(g.UID).Out(N.Cluster.Type, E.HasPassage).AsUIDEnumerable())
        foreach (var v in Q().StartAt(p).Out(N.Verse.Type, E.HasMember).AsEnumerable())
        {
            groupOf[v.GetString(N.Verse.VerseKey)] = g.UID;
            count++;
        }
    sizes.Add(count);
}
if (groupOf.Count == 0) return BadRequest($"No clusters of kind '{kind}'.");

var total = 0; var both = 0; var together = 0;
foreach (var pair in Q().StartAt(N.QurSimPair.Type).AsEnumerable())
{
    total++;
    if (groupOf.TryGetValue(pair.GetString(N.QurSimPair.VerseA), out var a) && groupOf.TryGetValue(pair.GetString(N.QurSimPair.VerseB), out var b))
    {
        both++;
        if (a == b) together++;
    }
}

var nVerses = (double)Q().StartAt(N.Verse.Type).AsUIDEnumerable().Count();
var chance = sizes.Sum(s => s * (s - 1) / 2.0) / (nVerses * (nVerses - 1) / 2.0);
var recall = total > 0 ? (double)together / total : 0;

return new
{
    Kind = kind,
    Groups = sizes.Count,
    VersesGrouped = groupOf.Count,
    QurSimPairs = total,
    BothVersesGrouped = both,
    Together = together,
    Recall = Math.Round(recall, 4),
    Precision = both > 0 ? Math.Round((double)together / both, 4) : 0,
    Chance = Math.Round(chance, 5),
    Lift = chance > 0 ? Math.Round(recall / chance, 1) : 0,
};

