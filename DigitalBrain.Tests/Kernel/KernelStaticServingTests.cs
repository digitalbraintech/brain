using System.IO;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class KernelStaticServingTests
{
    [Fact]
    public async Task Serves_Index_Html_From_Configured_WebRoot()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "dbtest-webroot-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>db-e2e-marker</title>");
        try
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b => b.UseSetting("DIGITALBRAIN_WEBROOT", webRoot));
            using var client = factory.CreateClient();

            var root = await client.GetStringAsync("/");
            Assert.Contains("db-e2e-marker", root);

            // SPA fallback: an unknown non-API path returns index.html, not 404.
            var deep = await client.GetStringAsync("/canvas/anything");
            Assert.Contains("db-e2e-marker", deep);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Without_WebRoot_Root_Is_Not_Served_As_Index()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var resp = await client.GetAsync("/");
        Assert.NotEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }
}
