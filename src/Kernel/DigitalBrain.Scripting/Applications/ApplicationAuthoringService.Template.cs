using System.Text.Json;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Applications;

public sealed partial class ApplicationAuthoringService
{
    public Task<string> TemplateAsync(IDigitalBrain brain, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = LocationFor(brain, key);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? project = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Kernel", "DigitalBrain.Sdk", "DigitalBrain.Sdk.csproj");
            if (File.Exists(candidate)) { project = candidate.Replace('\\', '/'); break; }
            directory = directory.Parent;
        }
        if (project is null)
        {
            throw new InvalidOperationException("The development SDK project is unavailable on this host; a source template cannot be generated.");
        }
        return Task.FromResult($$"""
            #:project {{project}}
            #:property TargetFramework=net11.0
            #:property PublishAot=false

            using DigitalBrain.Abstractions;

            await using var brain = await DigitalBrainClient.ConnectAsync(args);
            var application = brain.Application({{JsonSerializer.Serialize(key)}});
            application.Command<string, string>("reply", (_, _, _) => Task.FromResult("pong"));
            await application.RunAsync(args);
            """);
    }
}
