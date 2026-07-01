using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Tests.Foundry;

public class AzureResourceControllerTests
{
    [Fact]
    public async Task RestartSilo_DoesNotThrow_AndRecordsIntent()
    {
        var c = new AzureResourceController(NullLogger<AzureResourceController>.Instance, dryRun: true);
        await c.RestartSiloAsync("test");
        Assert.True(c.LastReason == "test");
    }
}
