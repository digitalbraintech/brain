using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions;

public static class BehaviorReferenceExtensions
{
    public static async Task<BehaviorView> SaveScriptAsync<TInput, TOutput>(
        this NeuronReference<IBehavior> behavior, string source, BehaviorInputPolicy inputPolicy,
        CancellationToken cancellationToken = default)
        where TInput : Signal where TOutput : Signal
        => await AwaitValidation(behavior, (await behavior.RequestAsync(new SaveBehaviorScript(source,
            [typeof(TInput).Name], [typeof(TOutput).Name], InputPolicy: inputPolicy), cancellationToken).ConfigureAwait(false)).Behavior,
            cancellationToken).ConfigureAwait(false);

    public static async Task<BehaviorView> SaveScriptAsync<TInput, TOutput>(
        this NeuronReference<IBehavior> behavior, string source, CancellationToken cancellationToken = default)
        where TInput : Signal where TOutput : Signal
        => await AwaitValidation(behavior, (await behavior.RequestAsync(new SaveBehaviorScript(source,
            [typeof(TInput).Name], [typeof(TOutput).Name]), cancellationToken).ConfigureAwait(false)).Behavior,
            cancellationToken).ConfigureAwait(false);

    public static async Task<BehaviorView> SaveScriptAsync(
        this NeuronReference<IBehavior> behavior, string source, CancellationToken cancellationToken = default)
        => await AwaitValidation(behavior, (await behavior.RequestAsync(new SaveBehaviorScript(source), cancellationToken)
            .ConfigureAwait(false)).Behavior, cancellationToken).ConfigureAwait(false);

    public static async Task<BehaviorView> ActivateAsync(
        this NeuronReference<IBehavior> behavior, CancellationToken cancellationToken = default)
        => (await behavior.RequestAsync(new EnableBehavior(), cancellationToken).ConfigureAwait(false)).Behavior;

    public static async Task<BehaviorView> DisableAsync(
        this NeuronReference<IBehavior> behavior, CancellationToken cancellationToken = default)
        => (await behavior.RequestAsync(new DisableBehavior(), cancellationToken).ConfigureAwait(false)).Behavior;

    public static async Task<BehaviorView> InvokeAsync(
        this NeuronReference<IBehavior> behavior, Signal input, CancellationToken cancellationToken = default)
        => (await behavior.RequestAsync(new InvokeBehavior(input), cancellationToken).ConfigureAwait(false)).Behavior;

    public static async Task<BehaviorView> ReadAsync(
        this NeuronReference<IBehavior> behavior, CancellationToken cancellationToken = default)
        => (await behavior.RequestAsync(new ReadBehavior(), cancellationToken).ConfigureAwait(false)).Behavior;

    private static async Task<BehaviorView> AwaitValidation(
        NeuronReference<IBehavior> behavior, BehaviorView saved, CancellationToken cancellationToken)
    {
        var revision = saved.Draft?.Revision;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (saved.Draft?.Validation == BehaviorValidation.Pending)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("The script is saved, but validation has not completed. Check that the Scripting resource is running, then read the draft diagnostics.");
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            saved = await behavior.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (saved.Draft?.Revision != revision)
            {
                throw new InvalidOperationException("Another edit replaced the script while it was being validated.");
            }
        }
        if (saved.Draft?.Validation != BehaviorValidation.Valid)
        {
            throw new InvalidOperationException("The script was saved with diagnostics: " + string.Join(Environment.NewLine, saved.Draft?.Diagnostics ?? []));
        }
        return saved;
    }
}
