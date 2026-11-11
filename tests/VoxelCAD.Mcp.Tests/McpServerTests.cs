using System.Linq;
using System.Text.Json;
using VoxelCAD.Commands;
using VoxelCAD.Mcp;
using Xunit;

namespace VoxelCAD.Mcp.Tests;

/// <summary>
/// The protocol as a client sees it, including the distinction the spec draws
/// between a tool that refused and a request that was never valid.
/// </summary>
public sealed class McpServerTests
{
    private const string ClientId = "test-client";

    private readonly ICommandDispatcher _dispatcher =
        CommandFactory.CreateDispatcher(new CommandContext());

    private readonly IMcpServer _server;

    public McpServerTests()
    {
        _server = McpFactory.CreateServer(_dispatcher);
        Call("new");
    }

    [Fact]
    public void InitializeAnswersWithAVersionAndTheToolsCapability()
    {
        JsonElement result = Send(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""")
            .GetProperty("result");

        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("voxelcad", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public void ANotificationGetsNoReply()
    {
        Assert.Null(Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public void EveryCommandIsPublishedAsAToolWithASchema()
    {
        JsonElement tools = Send("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")
            .GetProperty("result")
            .GetProperty("tools");

        Assert.Equal(_dispatcher.Commands.Count, tools.GetArrayLength());
        Assert.All(tools.EnumerateArray(), tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
        });
    }

    [Fact]
    public void RequiredParametersAreMarkedAsSuch()
    {
        JsonElement schema = Tool("create-element").GetProperty("inputSchema");

        Assert.Contains(
            "name",
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain(
            "parent",
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.True(schema.GetProperty("properties").TryGetProperty("parent", out _));
    }

    [Fact]
    public void ASuccessfulCallCarriesStructuredContent()
    {
        JsonElement result = Call("create-element", "\"name\":\"Bracket\",\"kind\":\"VoxelModel\"");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.True(result.GetProperty("structuredContent").TryGetProperty("id", out _));
        Assert.Equal("text", result.GetProperty("content")[0].GetProperty("type").GetString());
    }

    /// <summary>
    /// MCP requires structuredContent to be an object, and a list command's
    /// result is a bare array — so it rides in the text content alone. A client
    /// that validates the shape used to refuse the whole answer.
    /// </summary>
    [Fact]
    public void AListResultStaysOutOfStructuredContent()
    {
        JsonElement result = Call("scenes");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.False(result.TryGetProperty("structuredContent", out _));

        JsonElement parsed = JsonDocument.Parse(
            result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;

        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
    }

    [Fact]
    public void ARefusedCommandIsAToolErrorNotAProtocolError()
    {
        Call("create-element", "\"name\":\"Group\"");

        JsonElement result = Call(
            "set-field-mode",
            "\"element\":\"Group\",\"channel\":\"Position\",\"mode\":\"Snap90\"");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Snap90", result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    [Fact]
    public void AnUnknownToolIsAProtocolError()
    {
        JsonElement message = Send(
            """{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");

        Assert.Equal(-32602, message.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void ArrayArgumentsAreAcceptedForVectors()
    {
        Call("create-element", "\"name\":\"Model\",\"kind\":\"VoxelModel\"");

        JsonElement result = Call(
            "fill-box",
            "\"element\":\"Model\",\"from\":[0,0,0],\"to\":[1,1,1],\"material\":\"Default\"");

        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal(8, result.GetProperty("structuredContent").GetProperty("voxels").GetInt32());
    }

    [Fact]
    public void MalformedJsonIsReportedNotThrown()
    {
        JsonElement message = Send("{not json");

        Assert.Equal(-32700, message.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData("7", "7")]
    [InlineData("\"abc\"", "abc")]
    public void TheRequestIdComesBackUnchanged(string sent, string expected)
    {
        JsonElement id = Send($$"""{"jsonrpc":"2.0","id":{{sent}},"method":"tools/list"}""")
            .GetProperty("id");

        Assert.Equal(expected, id.ToString());
    }

    [Fact]
    public void NoResponseCarriesAnEmbeddedNewline()
    {
        // The stdio transport frames on newlines, so a message containing one
        // would silently split into two malformed messages.
        Call("create-element", "\"name\":\"Line\\nBreak\"");

        var listed = Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!;
        var tree = Handle(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"tree","arguments":{}}}""")!;

        Assert.DoesNotContain('\n', listed);
        Assert.DoesNotContain('\n', tree);
    }

    private JsonElement Tool(string name) =>
        Send("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Single(tool => tool.GetProperty("name").GetString() == name);

    private JsonElement Call(string name, string arguments = "")
    {
        var request =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\","
            + "\"params\":{\"name\":\"" + name + "\",\"arguments\":{" + arguments + "}}}";

        return Send(request).GetProperty("result");
    }

    private JsonElement Send(string request) =>
        JsonDocument.Parse(Handle(request)!).RootElement;

    /// <summary>
    /// One message in, its reply out. Blocking is safe here: nothing in the console
    /// catalogue leaves an answer pending — only the editor's handover does.
    /// </summary>
    private string? Handle(string request) =>
        _server.HandleMessageAsync(request, ClientId).GetAwaiter().GetResult();
}
