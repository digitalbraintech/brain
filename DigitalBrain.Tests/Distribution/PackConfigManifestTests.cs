using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Distribution;

public class PackConfigManifestTests
{
    private sealed class MinimalPack : IPackBehavior
    {
        public string Respond(string input) => input;
    }

    private sealed class ConfiguredPack : IPackBehavior
    {
        public string Respond(string input) => input;

        public PackManifest GetManifest() => new(
            new[] { new SynapseType(nameof(ExperienceUsed)) },
            new[]
            {
                new PackConfigField("api_key", "API Key", PackConfigFieldKind.Secret),
                new PackConfigField("deployment_mode", "Deployment Mode", PackConfigFieldKind.Choice,
                    Choices: new[] { "cloud", "local" }),
                new PackConfigField("endpoint_url", "Endpoint URL", PackConfigFieldKind.Text,
                    DependsOnKey: "deployment_mode", DependsOnValue: "cloud")
            });
    }

    [Fact]
    public void Default_manifest_has_null_required_config_and_handles_ExperienceUsed()
    {
        IPackBehavior pack = new MinimalPack();
        var manifest = pack.GetManifest();

        Assert.Null(manifest.RequiredConfig);
        Assert.Single(manifest.HandledSynapseTypes);
        Assert.Equal(nameof(ExperienceUsed), manifest.HandledSynapseTypes[0].Value);
    }

    [Fact]
    public void Pack_returning_RequiredConfig_exposes_all_fields()
    {
        var pack = new ConfiguredPack();
        var manifest = pack.GetManifest();

        Assert.NotNull(manifest.RequiredConfig);
        Assert.Equal(3, manifest.RequiredConfig!.Count);

        var apiKey = manifest.RequiredConfig[0];
        Assert.Equal("api_key", apiKey.Key);
        Assert.Equal("API Key", apiKey.Label);
        Assert.Equal(PackConfigFieldKind.Secret, apiKey.Kind);
        Assert.Null(apiKey.Choices);
        Assert.Null(apiKey.DependsOnKey);

        var deploymentMode = manifest.RequiredConfig[1];
        Assert.Equal("deployment_mode", deploymentMode.Key);
        Assert.Equal(PackConfigFieldKind.Choice, deploymentMode.Kind);
        Assert.Equal(new[] { "cloud", "local" }, deploymentMode.Choices);

        var endpointUrl = manifest.RequiredConfig[2];
        Assert.Equal("endpoint_url", endpointUrl.Key);
        Assert.Equal(PackConfigFieldKind.Text, endpointUrl.Kind);
        Assert.Equal("deployment_mode", endpointUrl.DependsOnKey);
        Assert.Equal("cloud", endpointUrl.DependsOnValue);
    }

    [Fact]
    public void ConfigurationProvided_carries_pack_name_and_values()
    {
        var values = new Dictionary<string, string>
        {
            ["api_key"] = "secret123",
            ["deployment_mode"] = "cloud"
        };

        var synapse = new ConfigurationProvided("GenericPack", values);

        Assert.Equal("GenericPack", synapse.PackName);
        Assert.Equal("secret123", synapse.Values["api_key"]);
        Assert.Equal("cloud", synapse.Values["deployment_mode"]);
        Assert.Equal(nameof(ConfigurationProvided), synapse.Type);
    }

    [Fact]
    public void Existing_PackManifest_ctor_without_RequiredConfig_still_compiles_and_RequiredConfig_is_null()
    {
        var manifest = new PackManifest(new[] { new SynapseType("CustomEvent") });

        Assert.Single(manifest.HandledSynapseTypes);
        Assert.Null(manifest.RequiredConfig);
    }
}
