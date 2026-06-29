using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.TestSupport;

// Minimal IHostEnvironment for tests that construct services taking IHostEnvironment directly (the framework's
// concrete HostingEnvironment types are internal). EnvironmentName drives IsDevelopment()/IsProduction() checks.
public sealed class FakeHostEnvironment(string environmentName = "Development") : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "DigitalBrain.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
