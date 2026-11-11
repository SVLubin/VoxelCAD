using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VoxelCAD.Preferences;

/// <summary>
/// Writes the file an agent's client reads to find this editor — the same
/// <c>mcpServers</c> shape Beckett drops into a project, with the bearer token
/// the endpoint expects filled in.
/// </summary>
/// <remarks>
/// It lives beside the preferences because its whole content is preferences: the
/// port and the token. It <b>merges</b> rather than overwrites, because the
/// obvious place to point it is a repository that already has a <c>.mcp.json</c>
/// naming other servers — clobbering Beckett's entry to add ours would be a poor
/// trade.
/// </remarks>
public static class McpClientConfigWriter
{
    public const string DefaultFileName = "mcp.json";

    /// <summary>What the endpoint is called in the client's server list.</summary>
    public const string ServerName = "voxelcad";

    private const string ServersKey = "mcpServers";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool TryWrite(string path, string url, string token, out string? error)
    {
        try
        {
            JsonObject root = ReadRoot(path);

            if (root[ServersKey] is not JsonObject servers)
            {
                servers = new JsonObject();
                root[ServersKey] = servers;
            }

            servers[ServerName] = new JsonObject
            {
                ["type"] = "http",
                ["url"] = url,
                ["headers"] = new JsonObject
                {
                    ["Authorization"] = $"Bearer {token}"
                }
            };

            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // The file is a merged one this editor does not own, and other
            // tools watch its mtime. Every preferences change re-applies the
            // endpoint, so an unchanged entry — a panel folded, a threshold
            // nudged — must not churn the file it would rewrite verbatim.
            string content = root.ToJsonString(JsonOptions);

            if (File.Exists(path) && File.ReadAllText(path) == content)
            {
                error = null;

                return true;
            }

            File.WriteAllText(path, content);
            FilePermissions.RestrictToOwner(path);

            error = null;

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            error = exception.Message;

            return false;
        }
    }

    /// <summary>
    /// What is already there, or an empty object. A file that is not JSON at all
    /// is replaced rather than reported: the alternative is an editor that cannot
    /// publish its endpoint until somebody tidies up a file they may not know
    /// exists.
    /// </summary>
    private static JsonObject ReadRoot(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}
