using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

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
            return executing is BorrowedDigitalBrain ? executing : new BorrowedDigitalBrain(executing);
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
            return brain;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

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
