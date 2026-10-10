using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace QuranKG.Workspace;

/// <summary>
/// The workspace's HTTP API, as far as these scripts use it. Every call here was checked against
/// a running workspace (curiosityai/curiosity build 72111):
///
/// <code>
/// POST   api/login/create                      {"UserName","Password"} → a session token (JWT)
/// GET    api/library/token?purpose=&amp;duration=     → a Library token (days)       — connector, definitions
/// GET    api/library/token-front-end-upload?…  → an Interface Upload token     — the front end
/// GET    api/node/count/{type}                 → how many nodes of a type
/// GET    api/build/compile-status              → {"FailingIds":[…],"Pending":…}
/// GET    api/index/status/all                  → {Pauses, Indexes{uid:…}, Status{uid:{QueueSize,Values{IndexSize}}}}
/// DELETE api/index/delete?nodeType=&amp;indexUID=
/// POST   api/endpoints/run/{path}              → 200 with the result, or 202 + X-MSK-ENDPOINT-KEY: re-post with it
/// </code>
///
/// A session token does not survive a workspace restart, so every command signs in afresh with
/// the administrator in .env rather than storing one.
/// </summary>
internal sealed class WorkspaceClient : IDisposable
{
    private const string POLL_HEADER = "X-MSK-ENDPOINT-KEY";

    private readonly HttpClient _http;

    private WorkspaceClient(HttpClient http) => _http = http;

    public void Dispose() => _http.Dispose();

    public static async Task<WorkspaceClient> SignInAsync(Target target)
    {
        var http = new HttpClient { BaseAddress = new Uri(target.Url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(15) };

        var body     = new JsonObject { ["UserName"] = target.AdminUser, ["Password"] = target.AdminPassword }.ToJsonString();
        var response = await http.PostAsync("api/login/create", new StringContent(body, Encoding.UTF8, "application/json"));
        var text     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new UsageException($"Could not sign in to {target.Url} as '{target.AdminUser}' ({(int)response.StatusCode}). Check WORKSPACE_ADMIN_USER / WORKSPACE_ADMIN_PASSWORD in .env.");

        var token = text.Trim().Trim('"');
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new WorkspaceClient(http);
    }

    public Task<string> MintLibraryTokenAsync(string purpose, int days)
        => GetTextAsync($"api/library/token?purpose={Uri.EscapeDataString(purpose)}&duration={days}");

    public Task<string> MintUploadTokenAsync(string purpose, int days)
        => GetTextAsync($"api/library/token-front-end-upload?purpose={Uri.EscapeDataString(purpose)}&duration={days}");

    public async Task<long> CountAsync(string nodeType)
        => long.TryParse((await GetTextAsync($"api/node/count/{Uri.EscapeDataString(nodeType)}")).Trim().Trim('"'), out var n) ? n : -1;

    public async Task<JsonNode?> GetJsonAsync(string path) => JsonNode.Parse(await GetTextAsync(path));

    public async Task DeleteIndexAsync(string nodeType, string indexUID)
    {
        var response = await _http.DeleteAsync($"api/index/delete?nodeType={Uri.EscapeDataString(nodeType)}&indexUID={Uri.EscapeDataString(indexUID)}");
        await EnsureAsync(response, "delete index " + indexUID);
    }

    /// <summary>Runs a code endpoint and returns its body, following the 202 poll for a slow one.</summary>
    public async Task<(string Body, TimeSpan Elapsed)> RunEndpointAsync(string path, string json)
    {
        var sw       = Stopwatch.StartNew();
        var response = await _http.PostAsync("api/endpoints/run/" + path.TrimStart('/'), new StringContent(json, Encoding.UTF8, "application/json"));

        while (response.StatusCode == HttpStatusCode.Accepted)
        {
            var key = response.Headers.TryGetValues(POLL_HEADER, out var values) ? values.FirstOrDefault() : null;
            if (key is null) break;

            await Task.Delay(500);

            using var poll = new HttpRequestMessage(HttpMethod.Post, "api/endpoints/run/" + path.TrimStart('/'));
            poll.Headers.Add(POLL_HEADER, key);
            response = await _http.SendAsync(poll);
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new UsageException($"{path}: HTTP {(int)response.StatusCode} {body}");

        return (body, sw.Elapsed);
    }

    private async Task<string> GetTextAsync(string path)
    {
        var response = await _http.GetAsync(path);
        await EnsureAsync(response, path);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new UsageException($"{what}: HTTP {(int)response.StatusCode} {(body.Length > 300 ? body[..300] + "…" : body)}");
    }
}
