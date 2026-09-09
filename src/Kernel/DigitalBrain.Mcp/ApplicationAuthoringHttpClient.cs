using System.Net.Http.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Scripting;

namespace DigitalBrain.Mcp;

internal sealed class ApplicationAuthoringHttpClient(HttpClient http) : IApplicationAuthoring
{
    public Task<ApplicationCapabilityCatalog> CatalogAsync(IDigitalBrain brain,
        CancellationToken cancellationToken = default)
        => GetAsync<ApplicationCapabilityCatalog>("/applications/catalog", cancellationToken);

    public Task<ApplicationExpectations> SetExpectationsAsync(IDigitalBrain brain, string key,
        string documentJson, Guid operationId, string? expectedExpectationRevision,
        CancellationToken cancellationToken = default)
        => PostAsync<ApplicationExpectations>(key, "expectations", new
        {
            documentJson,
            operationId,
            expectedExpectationRevision,
        }, cancellationToken);

    public Task<ApplicationExpectations> ReadExpectationsAsync(IDigitalBrain brain, string key,
        string expectationRevision, CancellationToken cancellationToken = default)
        => GetAsync<ApplicationExpectations>(
            $"/applications/{Key(key)}/expectations/{Uri.EscapeDataString(expectationRevision)}", cancellationToken);

    public Task<ApplicationScenarioRun> RunScenariosAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationScenarioRun>(key, "scenarios/run", new { expectedSourceRevision }, cancellationToken);

    public Task<ApplicationScenarioRun> ReadScenarioRunAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
        => GetAsync<ApplicationScenarioRun>(
            $"/applications/{Key(key)}/scenarios?expectedSourceRevision={Uri.EscapeDataString(expectedSourceRevision)}", cancellationToken);
    public Task<string> TemplateAsync(IDigitalBrain brain, string key, CancellationToken cancellationToken = default)
        => GetAsync<string>($"/applications/{Key(key)}/template", cancellationToken);

    public Task<ApplicationInvocation> ReadInvocationAsync(IDigitalBrain brain, string key,
        Guid operationId, CancellationToken cancellationToken = default)
        => GetAsync<ApplicationInvocation>($"/applications/{Key(key)}/invocations/{operationId:D}", cancellationToken);

    public Task<ApplicationInvocation> CancelInvocationAsync(IDigitalBrain brain, string key,
        Guid operationId, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationInvocation>(
            key, $"invocations/{operationId:D}/cancel", new { }, cancellationToken);

    public Task<ApplicationInvocation> InvokeAsync(IDigitalBrain brain, string key, string operation,
        string inputJson, Guid operationId, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationInvocation>(key, "invoke", new { operation, inputJson, operationId }, cancellationToken);

    public Task<ApplicationSource> SaveAsync(IDigitalBrain brain, string key, string source,
        string? expectedRevision, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationSource>(key, "save", new { source, expectedRevision }, cancellationToken);

    public Task<ApplicationSource> ReadAsync(IDigitalBrain brain, string key,
        CancellationToken cancellationToken = default)
        => GetAsync<ApplicationSource>($"/applications/{Key(key)}", cancellationToken);

    public async Task<IReadOnlyList<string>> ListFilesAsync(IDigitalBrain brain, string key,
        CancellationToken cancellationToken = default)
        => await GetAsync<string[]>($"/applications/{Key(key)}/files", cancellationToken).ConfigureAwait(false);

    public Task<ApplicationSource> ReadFileAsync(IDigitalBrain brain, string key, string path,
        CancellationToken cancellationToken = default)
        => GetAsync<ApplicationSource>(
            $"/applications/{Key(key)}/file?path={Uri.EscapeDataString(path)}", cancellationToken);

    public Task<ApplicationSource> SaveFileAsync(IDigitalBrain brain, string key, string path, string source,
        string expectedRevision, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationSource>(key, "file", new { path, source, expectedRevision }, cancellationToken);

    public async Task<IReadOnlyList<ApplicationSourceSummary>> ListAsync(IDigitalBrain brain,
        CancellationToken cancellationToken = default)
        => await GetAsync<ApplicationSourceSummary[]>("/applications", cancellationToken).ConfigureAwait(false);

    public Task<ApplicationValidation> ValidateAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationValidation>(key, "validate", new { expectedSourceRevision }, cancellationToken);

    public Task<ApplicationDescription> DescribeAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
        => GetAsync<ApplicationDescription>(
            $"/applications/{Key(key)}/description?expectedSourceRevision={Uri.EscapeDataString(expectedSourceRevision)}",
            cancellationToken);

    public Task<ApplicationActivation> ActivateAsync(IDigitalBrain brain, string key,
        string expectedSourceRevision, CancellationToken cancellationToken = default)
        => PostAsync<ApplicationActivation>(key, "activate", new { expectedSourceRevision }, cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The application authoring service returned an empty response.");
    }

    private async Task<T> PostAsync<T>(string key, string operation, object content,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            $"/applications/{Key(key)}/{operation}", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The application authoring service returned an empty response.");
    }

    private static string Key(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Uri.EscapeDataString(key);
    }
}
