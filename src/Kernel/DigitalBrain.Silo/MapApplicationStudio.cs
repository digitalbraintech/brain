using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal static class ApplicationStudioHttpMaps
{
    public static IEndpointRouteBuilder MapApplicationStudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/applications/catalog", static async Task<IResult> (
            IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.CatalogAsync(brain, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/expectations", static async Task<IResult> (
            string key, ApplicationExpectationRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.SetExpectationsAsync(brain, key, request.DocumentJson,
                request.OperationId, request.ExpectedExpectationRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}/expectations/{expectationRevision}", static async Task<IResult> (
            string key, string expectationRevision, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ReadExpectationsAsync(
                brain, key, expectationRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}/description", static async Task<IResult> (
            string key, string expectedSourceRevision, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.DescribeAsync(brain, key, expectedSourceRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/scenarios/run", static async Task<IResult> (
            string key, ApplicationRevisionRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.RunScenariosAsync(brain, key, request.ExpectedSourceRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}/scenarios", static async Task<IResult> (
            string key, string expectedSourceRevision, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            try
            {
                return Results.Ok(await authoring.ReadScenarioRunAsync(brain, key, expectedSourceRevision, ct).ConfigureAwait(false));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
        endpoints.MapGet("/applications/{key}/files", static async Task<IResult> (
            string key, IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ListFilesAsync(brain, key, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}/file", static async Task<IResult> (
            string key, string path, IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ReadFileAsync(brain, key, path, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/file", static async Task<IResult> (
            string key, ApplicationFileSaveRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.SaveFileAsync(brain, key, request.Path, request.Source,
                request.ExpectedRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}/template", static async Task<IResult> (
            string key, IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Json(await authoring.TemplateAsync(brain, key, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}/invocations/{operationId:guid}", static async Task<IResult> (
            string key, Guid operationId, IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ReadInvocationAsync(brain, key, operationId, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/invocations/{operationId:guid}/cancel", static async Task<IResult> (
            string key, Guid operationId, IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.CancelInvocationAsync(brain, key, operationId, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/invoke", static async Task<IResult> (
            string key, ApplicationInvokeRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.InvokeAsync(brain, key, request.Operation,
                request.InputJson, request.OperationId, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications", static async Task<IResult> (
            IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ListAsync(brain, ct).ConfigureAwait(false));
        });
        endpoints.MapGet("/applications/{key}", static async Task<IResult> (
            string key, IApplicationAuthoring authoring, IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ReadAsync(brain, key, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/save", static async Task<IResult> (
            string key, ApplicationSaveRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.SaveAsync(
                brain, key, request.Source, request.ExpectedRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/validate", static async Task<IResult> (
            string key, ApplicationRevisionRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ValidateAsync(
                brain, key, request.ExpectedSourceRevision, ct).ConfigureAwait(false));
        });
        endpoints.MapPost("/applications/{key}/activate", static async Task<IResult> (
            string key, ApplicationRevisionRequest request, IApplicationAuthoring authoring,
            IDigitalBrain brain, CancellationToken ct) =>
        {
            using var actor = VerifiedActor.Enter(HttpActor.Current);
            return Results.Ok(await authoring.ActivateAsync(
                brain, key, request.ExpectedSourceRevision, ct).ConfigureAwait(false));
        });
        return endpoints;
    }
}

internal sealed record ApplicationSaveRequest(string Source, string? ExpectedRevision);
internal sealed record ApplicationFileSaveRequest(string Path, string Source, string ExpectedRevision);
internal sealed record ApplicationRevisionRequest(string ExpectedSourceRevision);
internal sealed record ApplicationInvokeRequest(string Operation, string InputJson, Guid OperationId);
internal sealed record ApplicationExpectationRequest(
    string DocumentJson, Guid OperationId, string? ExpectedExpectationRevision);
