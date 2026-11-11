using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoxelCAD.Mcp;

/// <summary>
/// Serves one MCP endpoint over HTTP on the loopback interface, so an agent
/// running as a separate process — the user's desktop client, an IDE — can drive
/// the editor the user already has open.
/// </summary>
/// <remarks>
/// Requests arrive on listener threads and are marshalled onto the main thread
/// through <see cref="MainThreadWorkQueue"/>, because they touch the live model.
/// The host binds to 127.0.0.1 only, checks a bearer token, and rejects foreign
/// <c>Origin</c> headers — a localhost server is reachable by any local process
/// and, without an Origin check, by a web page via DNS rebinding.
/// <para>
/// It also tells its clients apart, which is what lets control of the editor belong
/// to <b>one</b> agent rather than to "an agent": a request with no
/// <c>Mcp-Session-Id</c> is answered with one to adopt, and whatever id a request
/// carries afterwards is that agent's identity for the whole session. The token is
/// shared, so this is coordination between cooperating clients and not a security
/// boundary — anyone holding the token already has the editor.
/// </para>
/// </remarks>
public sealed class McpHttpHost : IDisposable
{
    private const string EndpointPath = "/mcp";
    private const string SessionHeader = "Mcp-Session-Id";

    /// <summary>
    /// The identity a client that never sends a session id back is filed under.
    /// Sharing one is the honest degradation: two such clients are indistinguishable
    /// and behave as one agent — the pre-session behaviour — where minting them an
    /// identity per request would lock each of them out of the control it had just
    /// been granted.
    /// </summary>
    private const string AnonymousClientId = "anonymous";

    private readonly IMcpServer _server;
    private readonly MainThreadWorkQueue _workQueue;
    private readonly string _token;
    private readonly HttpListener _listener = new();

    private CancellationTokenSource? _cancellation;

    public McpHttpHost(IMcpServer server, MainThreadWorkQueue workQueue, int port, string token)
    {
        _server = server;
        _workQueue = workQueue;
        _token = token;
        Port = port;

        _listener.Prefixes.Add($"http://127.0.0.1:{port}{EndpointPath}/");
    }

    public int Port { get; }

    public string Token => _token;

    public bool IsRunning => _listener.IsListening;

    public void Start()
    {
        if (_listener.IsListening)
            return;

        _cancellation = new CancellationTokenSource();
        _listener.Start();

        _ = AcceptLoopAsync(_cancellation.Token);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();

        if (_listener.IsListening)
            _listener.Stop();

        _listener.Close();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            await RespondAsync(context).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A single failed request must not take the listener down; the client
            // sees the dropped connection and retries.
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;

        if (!IsOriginAllowed(request))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        if (!IsAuthorized(request))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        if (request.HttpMethod != "POST")
        {
            // GET would open a server-to-client SSE stream, which this host does
            // not offer; the spec allows answering that with 405.
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }

        string body;

        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            body = await reader.ReadToEndAsync().ConfigureAwait(false);

        string clientId = GetClientId(request, context.Response, body);

        // Two awaits, and the pair is the point. The first hands the message to the
        // main thread and comes back with whatever the command left running; the
        // second waits for that. A command that asked to be told when the editor is
        // free is waiting off the main thread by then, so the editor keeps running
        // and every other client keeps being served.
        Task<string?> pending = await _workQueue
            .Run(() => _server.HandleMessageAsync(body, clientId))
            .ConfigureAwait(false);

        string? response = await pending.ConfigureAwait(false);

        if (response == null)
        {
            // A notification: accepted, nothing to send back.
            context.Response.StatusCode = (int)HttpStatusCode.Accepted;
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes(response);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = payload.Length;

        await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Which agent this request belongs to. A client that carries a session id keeps
    /// its own identity; one that carries none is anonymous, and is offered an id to
    /// adopt on the handshake alone.
    /// </summary>
    /// <remarks>
    /// The handshake is the only place the offer belongs, and getting that wrong was
    /// measured rather than reasoned about: offering a fresh id on <i>every</i>
    /// request without one, a client that adopts what it is offered changes identity
    /// between its first call and its second — which stranded the very grant its
    /// first call had been given, leaving an agent unable to release control it held.
    /// Now the identity a session starts with is the identity it keeps: an adopted
    /// one throughout, or anonymous throughout.
    /// </remarks>
    private static string GetClientId(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string body)
    {
        var presented = request.Headers[SessionHeader];

        if (!string.IsNullOrWhiteSpace(presented))
            return presented.Trim();

        // Nothing stateful hangs off the handshake, so answering it as the anonymous
        // client costs nothing even for a client that does adopt the id.
        if (McpMessage.IsInitialize(body))
            response.Headers[SessionHeader] = Guid.NewGuid().ToString("N");

        return AnonymousClientId;
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];

        return header != null
            && header.StartsWith("Bearer ", StringComparison.Ordinal)
            && FixedTimeEquals(header["Bearer ".Length..], _token);
    }

    private static bool IsOriginAllowed(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];

        // No Origin is a non-browser client, which is the normal case. A present
        // Origin must be loopback — a real site's Origin never is.
        if (origin == null)
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]");
    }

    /// <summary>Length-independent, non-short-circuiting compare, so the token cannot be timed out.</summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
            return false;

        var difference = 0;

        for (var i = 0; i < left.Length; i++)
            difference |= left[i] ^ right[i];

        return difference == 0;
    }
}
