using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;
using DigitalBrain.Product.Identity;
using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.UI;

[GrainType("surface")]
internal sealed class SurfaceEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SurfaceState> state)
    : Entity<SurfaceState>(state), ISurface
{
    public async Task<SurfaceOpenReceipt> Open(CommandId commandId, SurfaceScene scene, int cap)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(scene)));
        var prior = State?.OpenReceipts?.FirstOrDefault(receipt => receipt.CommandId == commandId);
        if (prior is not null)
        {
            if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A surface command id cannot be reused for different content.");
            }
            return prior;
        }

        var previousKeys = ComponentKeys(State?.Scenes.FirstOrDefault(existing =>
            string.Equals(existing.SurfaceKey, scene.SurfaceKey, StringComparison.Ordinal))?.Root)
            .ToHashSet(StringComparer.Ordinal);
        var additions = Components(scene.Root)
            .Where(component => component.Key is { Length: > 0 } key && !previousKeys.Contains(key))
            .DistinctBy(component => component.Key, StringComparer.Ordinal)
            .ToArray();
        var receipt = new SurfaceOpenReceipt(commandId, fingerprint, additions);
        var receipts = (State?.OpenReceipts ?? []).Append(receipt).TakeLast(Math.Clamp(cap, 1, 500)).ToArray();
        await SaveScene(scene, cap, receipts);
        return receipt;
    }

    private async Task SaveScene(SurfaceScene scene, int cap, IReadOnlyList<SurfaceOpenReceipt>? receipts)
    {
        // Re-opening a scene refreshes its title and moves it to the most-recent slot.
        var scenes = (State?.Scenes ?? [])
            .Where(existing => !string.Equals(existing.SurfaceKey, scene.SurfaceKey, StringComparison.Ordinal))
            .ToList();
        scenes.Add(scene);
        while (scenes.Count > cap)
        {
            scenes.RemoveAt(0);
        }

        await SaveAsync(new SurfaceState(scenes, State?.Activities, receipts));
    }

    public Task ApplyActivity(ActivityView activity, int cap)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var previous = State?.Activities?.FirstOrDefault(existing => existing.Id == activity.Id && existing.Principal == activity.Principal);
        if (previous is not null && activity.Version > 0 && previous.Version >= activity.Version)
        {
            return Task.CompletedTask;
        }
        var activities = (State?.Activities ?? [])
            .Where(existing => existing.Id != activity.Id || existing.Principal != activity.Principal)
            .Append(activity).OrderByDescending(item => item.UpdatedAt).Take(Math.Clamp(cap, 1, 500)).ToArray();
        return SaveAsync(new SurfaceState(State?.Scenes ?? [], activities, State?.OpenReceipts));
    }

    private static IEnumerable<string> ComponentKeys(SurfaceComponent? root)
        => Components(root).Select(component => component.Key).OfType<string>();

    private static IEnumerable<SurfaceComponent> Components(SurfaceComponent? root)
    {
        if (root is null)
        {
            yield break;
        }
        yield return root;
        foreach (var child in root.Children ?? [])
        {
            foreach (var descendant in Components(child))
            {
                yield return descendant;
            }
        }
    }
}
