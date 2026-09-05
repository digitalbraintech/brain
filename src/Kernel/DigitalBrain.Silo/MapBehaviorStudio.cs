using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.Kernel;

internal static class BehaviorStudioHttpMaps
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapBehaviorStudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/behaviors", static async Task<IResult> (IDigitalBrain brain, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            var principal = HttpActor.Current.PrincipalId;
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            var ids = await grains.GetGrain<IBehaviorsKernel>(NeuronId.For<IBehaviors>(brain.Owner, "default").ToGrainId())
                .ReadBehaviorIds().WaitAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<StudioBehavior>();
            foreach (var id in ids.Where(id => id.Owner == brain.Owner && PrincipalPartition.OwnsInstance(principal, id.Name)))
            {
                var state = await grains.GetGrain<IBehaviorKernel>(id.ToGrainId()).ReadState().WaitAsync(cancellationToken).ConfigureAwait(false);
                result.Add(Project(state));
            }
            return Results.Ok(result.OrderBy(item => item.Name, StringComparer.Ordinal));
        });
        endpoints.MapGet("/behaviors/signals", static () => Results.Ok(SignalTypes().Select(type => new
        {
            type.Name,
            Type = type.FullName,
            Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanWrite).Select(property => new { property.Name, Type = property.PropertyType.Name }),
        }).OrderBy(item => item.Name, StringComparer.Ordinal)));
        endpoints.MapGet("/behaviors/{name}", static async Task<IResult> (string name, IDigitalBrain brain, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (!TryId(brain.Owner, name, out var id)) { return Results.BadRequest(new { message = "Use a local behavior name." }); }
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            var state = await grains.GetGrain<IBehaviorKernel>(id.ToGrainId()).ReadState().WaitAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(Project(state));
        });
        endpoints.MapPost("/behaviors/{name}/{operation}", static async Task<IResult> (
            string name, string operation, StudioBehaviorCommand command, IDigitalBrain brain, IGrainFactory grains, CancellationToken cancellationToken) =>
        {
            if (!TryId(brain.Owner, name, out var id)) { return Results.BadRequest(new { message = "Use a local behavior name." }); }
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            try
            {
                Signal<BehaviorRead> request;
                switch (operation)
                {
                    case "save":
                        if (string.IsNullOrWhiteSpace(command.Source) || command.Source.Length > 512_000)
                        { return Results.BadRequest(new { message = "Provide C# source of at most 512,000 characters." }); }
                        var current = await grains.GetGrain<IBehaviorKernel>(id.ToGrainId()).ReadState().WaitAsync(cancellationToken).ConfigureAwait(false);
                        var policy = command.InputPolicy ?? current.Draft?.InputPolicy ?? current.Active?.InputPolicy ?? BehaviorInputPolicy.EveryEvent;
                        if ((policy & ~(BehaviorInputPolicy.LatestPerSubject | BehaviorInputPolicy.ObserveFromActivation | BehaviorInputPolicy.OncePerVersion)) != 0)
                        { return Results.BadRequest(new { message = "Choose supported input policy options." }); }
                        request = new SaveBehaviorScript(command.Source, command.InputSignalTypes, command.OutputSignalTypes, command.ExpectedDraftRevision, policy);
                        break;
                    case "enable": request = new EnableBehavior(command.ExpectedDraftRevision); break;
                    case "disable": request = new DisableBehavior(); break;
                    case "invoke":
                        var state = await grains.GetGrain<IBehaviorKernel>(id.ToGrainId()).ReadState().WaitAsync(cancellationToken).ConfigureAwait(false);
                        var types = SignalTypes().Where(type => type.Name == command.InputType || type.FullName == command.InputType).ToArray();
                        if (types.Length != 1 || state.Active is null || !state.Active.InputSignalTypes.Any(name => name == types[0].Name || name == types[0].FullName))
                        { return Results.BadRequest(new { message = "Choose an input declared by the active behavior revision." }); }
                        if (command.Input.ValueKind != JsonValueKind.Object || command.Input.GetRawText().Length > 128_000)
                        { return Results.BadRequest(new { message = "Provide a bounded JSON object for the selected signal." }); }
                        if (System.Text.Json.JsonSerializer.Deserialize(command.Input, types[0], Json) is not Signal input)
                        { return Results.BadRequest(new { message = "The signal input is invalid." }); }
                        request = new InvokeBehavior(input);
                        break;
                    default: return Results.BadRequest(new { message = "Unknown behavior operation." });
                }
                var result = await brain.Get<IBehavior>(id.Name).RequestAsync(request, cancellationToken).ConfigureAwait(false);
                return Results.Ok(Project(result.Behavior));
            }
            catch (JsonException) { return Results.BadRequest(new { message = "The input does not match the selected signal." }); }
            catch (ArgumentException) { return Results.BadRequest(new { message = "The behavior source or declared signal types are invalid." }); }
            catch (NeuronAuthorizationException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (InvalidOperationException) { return Results.Conflict(new { message = "The behavior changed or is not ready for this operation. Refresh its state and diagnostics." }); }
        });
        return endpoints;
    }

    internal static bool TryId(OwnerId owner, string name, out NeuronId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) { return false; }
        try { id = NeuronId.For<IBehavior>(owner, PrincipalScoped.InstanceName(HttpActor.Current.PrincipalId, name)); return true; }
        catch (ArgumentException) { return false; }
    }

    internal static StudioBehavior Project(BehaviorView state)
    {
        _ = PrincipalPartition.TryParse(state.Id.Name, out _, out var name);
        return new(BrainGraphProjection.InstanceId(state.Id), name ?? state.Id.Name, state.Enabled, state.Epoch,
            state.PendingCount, state.Detail, Program(state.Draft), Program(state.Active));
    }

    private static StudioProgram? Program(BehaviorProgram? program) => program is null ? null : new(
        program.Revision, program.Source, program.InputSignalTypes, program.OutputSignalTypes,
        program.Validation.ToString(), program.Diagnostics, program.CreatedAt, program.InputPolicy);

    private static IEnumerable<Type> SignalTypes() => AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => !assembly.IsDynamic && assembly.GetName().Name?.StartsWith("DigitalBrain", StringComparison.Ordinal) == true)
        .SelectMany(assembly => assembly.GetExportedTypes())
        .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && typeof(Signal).IsAssignableFrom(type));
}

internal sealed record StudioBehavior(string Id, string Name, bool Enabled, long Epoch, int PendingCount,
    string? Detail, StudioProgram? Draft, StudioProgram? Active);
internal sealed record StudioProgram(Guid Revision, string Source, IReadOnlyList<string> InputSignalTypes,
    IReadOnlyList<string> OutputSignalTypes, string Validation, IReadOnlyList<string> Diagnostics, DateTimeOffset CreatedAt,
    BehaviorInputPolicy InputPolicy = BehaviorInputPolicy.EveryEvent);
internal sealed record StudioBehaviorCommand(string? Source = null, string[]? InputSignalTypes = null,
    string[]? OutputSignalTypes = null, Guid? ExpectedDraftRevision = null, string? InputType = null, JsonElement Input = default,
    BehaviorInputPolicy? InputPolicy = null);
