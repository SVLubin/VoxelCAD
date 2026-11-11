using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoxelCAD.Commands;
using VoxelCAD.Mcp;
using Xunit;

namespace VoxelCAD.Mcp.Tests;

/// <summary>
/// The HTTP transport end to end over a real loopback socket: a request marshals
/// onto the pump thread, reaches the server, and comes back — and everything
/// unauthorised or cross-origin is turned away first.
/// </summary>
public sealed class McpHttpHostTests : IDisposable
{
    private const string Token = "test-token-1234567890";

    /// <summary>
    /// Which client each request was taken for, in arrival order — how a test sees
    /// the identity the transport worked out.
    /// </summary>
    private readonly ConcurrentQueue<string> _clientIdsQueue = new();

    private readonly MainThreadWorkQueue _queue = new();
    private readonly McpHttpHost _host;
    private readonly CancellationTokenSource _pumpCancellation = new();
    private readonly HttpClient _client = new();

    public McpHttpHostTests()
    {
        ICommandDispatcher dispatcher = CommandFactory.CreateDispatcher(new CommandContext());

        IMcpServer server = McpFactory.CreateServer(clientId =>
        {
            _clientIdsQueue.Enqueue(clientId);

            return dispatcher;
        });

        _host = new McpHttpHost(server, _queue, FreePort(), Token);
        _host.Start();

        // Stand in for the editor's per-frame pump.
        _ = Task.Run(async () =>
        {
            while (!_pumpCancellation.IsCancellationRequested)
            {
                _queue.Pump();
                await Task.Delay(5).ConfigureAwait(false);
            }
        });
    }

    public void Dispose()
    {
        _pumpCancellation.Cancel();
        _host.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task AToolCallRoundTripsOverHttp()
    {
        HttpResponseMessage response = await PostAsync(
            Token,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"new","arguments":{}}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"isError\":false", body);
    }

    [Fact]
    public async Task ANotificationIsAcceptedWithNoBody()
    {
        HttpResponseMessage response = await PostAsync(
            Token,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMissingTokenIsRejected()
    {
        HttpResponseMessage response = await PostAsync(
            null,
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AWrongTokenIsRejected()
    {
        HttpResponseMessage response = await PostAsync(
            "not-the-token-aaaaaaa",
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AForeignOriginIsRejectedBeforeAnythingElse()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
                Encoding.UTF8,
                "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        message.Headers.Add("Origin", "https://evil.example");

        HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AGetIsRefusedSinceThereIsNoServerStream()
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// The handshake is answered with a session id to adopt, which is how a second
    /// agent connecting later becomes tellable from the first.
    /// </summary>
    [Fact]
    public async Task TheHandshakeOffersASessionIdToAdopt()
    {
        HttpResponseMessage response = await PostAsync(
            Token,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

        Assert.True(response.Headers.TryGetValues("Mcp-Session-Id", out var offered));
        Assert.NotEmpty(offered!.First());
    }

    /// <summary>
    /// And nothing else offers one. A client that adopts what it is offered would
    /// otherwise change identity between its first call and its second — stranding
    /// the grant the first one was given, which is exactly what was measured before
    /// this rule existed.
    /// </summary>
    [Fact]
    public async Task NothingButTheHandshakeOffersASessionId()
    {
        HttpResponseMessage response = await PostAsync(
            Token,
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.False(response.Headers.TryGetValues("Mcp-Session-Id", out _));

        // Without one it is the shared anonymous client, which is the old
        // single-agent behaviour rather than an identity per request.
        Assert.True(_clientIdsQueue.TryDequeue(out string? clientId));
        Assert.Equal("anonymous", clientId);
    }

    [Fact]
    public async Task TheSessionIdAClientCarriesIsWhoTheRequestIsTakenFor()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
                Encoding.UTF8,
                "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        message.Headers.Add("Mcp-Session-Id", "session-abc");

        await _client.SendAsync(message);

        Assert.True(_clientIdsQueue.TryDequeue(out string? clientId));
        Assert.Equal("session-abc", clientId);
    }

    private string Endpoint => $"http://127.0.0.1:{_host.Port}/mcp";

    private async Task<HttpResponseMessage> PostAsync(string? token, string body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (token != null)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _client.SendAsync(message);
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }
}
