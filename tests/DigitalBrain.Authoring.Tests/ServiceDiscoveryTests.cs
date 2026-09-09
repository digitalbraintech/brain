using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Authoring.Tests;

public sealed class ServiceDiscoveryTests
{
    [Fact]
    public async Task Authoring_client_resolves_the_Aspire_kernel_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var serverBuilder = WebApplication.CreateBuilder();
        await using var server = serverBuilder.Build();
        server.Urls.Add("http://127.0.0.1:0");
        server.MapGet("/applications", () => "[]");
        await server.StartAsync(ct);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["services:kernel:http:0"] = server.Urls.Single(),
        });
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient("authoring", client => client.BaseAddress = new Uri("https+http://kernel"));
        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("authoring");

        Assert.Equal("[]", await client.GetStringAsync("/applications", ct));
    }
}
