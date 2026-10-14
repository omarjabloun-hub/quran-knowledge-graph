[endpoint: Curiosity.Endpoints.Path("qkg/clusters/similar-sweep")]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

// Builds the SimilarGroup clusters at several Tau values and scores each against QurSim
// (qkg/clusters/similar-score), so the Tau can be chosen from numbers. Also scores the Themes as a baseline.
// Body (optional): { "Taus": [2.0, 2.5, 3.0], "Keep": 2.5 }. Only the LAST build stays in the graph, so the
// sweep ends by rebuilding at Keep (default: the Tau with the best Lift).
// Admin only, not read-only; a rebuild replaces the current SimilarGroup clusters.

class SweepRequest { public double[] Taus { get; set; } public double? Keep { get; set; } }
var req = ParseBody<SweepRequest>() ?? new SweepRequest();
var taus = req.Taus is { Length: > 0 } ? req.Taus : new[] { 2.0, 2.5, 3.0 };

var results = new List<object>();
var best = (Tau: taus[0], Lift: double.NegativeInfinity);

async Task<Newtonsoft.Json.Linq.JToken> Call(string path, string body)
    => await RunEndpointAsync<Newtonsoft.Json.Linq.JToken>(new EndpointUID(path), body, cancellationToken: CancellationToken);

var baseline = await Call("qkg/clusters/similar-score", "{\"Kind\":\"Theme\"}");

async Task<double> BuildAndScore(double tau)
{
    var build = await Call("qkg/clusters/similar-build", "{\"Tau\":" + tau.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
    var score = await Call("qkg/clusters/similar-score", "{\"Kind\":\"SimilarGroup\"}");
    results.Add(new { Tau = tau, Build = build, Score = score });
    return score?.Value<double?>("Lift") ?? 0;
}

foreach (var tau in taus)
{
    CancellationToken.ThrowIfCancellationRequested();
    var lift = await BuildAndScore(tau);
    if (lift > best.Lift) best = (tau, lift);
}

var keep = req.Keep ?? best.Tau;
if (keep != taus[taus.Length - 1]) await BuildAndScore(keep);

return new { ThemeBaseline = baseline, Sweep = results, KeptTau = keep, BestLiftTau = best.Tau };

