using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("surface")]
internal sealed class SurfaceEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SurfaceState> state)
    : Entity<SurfaceState>(state), ISurface
{
    public async Task Open(SurfaceScene scene, int cap)
    {
        ArgumentNullException.ThrowIfNull(scene);

        // Re-opening a scene refreshes its title and moves it to the most-recent slot.
        var scenes = (State?.Scenes ?? [])
            .Where(existing => !string.Equals(existing.SurfaceKey, scene.SurfaceKey, StringComparison.Ordinal))
            .ToList();
        scenes.Add(scene);
        while (scenes.Count > cap)
        {
            scenes.RemoveAt(0);
        }

        await SaveAsync(new SurfaceState(scenes, State?.Activities));
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
        return SaveAsync(new SurfaceState(State?.Scenes ?? [], activities));
    }
}
