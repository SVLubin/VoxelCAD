using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using VoxelCAD.Commands;

namespace VoxelCAD.Mcp;

/// <inheritdoc cref="IMcpServer"/>
internal sealed class McpServer : IMcpServer
{
    private const string ProtocolVersion = "2025-06-18";
    private const int InvalidParams = -32602;
    private const int ParseError = -32700;
    private const int InternalError = -32603;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Func<string, ICommandDispatcher> _dispatcherForClient;
    private readonly string _name;
    private readonly string _version;

    public McpServer(
        Func<string, ICommandDispatcher> dispatcherForClient,
        string name,
        string version)
    {
        _dispatcherForClient = dispatcherForClient;
        _name = name;
        _version = version;
    }

    public async Task<string?> HandleMessageAsync(string json, string clientId)
    {
        JsonElement message;

        try
        {
            message = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException exception)
        {
            return Error(null, ParseError, exception.Message);
        }

        JsonElement? id = message.TryGetProperty("id", out JsonElement idElement)
            ? idElement
            : null;

        if (!message.TryGetProperty("method", out JsonElement methodElement))
            return Error(id, InvalidParams, "Message has no method.");

        var method = methodElement.GetString() ?? string.Empty;
        JsonElement? parameters = message.TryGetProperty("params", out JsonElement paramsElement)
            ? paramsElement
            : null;

        try
        {
            return method switch
            {
                "initialize" => Result(id, Initialize(parameters)),
                "tools/list" => Result(id, ListTools(clientId)),
                "tools/call" => await CallToolAsync(id, parameters, clientId).ConfigureAwait(false),
                "ping" => Result(id, new Dictionary<string, object?>()),

                // Notifications carry no id and expect no reply.
                _ when id == null => null,
                _ => Error(id, InvalidParams, $"Unsupported method '{method}'.")
            };
        }
        catch (Exception exception)
        {
            return Error(id, InternalError, exception.Message);
        }
    }

