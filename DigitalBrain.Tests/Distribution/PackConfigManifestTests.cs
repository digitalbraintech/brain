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
                new PackConfigField("bot_token", "Telegram Bot Token", PackConfigFieldKind.Secret),
                new PackConfigField("mode", "Mode", PackConfigFieldKind.Choice,
                    Choices: new[] { "polling", "webhook" }),
                new PackConfigField("webhook_url", "Webhook URL", PackConfigFieldKind.Text,
                    DependsOnKey: "mode", DependsOnValue: "webhook")
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

        var token = manifest.RequiredConfig[0];
        Assert.Equal("bot_token", token.Key);
        Assert.Equal("Telegram Bot Token", token.Label);
        Assert.Equal(PackConfigFieldKind.Secret, token.Kind);
        Assert.Null(token.Choices);
        Assert.Null(token.DependsOnKey);

        var mode = manifest.RequiredConfig[1];
        Assert.Equal("mode", mode.Key);
        Assert.Equal(PackConfigFieldKind.Choice, mode.Kind);
        Assert.Equal(new[] { "polling", "webhook" }, mode.Choices);

        var webhookUrl = manifest.RequiredConfig[2];
        Assert.Equal("webhook_url", webhookUrl.Key);
        Assert.Equal(PackConfigFieldKind.Text, webhookUrl.Kind);
        Assert.Equal("mode", webhookUrl.DependsOnKey);
        Assert.Equal("webhook", webhookUrl.DependsOnValue);
    }

    [Fact]
    public void ConfigurationProvided_carries_pack_name_and_values()
    {
        var values = new Dictionary<string, string>
        {
            ["bot_token"] = "secret123",
            ["mode"] = "polling"
        };

        var synapse = new ConfigurationProvided("TelegramBot", values);

        Assert.Equal("TelegramBot", synapse.PackName);
        Assert.Equal("secret123", synapse.Values["bot_token"]);
        Assert.Equal("polling", synapse.Values["mode"]);
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
