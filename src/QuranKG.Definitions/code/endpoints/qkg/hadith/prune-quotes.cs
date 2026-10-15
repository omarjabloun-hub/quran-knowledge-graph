[endpoint: Curiosity.Endpoints.Path("qkg/hadith/prune-quotes")]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

//ImportEndpoint("qkg/_lib/dto")

// Deletes the hadith–verse links (QuranQuote) an earlier ingest wrote and the latest did not: the
// connector only adds and updates, so a link a better detector no longer finds would stay. Every
// QuranQuote carries the RunId of the ingest that wrote it; those not of the latest run are deleted,
// with the direct Hadith → Verse edge (HadithQuotesVerse / VerseQuotedInHadith) they stood for.
//
// Admin only, not read-only, not cached. scripts/ingest runs it after an ingest that wrote the hadith.

var quotes = Q().StartAt(N.QuranQuote.Type).AsEnumerable().ToList();
if (quotes.Count == 0) return new { Latest = "", Kept = 0, Deleted = 0 };

var latest = quotes.Select(q => q.GetString(N.QuranQuote.RunId) ?? "").Max(StringComparer.Ordinal);
var stale  = quotes.Where(q => (q.GetString(N.QuranQuote.RunId) ?? "") != latest).ToList();

foreach (var q in stale)
{
    CancellationToken.ThrowIfCancellationRequested();
    var hadith = Q().StartAt(q.UID).Out(N.Hadith.Type, E.QuoteInHadith).AsUIDEnumerable().FirstOrDefault();
    var verse  = Q().StartAt(q.UID).Out(N.Verse.Type,  E.QuoteOfVerse).AsUIDEnumerable().FirstOrDefault();

    if (hadith != default(UID128) && verse != default(UID128))
    {
        var (h, v) = await Graph.TryGetLockedAsync(hadith, verse);
        try
        {
            h.RemoveUniqueEdge(E.HadithQuotesVerse,   v);
            v.RemoveUniqueEdge(E.VerseQuotedInHadith, h);
        }
        finally
        {
            await Graph.CommitAsync(h, v);
        }
    }

    await Graph.TryDeleteAsync(q.UID);
    if (hadith != default(UID128)) await Graph.RemoveInvalidEdges(hadith);
    if (verse  != default(UID128)) await Graph.RemoveInvalidEdges(verse);
}

return new { Latest = latest, Kept = quotes.Count - stale.Count, Deleted = stale.Count };
