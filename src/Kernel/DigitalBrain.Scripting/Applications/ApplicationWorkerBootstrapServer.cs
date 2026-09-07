using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Scripting.Applications;

internal sealed class ApplicationWorkerBootstrapServer : IAsyncDisposable
{
    private readonly HttpListener listener;
    private readonly ExternalOrleansGateway gateway;
    private readonly ApplicationWorkerCapabilityAuthority capabilities;
    private readonly ConcurrentDictionary<string, Ticket> tickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> issuedCapabilities = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();
    private readonly Task loop;

    private ApplicationWorkerBootstrapServer(HttpListener listener, ExternalOrleansGateway gateway,
        ApplicationWorkerCapabilityAuthority capabilities)
    {
        this.listener = listener;
        this.gateway = gateway;
        this.capabilities = capabilities;
        loop = ListenAsync(stopping.Token);
    }

    internal Uri Endpoint { get; private init; } = null!;
    internal ApplicationWorkerCapabilityAuthority Capabilities => capabilities;

    internal static Task<ApplicationWorkerBootstrapServer> StartAsync(
        ExternalOrleansGateway gateway, ApplicationWorkerCapabilityAuthority? capabilities = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Start(gateway, capabilities));
    }

    internal static ApplicationWorkerBootstrapServer Start(ExternalOrleansGateway gateway,
        ApplicationWorkerCapabilityAuthority? capabilities = null)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var endpoint = new Uri($"http://127.0.0.1:{port}/");
        var listener = new HttpListener();
        listener.Prefixes.Add(endpoint.AbsoluteUri);
        listener.Start();
        return new ApplicationWorkerBootstrapServer(listener, gateway,
            capabilities ?? new ApplicationWorkerCapabilityAuthority()) { Endpoint = endpoint };
    }

    internal string Issue(FileApplicationArtifact artifact, OwnerId owner, ActorContext actor, string applicationKey)
    {
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var capability = capabilities.Issue(owner.Value, actor.PrincipalId.Value, applicationKey,
            artifact.ApplicationRevision, DateTimeOffset.MaxValue);
        tickets[nonce] = new(owner.Value, actor.PrincipalId.Value, actor.Username, applicationKey,
            artifact.ApplicationRevision, expiresAt, capability,
            artifact.Children?.ToDictionary(pair => pair.Key, pair => pair.Value.ApplicationRevision,
                StringComparer.Ordinal));
        issuedCapabilities[nonce] = capability;
        return nonce;
    }

    internal void Revoke(string nonce)
    {
        tickets.TryRemove(nonce, out _);
        if (issuedCapabilities.TryRemove(nonce, out var capability)) { capabilities.Revoke(capability); }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { return; }
            _ = RespondAsync(context, cancellationToken);
        }
    }

    private async Task RespondAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/redeem")
            {
                context.Response.StatusCode = 404;
                return;
            }
            using var reader = new StreamReader(context.Request.InputStream);
            var nonce = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (!tickets.TryRemove(nonce, out var ticket) || ticket.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                context.Response.StatusCode = 401;
                return;
            }
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.OutputStream, new WorkerBootstrap(
                gateway.Address, gateway.Port, gateway.ClusterId, gateway.ServiceId,
                ticket.Owner, ticket.Principal, ticket.Username, ticket.ApplicationKey, ticket.RevisionId,
                ticket.Capability, ticket.ChildRevisions),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var nonce in issuedCapabilities.Keys) { Revoke(nonce); }
        await stopping.CancelAsync().ConfigureAwait(false);
        listener.Close();
        try { await loop.ConfigureAwait(false); } catch (HttpListenerException) { }
        stopping.Dispose();
    }

    private sealed record Ticket(string Owner, Guid Principal, string Username, string ApplicationKey,
        string RevisionId, DateTimeOffset ExpiresAt, string Capability,
        IReadOnlyDictionary<string, string>? ChildRevisions = null);
    private sealed record WorkerBootstrap(string GatewayAddress, int GatewayPort, string ClusterId,
        string ServiceId, string Owner, Guid Principal, string Username, string ApplicationKey, string RevisionId,
        string WorkerCapability, IReadOnlyDictionary<string, string>? ChildRevisions);
}
