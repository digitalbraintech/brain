using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Microsoft.Hosting;

public static class MicrosoftHostingExtensions
{
    public static DigitalBrainModuleBuilder<MicrosoftModule> WithAspire(
        this DigitalBrainModuleBuilder<MicrosoftModule> module,
        string appHostProject,
        string owner,
        string applicationName = "DigitalBrain",
        string alias = "digitalbrain-local")
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(appHostProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        module.AddProjection(new AspireProjection(module.Brain, Path.GetFullPath(appHostProject), owner, applicationName, alias));
        return module;
    }

    private sealed class AspireProjection(DigitalBrainBuilder brain, string project, string owner, string applicationName, string alias)
        : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (brain.FakesEnabled)
            {
                return;
            }

            const string root = MicrosoftModule.AspireConfigurationRoot;
            builder
                .WithEnvironment(EnvironmentKeys.For(root, "ProjectPath"), project)
                .WithEnvironment(EnvironmentKeys.For(root, "Owner"), owner)
                .WithEnvironment(EnvironmentKeys.For(root, "ApplicationName"), applicationName)
                .WithEnvironment(EnvironmentKeys.For(root, "Alias"), alias);
        }
    }
}
