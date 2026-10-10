[tools: Curiosity.ChatAITools.UID("<randomly minted uid>")]
[tools: Curiosity.ChatAITools.DisplayName("Case Tools")]
[tools: Curiosity.ChatAITools.Description("Reads an engineer's workload straight from the graph: the cases open on their desk (ListInbox), the ones marked urgent (ListUrgentActions), and the ones nobody owns yet (ListUnassigned). These are the same numbers the workspace home page shows, computed the same way, so the assistant and the dashboard never disagree. Use these for any 'what is on my desk', 'what is urgent', 'what should I pick up' question INSTEAD OF SEARCHING — a search ranks records by text, these read the assignment edges. Each result carries the case's uid, id, title, status, category, due date and urgency.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-clipboard-list")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

// Reference template — definitions path: code/chat-ai-tools/<randomly-minted-uid>.cs
// (The attribute header above must be the first lines; the importer rejects a file that opens
// with a comment.)
//
// The OPERATIONAL layer: questions about the user's own work, not about the corpus.
//
// These read exactly what the dashboard's summary endpoint reads, in the same order, with the
// same definitions — assignment is an edge, the inbox is what is not yet finished. That is
// deliberate and it is the whole point: the user is looking at those counters on the home page
// while they talk to the assistant, and an assistant that quietly counts differently is worse
// than one that cannot count at all.
//
// ITS UID IS RANDOM (UID128.New()), not a hand-shaped one. The recognisable ids belong to the
// BUILT-IN tools, which are re-seeded on every boot and have to be found again by the same id
// each time; a tool defined here is created once by an import and referred to by whatever id it
// was given, so there is nothing a readable id buys — and not every 22-char string round-trips
// through the UID parser.

// Every row carries the case's UID: consult reads any record the user can access, so the uid is
// how the model goes from a row to the full text — and the rows carry enough to answer the
// workload question outright without it.
[ToolSystemPrompt("""
                  Questions about workload — what is on my desk, what is urgent, what is
                  unassigned, what should I pick up next — are answered with the case tools,
                  never with a search: they read the assignment edges, and they return the same
                  numbers the home page shows.

                  Every case they list carries its uid: pass it to consult to read the case in
                  full.
                  """)]
public class CaseTools
{
    private const int DEFAULT_LIMIT = 25;

    [Tool("Lists the cases open on an engineer's desk — assigned to them and not yet validated. This is the 'My inbox' number on the workspace home page, computed the same way. Use it for 'what is on my desk', 'what am I working on', 'what is still open'. Returns each case's uid, docId, title, workflow status, category, due date and whether it is urgent. To read one in full, consult its uid.")]
    //
    // EVERY [Parameter] says required: true or false — the one-argument form no longer makes a
    // tool parameter on 26.10.
    public static string ListInbox(ToolScope scope,
        [Parameter("The engineer whose inbox to read — user name or full name. Leave empty for the current user.", required: false)] string engineer = null,
        [Parameter("Maximum cases to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        var userUID = ResolveEngineer(scope, engineer);
        if (userUID == default(UID128)) return "No such engineer.";

        var cases = Owned(scope, userUID).Where(c => !IsValidated(c)).ToList();

        scope.SetToolCallDisplayName($"Reading {cases.Count} open case(s)");

        return Respond(cases, limit);
    }

    [Tool("Lists the cases assigned to an engineer that are marked urgent — a blocking finding, or a due date inside the next two weeks. This is the 'Urgent' number on the home page. Use it for 'what is urgent', 'what is late', 'what needs me today'. Returns the same fields as ListInbox, earliest due first.")]
    public static string ListUrgentActions(ToolScope scope,
        [Parameter("The engineer whose urgent work to read. Leave empty for the current user.", required: false)] string engineer = null,
        [Parameter("Maximum cases to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        var userUID = ResolveEngineer(scope, engineer);
        if (userUID == default(UID128)) return "No such engineer.";

        var cases = Owned(scope, userUID)
            .Where(c => c.GetBool(N.Case.IsUrgent))
            .OrderBy(c => c.GetString(N.Case.DueDate))            // earliest due first
            .ToList();

        return Respond(cases, limit);
    }

    [Tool("Lists the cases nobody owns yet. This is the 'Unassigned' number on the home page. Use it for 'what is waiting for an owner', 'what could I pick up'. It is workspace-wide rather than per engineer, so it takes no engineer parameter.")]
    public static string ListUnassigned(ToolScope scope,
        [Parameter("Maximum cases to return (default 25).", required: false)] int limit = DEFAULT_LIMIT)
    {
        // "Unowned" is "no user points at it" — not a flag beside the record that a second
        // write could leave stale.
        var assigned = new HashSet<UID128>(
            scope.Q().StartAt(N._User.Type).Out(N.Case.Type, E.HasAssignment).AsUIDEnumerable());

        var cases = scope.Q().StartAt(N.Case.Type).AsEnumerable()
            .Where(c => !assigned.Contains(c.UID))
            .OrderBy(c => c.GetString(N.Case.DueDate))            // earliest due first
            .ToList();

        scope.SetToolCallDisplayName($"Reading {cases.Count} unassigned case(s)");

        return Respond(cases, limit);
    }

    // -----------------------------------------------------------------------------------
    // shared — ONE definition of "owned" and "finished", matching the dashboard exactly
    // -----------------------------------------------------------------------------------

    // ReadOnlyNode, not Node: reads in the tool scope hand out ReadOnlyNode, and Node has no
    // getters.
    private static IEnumerable<ReadOnlyNode> Owned(ToolScope scope, UID128 userUID)
        => scope.Q().StartAt(userUID).Out(N.Case.Type, E.HasAssignment).AsEnumerable();

    private static bool IsValidated(ReadOnlyNode c) => c.GetString(N.Case.WorkflowStatus) == "Validated";

    private static UID128 ResolveEngineer(ToolScope scope, string engineer)
    {
        // scope.CurrentUser IS the user's UID128 — there is no .UID on it.
        if (string.IsNullOrWhiteSpace(engineer)) return scope.CurrentUser;

        foreach (var user in scope.Q().StartAt(N._User.Type).AsEnumerable())
        {
            if (string.Equals(user.GetString(N._User.UserName), engineer, StringComparison.OrdinalIgnoreCase))
                return user.UID;
        }

        return default(UID128);
    }

    /// <summary>Each row is self-sufficient — see the note at the top of this file.</summary>
    private static string Respond(List<ReadOnlyNode> cases, int limit)
    {
        var rows = cases.Take(limit).Select(c => new
        {
            uid            = c.UID.ToString(),
            docId          = c.GetString(N.Case.DocId),
            title          = c.GetString(N.Case.Title),
            workflowStatus = c.GetString(N.Case.WorkflowStatus),
            category       = c.GetString(N.Case.CategoryL4),
            dueDate        = c.GetString(N.Case.DueDate),
            isUrgent       = c.GetBool(N.Case.IsUrgent),
            // No finding counts: they count the EXPECTED findings — the ground truth the
            // retrieval is scored against — and no AI path may read the answer key.
        }).ToArray();

        // Say so when the answer was cut short: a silent truncation reads as "that is all of
        // them".
        var note = cases.Count > limit ? $" (showing {limit} of {cases.Count})" : "";

        return new { count = cases.Count, note, cases = rows }.ToJson();
    }
}

// A tool script ENDS by returning the instance; without it the tool loads as
// "Can't parse tool (Tool is null)" and exposes no functions.
return new CaseTools();
