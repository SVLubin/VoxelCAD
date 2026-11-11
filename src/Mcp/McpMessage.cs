using System.Text.Json;

namespace VoxelCAD.Mcp;

/// <summary>
/// The little a transport has to know about the message it is carrying. Only one
/// question so far, and it is genuinely the transport's: whether this is the
/// handshake, which is the one request that cannot yet carry a session id and so
/// the one to answer with a session id to adopt.
/// </summary>
internal static class McpMessage
{
    /// <summary>
    /// Whether this is the <c>initialize</c> handshake. A malformed body is not —
    /// deciding that is the server's job, and it will say so properly.
    /// </summary>
    public static bool IsInitialize(string json)
    {
        try
        {
            JsonElement message = JsonDocument.Parse(json).RootElement;

            return message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("method", out JsonElement method)
                && method.ValueKind == JsonValueKind.String
                && method.GetString() == "initialize";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
