namespace QuranKG.Workspace;

/// <summary>
/// The repository's .env, read the way the scripts read it: KEY=VALUE lines, # comments, optional
/// quotes. A variable already set in the process environment wins over the file, so a script (or
/// a CI job) can override one value without editing .env.
/// </summary>
internal sealed class Settings
{
    private readonly Dictionary<string, string> _values;

    private Settings(string root, Dictionary<string, string> values)
    {
        Root    = root;
        _values = values;
    }

    public string Root    { get; }
    public string EnvFile => Path.Combine(Root, ".env");

    public static Settings Load()
    {
        var root   = FindRoot();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var file   = Path.Combine(root, ".env");

        if (File.Exists(file))
        {
            foreach (var (key, value) in Parse(File.ReadAllLines(file))) values[key] = value;
        }

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (values.ContainsKey(key) || key.StartsWith("CURIOSITY_", StringComparison.Ordinal) || key.StartsWith("PROD_", StringComparison.Ordinal)
                || key.StartsWith("WORKSPACE_", StringComparison.Ordinal) || key.StartsWith("OLLAMA_", StringComparison.Ordinal) || key == "INTERFACE_UPLOAD_TOKEN" || key == "EMBEDDING_PROVIDER")
            {
                values[key] = (string?)entry.Value ?? "";
            }
        }

        return new Settings(root, values);
    }

    /// <summary>A value, or <paramref name="fallback"/> when it is unset, empty or still the template's placeholder.</summary>
    public string? Get(string key, string? fallback = null)
        => _values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) && !v.StartsWith("replace-with-", StringComparison.Ordinal) ? v : fallback;

    public string Require(string key, string why)
        => Get(key) ?? throw new UsageException($"{key} is not set in .env — {why}");

    /// <summary>The workspace a command acts on: the bare variables are local, the PROD_ ones production.</summary>
    public Target ResolveTarget(string name) => name switch
    {
        "local" => new Target(
            name,
            Get("CURIOSITY_URL", "http://localhost:8080/")!,
            Get("CURIOSITY_API_TOKEN"),
            Get("INTERFACE_UPLOAD_TOKEN"),
            Get("WORKSPACE_ADMIN_USER", "admin")!,
            Get("WORKSPACE_ADMIN_PASSWORD", "admin")!,
            ApiTokenKey: "CURIOSITY_API_TOKEN",
            UploadTokenKey: "INTERFACE_UPLOAD_TOKEN"),

        "prod" => new Target(
            name,
            Require("PROD_CURIOSITY_URL", "--target prod needs the production workspace's URL"),
            Get("PROD_CURIOSITY_API_TOKEN"),
            Get("PROD_INTERFACE_UPLOAD_TOKEN"),
            Require("PROD_WORKSPACE_ADMIN_USER", "needed to sign in to production"),
            Require("PROD_WORKSPACE_ADMIN_PASSWORD", "needed to sign in to production"),
            ApiTokenKey: "PROD_CURIOSITY_API_TOKEN",
            UploadTokenKey: "PROD_INTERFACE_UPLOAD_TOKEN"),

        _ => throw new UsageException($"Unknown --target '{name}' (expected: local | prod)"),
    };

    /// <summary>
    /// Sets KEY=VALUE in .env, in place: the line is replaced where it exists and appended where
    /// it does not, and every other line — comments included — is left as it was.
    /// </summary>
    public void Write(string key, string value)
    {
        var lines   = File.Exists(EnvFile) ? File.ReadAllLines(EnvFile).ToList() : new List<string>();
        var written = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim() == key)
            {
                lines[i] = key + "=" + value;
                written  = true;
            }
        }

        if (!written) lines.Add(key + "=" + value);

        File.WriteAllLines(EnvFile, lines);
        _values[key] = value;
    }

    private static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key   = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];
            else
            {
                var hash = value.IndexOf(" #", StringComparison.Ordinal);
                if (hash >= 0) value = value[..hash].TrimEnd();
            }

            yield return (key, value);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuranKG.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new UsageException("Run from inside the quran-knowledge-graph repository (no QuranKG.slnx found above the current directory).");
    }
}

/// <summary>One workspace and the credentials the scripts hold for it.</summary>
internal sealed record Target(
    string  Name,
    string  Url,
    string? ApiToken,
    string? UploadToken,
    string  AdminUser,
    string  AdminPassword,
    string  ApiTokenKey,
    string  UploadTokenKey);

/// <summary>A mistake in how the tool was called or configured: printed as one line, exit code 2.</summary>
internal sealed class UsageException(string message) : Exception(message);
