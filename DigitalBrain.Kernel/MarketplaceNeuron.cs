using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.marketplace.v1")]
public class MarketplaceNeuron(ILogger<MarketplaceNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IMarketplaceNeuron
{
    private Dictionary<string, NeuroPack>? _publishedCache;

    public async Task HandleAsync(PublishToMarketplace cmd)
    {
        var remote = ServiceProvider.GetService<IRemoteMarketplaceClient>();
        var useRemote = ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:UseRemote", false) ?? false;

        if (useRemote && remote is not null)
        {
            await remote.PublishAsync(cmd);
        }

        if (GatePublishing && !PublisherTrust.IsTrusted(ToNeuroPack(cmd), TrustedPublisherKeys()))
        {
            Logger.LogWarning("Publish REJECTED - pack {Name}@{Ver} is not from a trusted publisher (publishing gate enabled)",
                cmd.PackName, cmd.Version);
            return;
        }

        Logger.LogInformation("Marketplace PUBLISHED real pack {Name}@{Ver} owner={Owner} private={Private} commission={Rate:P0}",
            cmd.PackName, cmd.Version, cmd.OwnerId, cmd.IsPrivate, cmd.CommissionRate);

        EnsureCache();
        _publishedCache![KeyFor(cmd.PackName, cmd.Version)] = MaterializeManifest(ToNeuroPack(cmd));

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        var published = _publishedCache!.Values.ToList();
        var listSurface = UiSurfaceLiveData.MarketplaceListFromPacks(published, published);
        await FireAsync(listSurface);
        if (bus != null)
        {
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(listSurface, Self.Value));
        }
    }

    private static string KeyFor(string name, string version) => $"{name}@{version}";

    private static NeuroPack ToNeuroPack(PublishToMarketplace p) =>
        new(p.PackName, p.Version, p.OwnerId, p.IsPrivate, p.CommissionRate, p.Code, p.Description, p.AuthorPublicKeyBase64, p.SignatureBase64, p.Price);

    private NeuroPack MaterializeManifest(NeuroPack pack)
    {
        if (string.IsNullOrEmpty(pack.Code)) return pack;

        var embodiment = ServiceProvider.GetService<IPackEmbodiment>();
        if (embodiment is null) return pack;

        try
        {
            using var embodied = embodiment.Embody(pack.Name, pack.Code);
            var manifest = embodied.GetBundleManifest();
            return manifest is null ? pack : pack with { Manifest = manifest };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Manifest materialization failed for pack {Name}@{Ver}; listing without bundle metadata",
                pack.Name, pack.Version);
            return pack;
        }
    }

    public async Task HandleAsync(InstallFromMarketplace cmd)
    {
        var remote = ServiceProvider.GetService<IRemoteMarketplaceClient>();
        var useRemote = ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:UseRemote", false) ?? false;

        if (useRemote && remote is not null)
        {
            await remote.InstallAsync(cmd);
        }

        var pack = FindPublishedPack(cmd.PackName, cmd.Version);
        if (pack == null)
        {
            Logger.LogWarning("Install failed - pack not found: {Key}", cmd.PackName + "@" + cmd.Version);
            return;
        }

        if (pack.IsPrivate && cmd.BuyerId != pack.OwnerId)
        {
            Logger.LogWarning("Install blocked - pack {Key} is private to owner {Owner}", cmd.PackName + "@" + cmd.Version, pack.OwnerId);
            return;
        }

        var isSigned = !string.IsNullOrEmpty(pack.AuthorPublicKeyBase64) && !string.IsNullOrEmpty(pack.SignatureBase64);
        if (isSigned)
        {
            if (!PackSignatureVerifier.VerifyPack(pack))
            {
                Logger.LogWarning("Install REJECTED - invalid signature on pack {Key}", cmd.PackName + "@" + cmd.Version);
                return;
            }
            Logger.LogInformation("Install signature verified for pack {Key}", cmd.PackName + "@" + cmd.Version);
        }
        else if (RejectUnsignedPacks)
        {
            Logger.LogWarning("Install REJECTED - pack {Key} is unsigned and unsigned installs are disabled", cmd.PackName + "@" + cmd.Version);
            return;
        }
        else
        {
            Logger.LogWarning("Install WARNING - pack {Key} is unsigned (allowed during trust transition)", cmd.PackName + "@" + cmd.Version);
        }

        if (pack.Price > 0m)
        {
            var license = GrainFactory.GetGrain<ILicenseNeuron>("license-main");
            if (!await license.HasLicenseAsync(pack.Name, cmd.BuyerId))
            {
                Logger.LogWarning("Install REJECTED - premium pack {Key} requires a license for buyer {Buyer}", cmd.PackName + "@" + cmd.Version, cmd.BuyerId);
                return;
            }
            Logger.LogInformation("Install entitlement verified for premium pack {Key}, buyer {Buyer}", cmd.PackName + "@" + cmd.Version, cmd.BuyerId);
        }

        var commissionAmount = 0.0;
        await FireAsync(new CommissionTaken(
            pack.Name,
            pack.Version,
            cmd.BuyerId,
            pack.OwnerId,
            pack.CommissionRate,
            commissionAmount));

        await FireAsync(new NeuroPackInstalled(pack));

        if (string.Equals(cmd.PackName, KernelPack.Name, StringComparison.OrdinalIgnoreCase))
        {
            var aspire = GrainFactory.GetGrain<IAspireNeuron>("aspire-main");
            await aspire.FireAsync(new PerformKernelSelfUpdate(cmd.Version));
        }

        var genKey = "generated-" + pack.Name.ToLowerInvariant();
        var generated = GrainFactory.GetGrain<IGeneratedNeuron>(genKey);
        await generated.DeliverAsync(new NeuroPackInstalled(pack));
        await generated.FireAsync(new ExperienceUsed(pack.Name, "installed-and-activated", cmd.BuyerId, cmd.SessionId));

        var pub = new List<NeuroPack> { pack };
        var inst = new List<NeuroPack> { pack };
        var refInst = UiSurfaceLiveData.InstalledBundlesFromPacks(pub, inst, cmd.BuyerId, cmd.SessionId);
        await FireAsync(refInst);
        var bus2 = ServiceProvider.GetService<HomeFeedBus>();
        if (bus2 != null)
        {
            bus2.Broadcast(UiSurfaceRfwBridge.FromUiSurface(refInst, Self.Value));
        }

        Logger.LogInformation("Marketplace INSTALL {Key} by {Buyer}. Commission {Rate:P0} taken for seller {Seller}.",
            cmd.PackName + "@" + cmd.Version, cmd.BuyerId, pack.CommissionRate, pack.OwnerId);
    }

    public async Task HandleAsync(ListPublished _cmd)
    {
        var remote = ServiceProvider.GetService<IRemoteMarketplaceClient>();
        var useRemote = ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:UseRemote", false) ?? false;

        if (useRemote && remote is not null)
        {
            var list = await remote.ListAsync();
            await FireAsync(list);
            return;
        }

        var packs = GetPublishedPacks();
        Logger.LogInformation("Marketplace listing {Count} real packs", packs.Count);
        await FireAsync(new PublishedList(packs));
    }

    public async Task HandleAsync(FilterMarketplace cmd)
    {
        var published = GetPublishedPacks();
        var surface = UiSurfaceLiveData.MarketplaceTreeSurface(
            published, Array.Empty<NeuroPack>(), cmd.Tier, cmd.Channel, Self.Value);
        await FireAsync(surface);

        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is not null)
            bus.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
    }

    private IReadOnlyList<NeuroPack> GetPublishedPacks()
    {
        EnsureCache();
        return _publishedCache!.Values.ToList();
    }

    private void EnsureCache()
    {
        if (_publishedCache is not null) return;

        _publishedCache = new Dictionary<string, NeuroPack>(StringComparer.OrdinalIgnoreCase);

        var gated = GatePublishing;
        var trustedKeys = gated ? TrustedPublisherKeys() : null;
        foreach (var p in OutgoingJournal.Concat(IncomingJournal).OfType<PublishToMarketplace>())
        {
            var pack = ToNeuroPack(p);
            if (gated && !PublisherTrust.IsTrusted(pack, trustedKeys!)) continue;
            _publishedCache[KeyFor(p.PackName, p.Version)] = pack;
        }

        foreach (var s in MarketplaceSeeds.LocalUiPacks)
        {
            var k = KeyFor(s.Name, s.Version);
            if (!_publishedCache.ContainsKey(k)) _publishedCache[k] = MaterializeManifest(s);
        }
    }

    private NeuroPack? FindPublishedPack(string name, string version)
    {
        EnsureCache();
        _publishedCache!.TryGetValue(KeyFor(name, version), out var p);
        return p;
    }

    private bool RejectUnsignedPacks =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:RejectUnsignedPacks", true) ?? true;

    private bool GatePublishing =>
        ServiceProvider.GetService<IConfiguration>()?.GetValue("DigitalBrain:Marketplace:GatePublishing", false) ?? false;

    private IReadOnlyCollection<string> TrustedPublisherKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { TrustedPublisher.PublicKeyBase64 };
        var configured = ServiceProvider.GetService<IConfiguration>()
            ?.GetSection("DigitalBrain:Marketplace:TrustedPublisherKeys").Get<string[]>();
        if (configured is not null)
            foreach (var key in configured) keys.Add(key);
        return keys;
    }

}
