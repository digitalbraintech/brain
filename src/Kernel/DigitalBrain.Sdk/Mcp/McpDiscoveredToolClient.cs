using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Sdk;

/// <summary>
/// Owns isolated identity sessions and native catalogs for an explicitly admitted MCP connection.
/// Only a known read rejected with HTTP 401 may be replayed, once, after credential refresh.
/// </summary>
public sealed class McpDiscoveredToolClient<TIdentity> : IAsyncDisposable where TIdentity : notnull
{
    private readonly string _name;
    private readonly McpSessionOptions _options;
    private readonly HashSet<string> _allowed;
    private readonly Func<string, bool>? _allowTool;
    private readonly McpConnectionTransport<TIdentity> _transport;
    private readonly McpToolPolicy _policy;
    private readonly Func<McpClient, TIdentity, CancellationToken, Task>? _configureSession;
    private readonly Dictionary<TIdentity, Session> _sessions = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _maintenance;
    private bool _disposed;

    public McpDiscoveredToolClient(
        McpStdioConnection connection,
        Func<McpClient, TIdentity, CancellationToken, Task>? configureSession = null)
        : this(connection, configureSession, null) { }

    internal McpDiscoveredToolClient(
        McpStdioConnection connection,
        Func<McpClient, TIdentity, CancellationToken, Task>? configureSession,
        Func<TIdentity, CancellationToken, Task<McpClient>>? connect)
        : this(connection?.Name ?? throw new ArgumentNullException(nameof(connection)), StdioOptions(connection), connection.AllowedToolNames,
            new McpStdioTransport<TIdentity>(connection with { Arguments = [.. connection.Arguments] }, connect),
            new McpToolPolicy(static _ => false), configureSession)
    { }

    /// <summary>
    /// Creates an authenticated HTTP connection using the same native catalog/session path as STDIO.
    /// Identity must include the verified specialist and principal. The connection value must include
    /// its revision; authorize must validate the current actor's access on every invocation/token fetch.
    /// </summary>
    public static McpDiscoveredToolClient<TIdentity> ForHttp<TConnection>(
        McpEndpoint endpoint,
        IMcpCredentials<TConnection> credentials,
        Func<TIdentity, OwnerId> owner,
        Action<TIdentity, TConnection> authorize,
        IReadOnlyCollection<string> allowedToolNames,
        McpToolPolicy policy,
        McpSessionOptions? options = null) where TConnection : notnull
    {
        ArgumentNullException.ThrowIfNull(allowedToolNames);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(policy);
        options ??= new McpSessionOptions();
        return new(endpoint.Name, options, allowedToolNames,
            new McpHttpTransport<TIdentity, TConnection>(endpoint, credentials, owner, authorize, options.Timeout ?? TimeSpan.FromSeconds(30)), policy);
    }

    /// <summary>Connects to an explicitly configured local MCP server without external credentials.</summary>
    public static McpDiscoveredToolClient<TIdentity> ForLocalHttp(McpEndpoint endpoint,
        Func<HttpClient> createHttpClient, Func<string, bool> allowTool, McpSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(createHttpClient);
        ArgumentNullException.ThrowIfNull(allowTool);
        if (!endpoint.Uri.IsLoopback)
        {
            throw new ArgumentException("An unauthenticated local MCP connection requires a loopback endpoint.", nameof(endpoint));
        }
        options ??= new();
        return new(endpoint.Name, options, [], new McpLocalHttpTransport<TIdentity>(endpoint, createHttpClient,
            options.Timeout ?? TimeSpan.FromSeconds(30)), new McpToolPolicy(static _ => false), allowTool: allowTool);
    }

    internal McpDiscoveredToolClient(string name, McpSessionOptions options,
        IReadOnlyCollection<string> allowedToolNames, McpConnectionTransport<TIdentity> transport,
        McpToolPolicy policy, Func<McpClient, TIdentity, CancellationToken, Task>? configureSession = null,
        Func<string, bool>? allowTool = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ResponseBudgetBytes ?? 1_048_576, 512);
        if ((options.Timeout ?? TimeSpan.FromSeconds(30)) <= TimeSpan.Zero || options.IdleTimeout <= TimeSpan.Zero || options.Lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("MCP operation, lifetime and idle timeouts must be positive.", nameof(options));
        }

