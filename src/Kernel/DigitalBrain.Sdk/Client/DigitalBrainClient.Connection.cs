using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Abstractions;

public sealed partial class DigitalBrainClient
{
    private static readonly AsyncLocal<IDigitalBrain?> Execution = new();
    private IHost? _host;

    /// <summary>Connects a console program, or borrows the current saved behavior's connection.</summary>
    public static async Task<IDigitalBrain> ConnectAsync(
        string[] args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();
        if (Execution.Value is { } executing)
        {
            return executing;
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
                host.Services.GetRequiredService<IGrainFactory>(), new OwnerId(owner), connectionActor)) { _host = host };
            await brain.ActivateAsync(cancellationToken).ConfigureAwait(false);
            return brain;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    // Infrastructure entry points. User programs always call ConnectAsync(args).
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static DigitalBrainClient ConnectExecution(
        IGrainFactory grains, NeuronId source, BehaviorClaim claim)
        => new(new DigitalBrainClientTransport(grains, source.Owner, source, claim));

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static IDisposable BindExecution(IDigitalBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var previous = Execution.Value;
        Execution.Value = brain;
        return new ExecutionScope(previous);
    }

    public TSignal Input<TSignal>() where TSignal : Signal
        => _transport.Input is TSignal input ? input
            : throw new InvalidOperationException($"The current behavior input is not '{typeof(TSignal).Name}'.");

    public NeuronId InputSource => _transport.InputSource
        ?? throw new InvalidOperationException("InputSource is available only while a saved behavior is executing.");

    private sealed class ExecutionScope(IDigitalBrain? previous) : IDisposable
    {
        public void Dispose() => Execution.Value = previous;
    }
}
