using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoxelCAD.Preferences;
using Xunit;

namespace VoxelCAD.Preferences.Tests;

/// <summary>
/// The file that makes connecting an agent a matter of pointing at a path. It
/// lands in places that already hold other servers' entries, so what it leaves
/// alone matters as much as what it writes.
/// </summary>
public sealed class McpClientConfigWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"voxelcad-config-{Guid.NewGuid():N}");

    private readonly string _path;

    public McpClientConfigWriterTests()
    {
        _path = Path.Combine(_directory, ".mcp.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ItWritesTheServerEntryAClientExpects()
    {
        Assert.True(McpClientConfigWriter.TryWrite(
            _path,
            "http://127.0.0.1:8971/mcp",
            "abc123",
            out string? error));

        Assert.Null(error);

        JsonNode entry = Read()["mcpServers"]!["voxelcad"]!;

        Assert.Equal("http", (string?)entry["type"]);
        Assert.Equal("http://127.0.0.1:8971/mcp", (string?)entry["url"]);
        Assert.Equal("Bearer abc123", (string?)entry["headers"]!["Authorization"]);
    }

    [Fact]
    public void ItCreatesTheDirectoryOnTheWay()
    {
        var nested = Path.Combine(_directory, "one", "two", ".mcp.json");

        Assert.True(McpClientConfigWriter.TryWrite(nested, "http://127.0.0.1:1/mcp", "t", out _));
        Assert.True(File.Exists(nested));
    }

    /// <summary>
    /// The obvious place to point this is a repository whose <c>.mcp.json</c>
    /// already names another server — Beckett's, in this one. Taking that away to
    /// add ours would be a poor trade.
    /// </summary>
    [Fact]
    public void ItLeavesOtherServersWhereTheyAre()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _path,
            """
            {
              "mcpServers": {
                "beckett": { "type": "http", "url": "http://127.0.0.1:8770/mcp" }
              }
            }
            """);

        McpClientConfigWriter.TryWrite(_path, "http://127.0.0.1:8971/mcp", "t", out _);

        JsonNode servers = Read()["mcpServers"]!;

        Assert.Equal("http://127.0.0.1:8770/mcp", (string?)servers["beckett"]!["url"]);
        Assert.Equal("http://127.0.0.1:8971/mcp", (string?)servers["voxelcad"]!["url"]);
    }

    [Fact]
    public void ItKeepsKeysItKnowsNothingAbout()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, """{ "somethingElse": { "kept": true } }""");

        McpClientConfigWriter.TryWrite(_path, "http://127.0.0.1:8971/mcp", "t", out _);

        Assert.True((bool)Read()["somethingElse"]!["kept"]!);
    }

    [Fact]
    public void RewritingReplacesOurOwnEntryRatherThanAddingASecond()
    {
        McpClientConfigWriter.TryWrite(_path, "http://127.0.0.1:8971/mcp", "old", out _);
        McpClientConfigWriter.TryWrite(_path, "http://127.0.0.1:9000/mcp", "new", out _);

        JsonNode entry = Read()["mcpServers"]!["voxelcad"]!;

        Assert.Equal("http://127.0.0.1:9000/mcp", (string?)entry["url"]);
        Assert.Equal("Bearer new", (string?)entry["headers"]!["Authorization"]);
    }

    [Fact]
    public void AFileThatIsNotJsonIsReplacedRatherThanReported()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, "this was never JSON");

        Assert.True(McpClientConfigWriter.TryWrite(_path, "http://127.0.0.1:8971/mcp", "t", out _));
        Assert.NotNull(Read()["mcpServers"]!["voxelcad"]);
    }

    [Fact]
    public void APathThatCannotBeWrittenIsReportedNotThrown()
    {
        Directory.CreateDirectory(_directory);

        // A directory where the file should be: the write cannot succeed, and the
        // caller gets told instead of the editor falling over.
        Directory.CreateDirectory(_path);

        Assert.False(McpClientConfigWriter.TryWrite(_path, "http://127.0.0.1:8971/mcp", "t", out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private JsonNode Read() =>
        JsonNode.Parse(File.ReadAllText(_path)) ?? throw new JsonException("Empty config.");
}
