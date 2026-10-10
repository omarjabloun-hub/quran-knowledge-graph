[endpoint: Curiosity.Endpoints.Path("app/case/set-decision")]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("app/_lib/helpers")

// Reference template — the attribute header above MUST be the first lines of the file (the
// importer rejects a file that opens with a comment).
// Definitions path: code/endpoints/app/case/set-decision.cs
//
// A WRITE endpoint. Note what is NOT here: [Curiosity.Endpoints.ReadOnly]. The runtime hands a
// read-only endpoint a read-only scope, so the write below would fail at RUN TIME, not at
// import — which makes this the easiest mistake on the project to ship.
//
// This is also the one edge the connector never writes. Everything else the graph knows is
// derived from the corpus; a confirmed impact is a human judgement made in the UI — which is
// exactly what makes it meaningful to compare against the ground-truth findings the connector
// DOES write.

var req      = Body.FromJson<TD.DecisionRequest>();
var caseUID  = req?.CaseUID ?? default(UID128);
var targets  = req?.TargetUIDs ?? new UID128[0];
var decision = req?.Decision ?? TD.DECISION_NONE;

if (caseUID == default(UID128))     return BadRequest("caseUID is required");
if (targets.Length == 0)            return BadRequest("at least one targetUID is required");

if (decision != TD.DECISION_CONFIRMED && decision != TD.DECISION_REJECTED && decision != TD.DECISION_NONE)
    return BadRequest($"decision must be {TD.DECISION_CONFIRMED}, {TD.DECISION_REJECTED} or {TD.DECISION_NONE}");

// Check the TYPE, not just that the UID resolves.
if (!Graph.TryGet(caseUID, out ReadOnlyNode c) || c.Type != N.Case.Type)
    return NotFound($"Case '{caseUID}' not found.");

// Batched on purpose: the candidate list is multi-select, and one request per row would turn a
// single gesture into twenty round-trips.
//
// THE WRITE API. Graph here is the workspace's scoped graph, not the connector's client: it has
// no Link / Unlink / CommitPendingAsync. Take BOTH ends under one lock (two separate locks are how
// two concurrent decisions deadlock), change the edges on the locked nodes, commit both.
var affected = 0;

foreach (var targetUID in targets)
{
    if (targetUID == caseUID) continue;                                   // locking one node twice deadlocks
    if (!Graph.TryGet(targetUID, out ReadOnlyNode target)) continue;
    if (!TD.ImpactableTypes.Contains(target.Type)) continue;              // never hang a decision off a keyword

    var (locked, other) = await Graph.TryGetLockedAsync(caseUID, targetUID);

    try
    {
        // The two edges are mutually exclusive by construction, so setting either clears the
        // other — in BOTH directions. Removing only the forward edge leaves the old decision
        // counting on the target's side.
        locked.RemoveUniqueEdge(E.ConfirmedImpact,   other);
        other .RemoveUniqueEdge(E.ConfirmedImpactOf, locked);
        locked.RemoveUniqueEdge(E.RejectedImpact,    other);
        other .RemoveUniqueEdge(E.RejectedImpactOf,  locked);

        if (decision == TD.DECISION_CONFIRMED)
        {
            locked.AddUniqueEdge(E.ConfirmedImpact,   other);
            other .AddUniqueEdge(E.ConfirmedImpactOf, locked);
        }
        else if (decision == TD.DECISION_REJECTED)
        {
            locked.AddUniqueEdge(E.RejectedImpact,   other);
            other .AddUniqueEdge(E.RejectedImpactOf, locked);
        }

        affected++;
    }
    finally
    {
        await Graph.CommitAsync(locked, other);
    }
}

// Return a VALUE, not a JSON string: the controller serialises whatever the script returns, so
// returning x.ToJson() sends it as text/plain and lets the shape drift from the DTO the front
// end deserialises.
return new TD.OkDto { Ok = affected > 0, Error = affected == 0 ? "no target was a candidate for a decision" : null };
