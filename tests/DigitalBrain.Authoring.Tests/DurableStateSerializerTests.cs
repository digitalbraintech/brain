using DigitalBrain.Aspire;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Storage;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class DurableStateSerializerTests
{
    [Fact]
    public void Production_grain_storage_serializer_round_trips_workspace_state()
    {
        var builder = Host.CreateApplicationBuilder();
        var storage = "UseDevelopmentStorage=true";
        builder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Clustering}"] = storage;
        builder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Reminders}"] = storage;
        builder.Configuration[$"ConnectionStrings:{DigitalBrainNames.GrainState}"] = storage;
        builder.Configuration[$"ConnectionStrings:{DigitalBrainNames.JournalConnection}"] = storage;
        builder.Configuration[$"Orleans:GrainStorage:{DigitalBrainNames.DefaultGrainStorage}:ProviderType"] = "AzureBlobStorage";
        builder.Configuration[$"Orleans:GrainStorage:{DigitalBrainNames.DefaultGrainStorage}:ServiceKey"] = DigitalBrainNames.GrainState;
        builder.Configuration["Orleans:Clustering:ProviderType"] = "AzureTableStorage";
        builder.Configuration["Orleans:Clustering:ServiceKey"] = DigitalBrainNames.Clustering;
        builder.Configuration["Orleans:Reminders:ProviderType"] = "AzureTableStorage";
        builder.Configuration["Orleans:Reminders:ServiceKey"] = DigitalBrainNames.Reminders;
        builder.AddDigitalBrain(new ModuleManifest([typeof(UIModule)]));
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptionsMonitor<AzureBlobStorageOptions>>()
            .Get(DigitalBrainNames.DefaultGrainStorage);
        Assert.Equal("digitalbrain-v2-state", options.ContainerName);
        Assert.Equal("digitalbrain-v2-journal", host.Services
            .GetRequiredService<IOptions<Orleans.Journaling.AzureBlobJournalStorageOptions>>().Value.ContainerName);
        var serializer = options.GrainStorageSerializer;
        Assert.IsType<OrleansGrainStorageSerializer>(serializer);
        var expected = new WorkspaceIndexState(
        [
            new WorkspaceRecord("main", Guid.NewGuid().ToString("n"), "Main", DateTimeOffset.UtcNow),
        ]);

        var stored = serializer.Serialize(expected);
        var recovered = serializer.Deserialize<WorkspaceIndexState>(stored);

        var workspace = Assert.Single(recovered.Workspaces);
        Assert.Equal("main", workspace.Name);
        Assert.Equal("Main", workspace.Title);
    }
}
