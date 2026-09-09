using DigitalBrain.Abstractions.Identity;
using ModelContextProtocol.Client;

namespace DigitalBrain.Sdk;

// Transport differences live here. Catalogs, leases, retries and result handling belong to the
// discovered client, so HTTP and STDIO cannot acquire separate execution semantics.
internal abstract class McpConnectionTransport<TIdentity> where TIdentity : notnull
{
    internal abstract object Binding(TIdentity identity);
    internal abstract Task<McpTransportLease> ConnectAsync(TIdentity identity, object binding,
        BearerTokenHandler.ResponseBudget budget, CancellationToken cancellationToken);
    internal virtual bool CanRefresh => false;
    internal virtual Task RefreshAsync(TIdentity identity, object binding, CancellationToken cancellationToken)
        => Task.CompletedTask;
    internal virtual Task RejectAsync(TIdentity identity, object binding, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class McpStdioTransport<TIdentity>(McpStdioConnection connection,
    Func<TIdentity, CancellationToken, Task<McpClient>>? connect = null) : McpConnectionTransport<TIdentity>
    where TIdentity : notnull
{
    private readonly object _binding = new();

    internal override object Binding(TIdentity identity) => _binding;

    internal override async Task<McpTransportLease> ConnectAsync(TIdentity identity, object binding,
        BearerTokenHandler.ResponseBudget budget, CancellationToken cancellationToken)
    {
        if (connect is not null)
        {
            return new(await connect(identity, cancellationToken).ConfigureAwait(false));
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = connection.Name,
            Command = connection.Command,
            Arguments = [.. connection.Arguments],
            WorkingDirectory = connection.WorkingDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(2),
        });
        return new(await McpClient.CreateAsync(transport,
            new McpClientOptions { InitializationTimeout = connection.OperationTimeout },
            cancellationToken: cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class McpHttpTransport<TIdentity, TConnection>(McpEndpoint endpoint,
    IMcpCredentials<TConnection> credentials, Func<TIdentity, OwnerId> owner,
    Action<TIdentity, TConnection> authorize, TimeSpan timeout) : McpConnectionTransport<TIdentity>
    where TIdentity : notnull where TConnection : notnull
{
    internal override bool CanRefresh => true;

    internal override object Binding(TIdentity identity)
    {
        var binding = credentials.Connection(owner(identity));
        authorize(identity, binding);
        return binding;
    }

    internal override async Task<McpTransportLease> ConnectAsync(TIdentity identity, object binding,
        BearerTokenHandler.ResponseBudget budget, CancellationToken cancellationToken)
    {
        var handler = new BearerTokenHandler(endpoint, token => TokenAsync(identity, binding, false, token));
        handler.BeginOperation(budget);
        var http = new HttpClient(handler) { Timeout = timeout };
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.Uri,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                MaxReconnectionAttempts = 0,
            }, http);
            var client = await McpClient.CreateAsync(transport,
                new McpClientOptions { InitializationTimeout = timeout }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(client, http, handler);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    internal override async Task RefreshAsync(TIdentity identity, object binding, CancellationToken cancellationToken)
        => _ = await TokenAsync(identity, binding, true, cancellationToken).ConfigureAwait(false);

    internal override Task RejectAsync(TIdentity identity, object binding, CancellationToken cancellationToken)
    {
        RequireCurrent(identity, binding);
        return credentials.RejectAsync(owner(identity), (TConnection)binding, cancellationToken);
    }

    private async Task<string> TokenAsync(TIdentity identity, object binding, bool refresh, CancellationToken cancellationToken)
    {
        RequireCurrent(identity, binding);
        var token = await credentials.AccessTokenAsync(owner(identity), (TConnection)binding, refresh, cancellationToken).ConfigureAwait(false);
        RequireCurrent(identity, binding);
        return token;
    }

    private void RequireCurrent(TIdentity identity, object binding)
    {
        if (!Equals(Binding(identity), binding))
        {
            throw new McpOperationException("The MCP connection changed. Prepare fresh tools before continuing.", McpFailureKind.ConnectionChanged);
        }
    }
}

internal sealed class McpLocalHttpTransport<TIdentity>(McpEndpoint endpoint, Func<HttpClient> createHttpClient,
    TimeSpan timeout) : McpConnectionTransport<TIdentity> where TIdentity : notnull
{
    internal override object Binding(TIdentity identity) => endpoint;

    internal override async Task<McpTransportLease> ConnectAsync(TIdentity identity, object binding,
        BearerTokenHandler.ResponseBudget budget, CancellationToken cancellationToken)
    {
        var http = createHttpClient();
        http.Timeout = timeout;
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.Uri,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                MaxReconnectionAttempts = 0,
            }, http);
            var client = await McpClient.CreateAsync(transport,
                new McpClientOptions { InitializationTimeout = timeout }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(client, http);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }
}

internal sealed class McpTransportLease(McpClient client, HttpClient? http = null, BearerTokenHandler? handler = null) : IAsyncDisposable
{
    internal McpClient Client { get; } = client;
    internal void BeginOperation(BearerTokenHandler.ResponseBudget budget) => handler?.BeginOperation(budget);

    public async ValueTask DisposeAsync()
    {
        // Do not fetch/refresh credentials merely to DELETE an expiring HTTP session.
        http?.Dispose();
        try { await Client.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* Failed transport cleanup must not replace the operation failure. */ }
    }
}