    private Dictionary<string, object?> Initialize(JsonElement? parameters)
    {
        // Echo the client's version when we know it, otherwise answer with ours
        // and let the client decide whether it can live with that.
        var requested = parameters?.TryGetProperty("protocolVersion", out JsonElement version) == true
            ? version.GetString()
            : null;

        return new Dictionary<string, object?>
        {
            ["protocolVersion"] = requested == ProtocolVersion ? requested : ProtocolVersion,
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["tools"] = new Dictionary<string, object?>()
            },
            ["serverInfo"] = new Dictionary<string, object?>
            {
                ["name"] = _name,
                ["version"] = _version
            },
            ["instructions"] =
                "Only one agent holds the editor at a time: 'take-control' is refused while "
                + "another agent has it, 'control-status' says who does, and 'wait' on "
                + "'take-control' puts you in line — agents are served in the order they "
                + "asked — and answers the moment the editor is yours. Never read a status as "
                + "permission to edit unless it says youHoldIt. "
                + "Every tool maps to one editor command and is undoable. Elements and "
                + "materials may be addressed by id or by an unambiguous name. Use 'tree' "
                + "and 'history' to see the current state, and 'bounds' then 'slice' to read "
                + "a voxel model back — a slice draws one layer as a character grid, which is "
                + "how to check that what was built is what was meant. "
                + "The editor can also drive its own interface: 'ui' lists the controls with "
                + "the references the 'input-*' tools aim at, and 'window' pictures the whole "
                + "window. That is a debug instrument for exercising the interface — the "
                + "transform handles above all — and it needs the input-injection preference "
                + "switched on; 'preferences' says whether it is. Build models with "
                + "'set-voxels', 'fill-box' and 'set-transform', never by clicking."
        };
    }

    private Dictionary<string, object?> ListTools(string clientId) =>
        new()
        {
            ["tools"] = _dispatcherForClient(clientId).Commands
                .Select(command => (object?)ToolSchemaHelper.ToTool(command))
                .ToList()
        };

    private async Task<string> CallToolAsync(JsonElement? id, JsonElement? parameters, string clientId)
    {
        ICommandDispatcher dispatcher = _dispatcherForClient(clientId);

        if (parameters?.TryGetProperty("name", out JsonElement nameElement) != true)
            return Error(id, InvalidParams, "tools/call needs a tool name.");

        var name = nameElement.GetString() ?? string.Empty;

        // An unknown tool is a protocol error; a tool that ran and refused is not.
        if (dispatcher.Commands.All(command => command.Name != name))
            return Error(id, InvalidParams, $"Unknown tool '{name}'.");

        Dictionary<string, string> arguments =
            parameters.Value.TryGetProperty("arguments", out JsonElement argumentsElement)
                ? ToStringArguments(argumentsElement)
                : new Dictionary<string, string>();

        CommandResult result = dispatcher.Execute(name, arguments);

        // A command may answer twice: once to say it is waiting, and once with how
        // the wait ended. Only the handover does — see CommandResult.Pending — and
        // this is the transport that can afford to hold the reply, because the
        // command has already let the main thread go.
        if (result.Pending != null)
            result = await result.Pending.ConfigureAwait(false);

        if (result.Ok && TryBuildImageContent(result.Value, out object? imageContent, out object? metadata))
        {
            // A captured frame reaches the model as an image block, not as a wall
            // of base64 text — that is the whole point of a "see" tool. The bytes
            // are large, so they go there and nowhere else; the rest of the
            // result — size, format — rides along as structured content, without a
            // second copy of the image in it.
            return Result(id, new Dictionary<string, object?>
            {
                ["content"] = new List<object?> { imageContent },
                ["structuredContent"] = metadata,
                ["isError"] = false
            });
        }

        var text = result.Ok
            ? JsonSerializer.Serialize(result.Value ?? new Dictionary<string, object?>(), JsonOptions)
            : result.Error!;

        var payload = new Dictionary<string, object?>
        {
            ["content"] = new List<object?>
            {
                new Dictionary<string, object?> { ["type"] = "text", ["text"] = text }
            },
            ["isError"] = !result.Ok
        };

        // MCP requires structuredContent to be a JSON object. A command whose
        // result is a bare list — scenes, materials — keeps it in the text
        // content alone: clients that validate the shape refused the whole
        // answer when an array landed there.
        if (result.Ok && result.Value is System.Collections.IDictionary)
            payload["structuredContent"] = result.Value;

        return Result(id, payload);
    }

    /// <summary>
    /// A command whose result carries a base64 image and its MIME type is rendered
    /// as an MCP image content block. The convention is minimal and lives only
    /// here: the command layer stays transport-agnostic, and only the one transport
    /// that can show a picture knows how.
    /// </summary>
    private static bool TryBuildImageContent(object? value, out object? content, out object? metadata)
    {
        content = null;
        metadata = null;

        if (value is not IReadOnlyDictionary<string, object?> fields
            || !fields.TryGetValue("image", out object? image)
            || image is not string data
            || !fields.TryGetValue("mimeType", out object? mime)
            || mime is not string mimeType)
        {
            return false;
        }

        content = new Dictionary<string, object?>
        {
            ["type"] = "image",
            ["data"] = data,
            ["mimeType"] = mimeType
        };

        // Everything the command reported except the base64 itself, so the size
        // and format still reach the client without doubling the payload.
        var rest = new Dictionary<string, object?>();

        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (field.Key != "image")
                rest[field.Key] = field.Value;
        }

        metadata = rest;

        return true;
    }

    /// <summary>
    /// The schema asks for strings, but a model may well send a number or an
    /// array anyway. Accepting those costs nothing and avoids a refusal that
    /// would teach it nothing.
    /// </summary>
    private static Dictionary<string, string> ToStringArguments(JsonElement arguments)
    {
        var valuesByNameDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (arguments.ValueKind != JsonValueKind.Object)
            return valuesByNameDictionary;

        foreach (JsonProperty property in arguments.EnumerateObject())
            valuesByNameDictionary[property.Name] = ToText(property.Value);

        return valuesByNameDictionary;
    }

    private static string ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Array => string.Join(',', value.EnumerateArray().Select(ToText)),
        _ => value.GetRawText()
    };

    private static string Result(JsonElement? id, object payload) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = ToIdValue(id),
                ["result"] = payload
            },
            JsonOptions);

    private static string Error(JsonElement? id, int code, string message) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = ToIdValue(id),
                ["error"] = new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["message"] = message
                }
            },
            JsonOptions);

    private static object? ToIdValue(JsonElement? id) => id?.ValueKind switch
    {
        JsonValueKind.Number => id.Value.GetInt64(),
        JsonValueKind.String => id.Value.GetString(),
        _ => null
    };
}
