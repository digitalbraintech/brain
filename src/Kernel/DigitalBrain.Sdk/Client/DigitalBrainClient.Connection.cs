using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Abstractions.Scripting;
using Orleans.Configuration;
using Orleans.Hosting;

namespace DigitalBrain.Abstractions;

public sealed partial class DigitalBrainClient
{
    private static readonly AsyncLocal<IDigitalBrain?> Execution = new();
    private IHost? _host;
    private IDisposable? _workerScope;

    /// <summary>Connects a console program, or borrows the current saved behavior's connection.</summary>
    public static async Task<IDigitalBrain> ConnectAsync(
        string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();
        if (Execution.Value is { } executing)
        {
            return executing is BorrowedDigitalBrain ? executing : new BorrowedDigitalBrain(executing);
        }

        if (TryWorkerArguments(args, out var endpoint, out var ticket))
        {
            return await ConnectWorkerAsync(endpoint, ticket, cancellationToken).ConfigureAwait(false);
        }

        var builder = Host.CreateApplicationBuilder(args);
        var principalSetting = builder.Configuration["DigitalBrain:Principal"];
        ActorContext? connectionActor = null;
        if (!string.IsNullOrWhiteSpace(principalSetting))
        {
            if (!Guid.TryParse(principalSetting, out var principal) || principal == Guid.Empty)
            {
                throw new InvalidOperationException("DigitalBrain:Principal must be the principal GUID of the connected user.");
            }
            connectionActor = new(new PrincipalId(principal), "sdk-program");
        }
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(DigitalBrainNames.Clustering)))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:clustering to connect to DigitalBrain.");
        }
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.UseOrleansClient(client => ModelPayloadSerialization.AddModelPayloadSerialization(client.Services));
        var host = builder.Build();
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            var owner = builder.Configuration[DigitalBrainNames.Owner] ?? DigitalBrainNames.DefaultOwner;
            var brain = new DigitalBrainClient(new DigitalBrainClientTransport(
                host.Services.GetRequiredService<IGrainFactory>(), new OwnerId(owner), connectionActor))
            { _host = host };
            return brain;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static bool TryWorkerArguments(string[] args, out Uri endpoint, out string ticket)
    {
        endpoint = null!;
        ticket = string.Empty;
        var endpointIndex = Array.IndexOf(args, "--digitalbrain-worker-endpoint");
        var ticketIndex = Array.IndexOf(args, "--digitalbrain-worker-ticket");
        if (endpointIndex < 0 && ticketIndex < 0)
        {
            return false;
        }
        if (endpointIndex < 0 || ticketIndex < 0 || endpointIndex + 1 >= args.Length || ticketIndex + 1 >= args.Length ||
            !Uri.TryCreate(args[endpointIndex + 1], UriKind.Absolute, out var parsedEndpoint) ||
            parsedEndpoint.Scheme != Uri.UriSchemeHttp || !IPAddress.TryParse(parsedEndpoint.Host, out var address) ||
            !IPAddress.IsLoopback(address) || string.IsNullOrWhiteSpace(args[ticketIndex + 1]))
        {
            throw new InvalidOperationException("Application worker bootstrap arguments are invalid.");
        }
        endpoint = parsedEndpoint;
        ticket = args[ticketIndex + 1];
        return true;
    }

    private static async Task<IDigitalBrain> ConnectWorkerAsync(
        Uri endpoint, string ticket, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = endpoint };
        using var response = await http.PostAsync("redeem", new StringContent(ticket), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bootstrap = await response.Content.ReadFromJsonAsync<WorkerBootstrap>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Worker bootstrap returned no context.");
        if (!IPAddress.TryParse(bootstrap.GatewayAddress, out var gateway) || bootstrap.GatewayPort is <= 0 or > 65535 ||
            bootstrap.Principal == Guid.Empty || string.IsNullOrWhiteSpace(bootstrap.Owner) ||
            string.IsNullOrWhiteSpace(bootstrap.Username) || string.IsNullOrWhiteSpace(bootstrap.ApplicationKey) ||
            string.IsNullOrWhiteSpace(bootstrap.RevisionId) || string.IsNullOrWhiteSpace(bootstrap.WorkerCapability))
        {
            throw new InvalidOperationException("Worker bootstrap context is invalid.");
        }

        var builder = Host.CreateApplicationBuilder();
        // A worker connects using its redeemed bootstrap, not inherited silo providers.
        builder.Configuration.Sources.Clear();
        builder.UseOrleansClient(client =>
        {
            client.UseStaticClustering(new IPEndPoint(gateway, bootstrap.GatewayPort));
            client.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = bootstrap.ClusterId;
                options.ServiceId = bootstrap.ServiceId;
            });
            ModelPayloadSerialization.AddModelPayloadSerialization(client.Services);
        });
        var host = builder.Build();
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            var actor = new ActorContext(new PrincipalId(bootstrap.Principal), bootstrap.Username);
            var brain = new DigitalBrainClient(new DigitalBrainClientTransport(
                host.Services.GetRequiredService<IGrainFactory>(), new OwnerId(bootstrap.Owner), actor))
            {
                _host = host,
                _workerScope = ApplicationRunScope.EnterProcess(
                    bootstrap.RevisionId, bootstrap.ApplicationKey, bootstrap.WorkerCapability,
                    bootstrap.ChildRevisions, cancellationToken),
            };
            return brain;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private sealed record WorkerBootstrap(string GatewayAddress, int GatewayPort, string ClusterId,
        string ServiceId, string Owner, Guid Principal, string Username, string ApplicationKey, string RevisionId,
        string WorkerCapability, IReadOnlyDictionary<string, string>? ChildRevisions);

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static IDisposable BindExecution(IDigitalBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var previous = Execution.Value;
        Execution.Value = brain is BorrowedDigitalBrain ? brain : new BorrowedDigitalBrain(brain);
        return new ExecutionScope(previous);
    }

    private sealed class ExecutionScope(IDigitalBrain? previous) : IDisposable
    {
        public void Dispose() => Execution.Value = previous;
    }
}