        _name = name;
        _options = options;
        _allowed = new HashSet<string>(allowedToolNames, StringComparer.Ordinal);
        _allowTool = allowTool;
        _transport = transport;
        _policy = policy;
        _configureSession = configureSession;
        _maintenance = MaintainAsync();
    }

    private static McpSessionOptions StdioOptions(McpStdioConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Command);
        return new()
        {
            Capacity = connection.Capacity,
            ResponseBudgetBytes = connection.ResponseBudgetBytes,
            Timeout = connection.OperationTimeout,
            IdleTimeout = connection.IdleTimeout,
        };
    }

    public Task<IReadOnlyList<AIFunction>> GetToolsAsync(TIdentity identity, CancellationToken cancellationToken = default)
        => WithSessionAsync<IReadOnlyList<AIFunction>>(identity, (session, token) =>
        {
            var binding = session.Binding!;
            IReadOnlyList<AIFunction> tools = session.Tools!.Values.Where(tool => IsAllowed(tool.Name))
                .Select(tool => (AIFunction)new McpDiscoveredTool(_name, tool,
                    (arguments, invocationToken) => new ValueTask<object?>(InvokeAsync(identity, binding, tool.ProtocolTool, arguments, invocationToken))))
                .ToArray();
            return Task.FromResult(tools);
        }, cancellationToken);

    /// <summary>Disconnect this identity. Its next preparation or invocation obtains a fresh catalog.</summary>
    public async Task InvalidateAsync(TIdentity identity, CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (_sessions)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(identity, out session))
            {
                return;
            }
            session.Users++;
        }

        try
        {
            await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await session.ResetAsync().ConfigureAwait(false); }
            finally { session.Gate.Release(); }
        }
        finally { Release(session); }
    }

    /// <summary>Remove unused identity slots as well as disposing their processes.</summary>
    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        List<Session> expired = [];
        lock (_sessions)
        {
            var cutoff = DateTimeOffset.UtcNow - _options.IdleTimeout;
            foreach (var pair in _sessions.ToArray())
            {
                if (pair.Value.Users == 0 && pair.Value.LastUsed <= cutoff)
                {
                    _sessions.Remove(pair.Key);
                    expired.Add(pair.Value);
                }
            }
        }

        foreach (var session in expired)
        {
            // Once removed there are no callers, and cancellation must not leak its process.
            await session.ResetAsync().ConfigureAwait(false);
            session.Gate.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async ValueTask DisposeAsync()
    {
        Session[] sessions;
        lock (_sessions)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _maintenance.ConfigureAwait(false);
        foreach (var session in sessions)
        {
            await session.Gate.WaitAsync().ConfigureAwait(false);
            try { await session.ResetAsync().ConfigureAwait(false); }
            finally { session.Gate.Release(); }
            // Queued operations can still be unwinding cancellation; do not dispose their gate.
        }

        _shutdown.Dispose();
    }

    private async Task<object?> InvokeAsync(TIdentity identity, object binding, Tool snapshot, AIFunctionArguments arguments, CancellationToken cancellationToken)
        => await WithSessionAsync<object?>(identity, async (session, token) =>
        {
            if (!IsAllowed(snapshot.Name) || !session.Tools!.TryGetValue(snapshot.Name, out var tool))
            {
                throw new McpOperationException($"MCP connection '{_name}' no longer publishes permitted tool '{snapshot.Name}'. Refresh the tool catalog.", McpFailureKind.CatalogChanged);
            }

            if (!SameSchema(snapshot.InputSchema, tool.ProtocolTool.InputSchema) ||
                !SameSchema(snapshot.OutputSchema, tool.ProtocolTool.OutputSchema))
            {
                throw new McpOperationException($"MCP tool '{snapshot.Name}' changed its schema. Refresh the tool catalog before calling it.", McpFailureKind.CatalogChanged);
            }

            var result = await tool.CallAsync(new Dictionary<string, object?>(arguments), cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return SerializeBounded(result);
        }, cancellationToken, binding, snapshot.Name).ConfigureAwait(false);

    private async Task<TResult> WithSessionAsync<TResult>(
        TIdentity identity, Func<Session, CancellationToken, Task<TResult>> action, CancellationToken cancellationToken,
        object? expectedBinding = null, string? toolName = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        deadline.CancelAfter(_options.Timeout ?? TimeSpan.FromSeconds(30));
        var session = Reserve(identity);
        try
        {
            await session.Gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                var budget = new BearerTokenHandler.ResponseBudget(_options.ResponseBudgetBytes ?? 1_048_576);
                for (var attempt = 0; ; attempt++)
                {
                    var binding = _transport.Binding(identity);
                    RequireBinding(expectedBinding, binding);
                    if (!Equals(session.Binding, binding) || session.ExpiresAt <= DateTimeOffset.UtcNow)
                    {
                        await session.ResetAsync().ConfigureAwait(false);
                    }
                    try
                    {
                        await PrepareAsync(session, identity, binding, budget, deadline.Token).ConfigureAwait(false);
                        var result = await action(session, deadline.Token).ConfigureAwait(false);
                        RequireBinding(binding, _transport.Binding(identity));
                        return result;
                    }
                    catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized && _transport.CanRefresh)
                    {
                        await session.ResetAsync().ConfigureAwait(false);
                        if (attempt != 0 || (toolName is not null && !_policy.IsReadOnly(toolName)))
                        {
                            await _transport.RejectAsync(identity, binding, deadline.Token).ConfigureAwait(false);
                            throw new McpAuthenticationRequiredException();
                        }
                        await _transport.RefreshAsync(identity, binding, deadline.Token).ConfigureAwait(false);
                        RequireBinding(binding, _transport.Binding(identity));
                    }
                }
            }
            catch (Exception exception)
            {
                await session.ResetAsync().ConfigureAwait(false);
                if (exception is OperationCanceledException or McpOperationException or McpAuthenticationRequiredException)
                {
                    throw;
                }
                // Protocol messages and child-process errors may contain paths, arguments or credentials.
                throw new McpOperationException($"MCP connection '{_name}' failed ({exception.GetType().Name}). The next request will reconnect; this operation was not replayed.");
            }
            finally { session.Gate.Release(); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
            throw new TimeoutException($"MCP connection '{_name}' exceeded its operation deadline. Cancellation was requested, but a remote outcome is not confirmed. The operation was not replayed.");
        }
        finally { Release(session); }
    }

    private Session Reserve(TIdentity identity)
    {
        lock (_sessions)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(identity, out var session))
            {
                if (_sessions.Count >= _options.Capacity)
                {
                    throw new McpOperationException($"MCP connection '{_name}' has no free identity sessions. Retry after idle sessions expire.", McpFailureKind.Capacity);
                }

                _sessions.Add(identity, session = new Session());
            }

            session.Users++;
            return session;
        }
    }

    private void Release(Session session)
    {
        lock (_sessions)
        {
            session.Users--;
            session.LastUsed = DateTimeOffset.UtcNow;
        }
    }

    private async Task PrepareAsync(Session session, TIdentity identity, object binding,
        BearerTokenHandler.ResponseBudget budget, CancellationToken cancellationToken)
    {
        if (session.Client is null)
        {
            session.Transport = await _transport.ConnectAsync(identity, binding, budget, cancellationToken).ConfigureAwait(false);
            session.Binding = binding;
            session.ExpiresAt = _options.Lifetime is { } lifetime ? DateTimeOffset.UtcNow.Add(lifetime) : DateTimeOffset.MaxValue;
            session.Notification = session.Client!.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
                (_, _) => { Interlocked.Increment(ref session.CatalogVersion); return ValueTask.CompletedTask; });
            if (_configureSession is not null)
            {
                await _configureSession(session.Client, identity, cancellationToken).ConfigureAwait(false);
            }
        }

        session.Transport!.BeginOperation(budget);

        var version = Volatile.Read(ref session.CatalogVersion);
        if (session.Tools is null || session.LoadedVersion != version)
        {
            var tools = await session.Client!.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            _policy.ValidateCatalog?.Invoke(tools);
            session.Tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
            session.LoadedVersion = version;
        }
    }

    private bool IsAllowed(string tool) => _allowTool?.Invoke(tool) ?? _allowed.Contains(tool);

    private static void RequireBinding(object? expected, object actual)
    {
        if (expected is not null && !Equals(expected, actual))
        {
            throw new McpOperationException("The MCP connection changed. Prepare fresh tools before continuing.", McpFailureKind.ConnectionChanged);
        }
    }

    private JsonElement SerializeBounded(CallToolResult result)
    {
        // Bound model/inspector evidence, not the server's business schema. Never return partial JSON
        // as if it were the full structured result, and retain the provider's isError value.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions);
        if (bytes.Length <= (_options.ResponseBudgetBytes ?? 1_048_576))
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }

        var bounded = new CallToolResult
        {
            IsError = result.IsError,
            Content = [new TextContentBlock { Text = "MCP response content was omitted because it exceeded the configured response budget. Request a narrower result; no complete evidence is available from this response." }],
            Meta = new JsonObject
            {
                ["digitalbrain"] = new JsonObject { ["truncated"] = true, ["responseBytes"] = bytes.Length },
            },
        };
        return JsonSerializer.SerializeToElement(bounded, McpJsonUtilities.DefaultOptions);
    }

    private async Task MaintainAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Clamp(_options.IdleTimeout.TotalMilliseconds, 100, 60_000)));
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await PruneAsync(_shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private static bool SameSchema(JsonElement? left, JsonElement? right)
        => left is null ? right is null : right is not null && JsonElement.DeepEquals(left.Value, right.Value);

    private sealed class Session
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal McpTransportLease? Transport;
        internal McpClient? Client => Transport?.Client;
        internal object? Binding;
        internal DateTimeOffset ExpiresAt;
        internal IAsyncDisposable? Notification;
        internal Dictionary<string, McpClientTool>? Tools;
        internal long CatalogVersion;
        internal long LoadedVersion;
        internal int Users;
        internal DateTimeOffset LastUsed = DateTimeOffset.UtcNow;

        internal async ValueTask ResetAsync()
        {
            Tools = null;
            Binding = null;
            if (Notification is not null)
            {
                try { await Notification.DisposeAsync().ConfigureAwait(false); }
                catch (Exception) { /* A failed notification registration must not leak its transport. */ }
                Notification = null;
            }

            var transport = Transport;
            Transport = null;
            if (transport is not null)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
