using System.Collections.Generic;
using System.Text.Json;
using VoxelCAD.Commands;
using VoxelCAD.Mcp;
using Xunit;

namespace VoxelCAD.Mcp.Tests;

/// <summary>
/// A command whose result carries a base64 image reaches the client as an MCP
/// image block, not as a wall of text — the transport half of the agent's "see"
/// tool. The convention lives only in the server; the command layer stays
/// transport-agnostic.
/// </summary>
public sealed class McpImageContentTests
{
    private const string Base64Png = "iVBORw0KGgo=";

    private readonly IMcpServer _server;

    public McpImageContentTests()
    {
        var imageCommand = new CommandDefinition(
            new CommandDescriptor("frame", "A fake capture.", System.Array.Empty<CommandParameter>(), mutates: false),
            (_, _) => CommandResult.Success(new Dictionary<string, object?>
            {
                ["mimeType"] = "image/png",
                ["width"] = 640,
                ["image"] = Base64Png
            }));

        ICommandDispatcher dispatcher = CommandFactory.CreateDispatcher(
            new CommandContext(),
            new[] { imageCommand });

        _server = McpFactory.CreateServer(dispatcher);
    }

    [Fact]
    public void AnImageResultIsRenderedAsAnImageBlock()
    {
        JsonElement content = Call("frame").GetProperty("content")[0];

        Assert.Equal("image", content.GetProperty("type").GetString());
        Assert.Equal("image/png", content.GetProperty("mimeType").GetString());
        Assert.Equal(Base64Png, content.GetProperty("data").GetString());
    }

    /// <summary>
    /// The metadata rides along as structured content — but not the base64 image,
    /// which would double the payload.
    /// </summary>
    [Fact]
    public void AnImageResultCarriesItsMetadataButNotACopyOfTheImage()
    {
        JsonElement result = Call("frame");
        JsonElement structured = result.GetProperty("structuredContent");

        Assert.Equal(640, structured.GetProperty("width").GetInt32());
        Assert.False(structured.TryGetProperty("image", out _));
        Assert.False(result.GetProperty("isError").GetBoolean());
    }

    private JsonElement Call(string name)
    {
        var request =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\","
            + "\"params\":{\"name\":\"" + name + "\",\"arguments\":{}}}";

        string? response = _server.HandleMessageAsync(request, "test-client").GetAwaiter().GetResult();

        return JsonDocument.Parse(response!).RootElement.GetProperty("result");
    }
}
