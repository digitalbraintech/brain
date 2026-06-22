using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Journaling;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs

// Centralized (single source) prototype in-memory journal support to avoid duplication across entry points.
internal sealed class InMemoryJournalForPrototype<T> : List<T>, IDurableList<T>;

internal sealed class PrototypeJournaledStateManager : IJournaledStateManager
{
    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public void RegisterState(string stateId, IJournaledState state) { }
    public bool TryGetState(string stateId, out IJournaledState? state) { state = null; return false; }
    public ValueTask WriteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DeleteStateAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}

// Centralized config helper to eliminate dupe journal registration in prod hosting (Program + Extensions).
internal static class DigitalBrainJournalConfig
{
    public static void ConfigurePrototypeJournals(this ISiloBuilder siloBuilder)
    {
        siloBuilder.ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<DigitalBrain.Protocol.Synapse>>("in-journal",
                (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
            services.AddKeyedScoped<IDurableList<DigitalBrain.Protocol.Synapse>>("out-journal",
                (_, _) => new InMemoryJournalForPrototype<DigitalBrain.Protocol.Synapse>());
            services.AddSingleton<IJournaledStateManager, PrototypeJournaledStateManager>();
        });
    }
}
