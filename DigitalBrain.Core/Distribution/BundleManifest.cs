namespace DigitalBrain.Core;

public enum BundleTier { Substrate, Channel, Content }

public enum BundleChannel { InApp, Telegram, Web }

[GenerateSerializer]
public record ExperienceRef(
    [property: Id(0)] string ExperienceId,
    [property: Id(1)] string EntryEvent = "start");

[GenerateSerializer]
public record BundleDependency(
    [property: Id(0)] string PackName,
    [property: Id(1)] string MinVersion);

// Product-level metadata a bundle declares in code (single source of truth). The next slice materializes
// this into the marketplace catalog at publish so discovery can facet by tier/channel without recompiling.
// PackManifest stays separate — it carries dispatch (HandledSynapseTypes) and config requirements.
[GenerateSerializer]
public record BundleManifest(
    [property: Id(0)] BundleTier Tier,
    [property: Id(1)] ExperienceRef? EntryExperience,
    [property: Id(2)] IReadOnlyList<BundleChannel> Channels,
    [property: Id(3)] IReadOnlyList<BundleDependency>? Dependencies = null);
