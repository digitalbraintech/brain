using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Sdk.Webhooks;

/// <summary>A configured external event source. Secrets belong to hosting, never to behavior source.</summary>
public sealed class ConfiguredWebhookSource
{
    public ConfiguredWebhookSource(OwnerId owner, PrincipalId principal, string name, string path, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (secret.Length < 32)
        {
            throw new ArgumentException("Use a webhook signing secret of at least 32 characters.", nameof(secret));
        }
        Id = NeuronId.For<IWebhook>(owner, PrincipalPartition.InstanceName(principal, name));
        Actor = new(principal, "webhook-ingress");
        Definition = new(path);
        Secret = secret;
        Epoch = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}:{path}:{secret}")));
    }
    public NeuronId Id { get; }
    public WebhookDefinition Definition { get; }
    internal ActorContext Actor { get; }
    internal string Secret { get; }
    internal string Epoch { get; }
}

public static class ConfiguredWebhookHostingExtensions
{
    public static IServiceCollection AddWebhookSource(this IServiceCollection services, ConfiguredWebhookSource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);
        services.AddWebhookNeurons();
        services.AddSingleton(source);
        services.AddSingleton<IHttpSurface>(provider => new WebhookSurface(source.Definition,
            new ConfiguredWebhookHandler(source, provider.GetRequiredService<IGrainFactory>())));
        return services;
    }
}

[GrainType("webhook")]
internal sealed class ConfiguredWebhookNeuron(NeuronRuntime runtime) : WebhookNeuron(runtime)
{
    private ConfiguredWebhookSource? Source => ServiceProvider.GetServices<ConfiguredWebhookSource>().SingleOrDefault(source => source.Id == Id);
    protected override ActorContext SourceActor => Source?.Actor
        ?? (PrincipalPartition.TryParse(Id.Name, out var principal, out _)
            ? new(principal, "webhook-setup") : throw new NeuronAuthorizationException("Use an authenticated, principal-scoped webhook name."));
    protected override string SourceEpoch => Source?.Epoch ?? "unconfigured";
    protected override bool SourceAvailable => Source is not null;
    protected override string? SourceDetail => Source is null ? "Configure this webhook's authenticated HTTP endpoint in the host before subscribing." : null;
}

internal sealed class ConfiguredWebhookHandler(ConfiguredWebhookSource source, IGrainFactory grains) : IWebhookHandler
{
    public async Task<WebhookAcceptance> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var signature = Header(request, "X-DigitalBrain-Signature-256");
        var eventId = Header(request, "X-DigitalBrain-Delivery");
        if (signature is null || !signature.StartsWith("sha256=", StringComparison.Ordinal) || signature.Length != 71)
        {
            return WebhookAcceptance.Unauthorized;
        }
        byte[] claimed;
        try { claimed = Convert.FromHexString(signature[7..]); }
        catch (FormatException) { return WebhookAcceptance.Unauthorized; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(source.Secret), request.Body.Span);
        if (!CryptographicOperations.FixedTimeEquals(expected, claimed))
        {
            return WebhookAcceptance.Unauthorized;
        }
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 200)
        {
            return WebhookAcceptance.BadRequest;
        }
        try
        {
            using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions { MaxDepth = 32 });
            var now = DateTimeOffset.UtcNow;
            var signal = new WebhookReceived(eventId, new WebhookPayload(document.RootElement.GetRawText()), now);
            var receipt = new WebhookReceipt(eventId, Convert.ToHexStringLower(SHA256.HashData(request.Body.Span)), source.Epoch, signal, now);
            using var actor = VerifiedActor.Enter(source.Actor);
            return await grains.GetGrain<IAuthenticatedWebhookIngress>(source.Id.ToGrainId()).AcceptAsync(receipt).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) { return WebhookAcceptance.BadRequest; }
    }

    private static string? Header(WebhookRequest request, string name)
        => request.Headers.TryGetValue(name, out var values) && values.Length == 1 ? values[0] : null;
}
