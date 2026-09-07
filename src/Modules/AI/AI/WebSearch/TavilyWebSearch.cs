using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI.WebSearch;

public sealed class TavilyWebSearch : IWebSearch
{
    public const string ApiKeyConfigurationKey = "DigitalBrain:AI:Tavily:ApiKey";
    public const string EnabledConfigurationKey = "DigitalBrain:AI:Tavily:Enabled";

    private static readonly Uri SearchEndpoint = new("https://api.tavily.com/search");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public TavilyWebSearch(HttpClient httpClient, IConfiguration configuration)
        : this(httpClient, configuration[ApiKeyConfigurationKey]
            ?? throw new InvalidOperationException(
                $"Tavily web search is enabled but {ApiKeyConfigurationKey} is missing."))
    {
    }

    internal TavilyWebSearch(HttpClient httpClient, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async Task<WebSearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 20);

        using var request = new HttpRequestMessage(HttpMethod.Post, SearchEndpoint)
        {
            Content = JsonContent.Create(new SearchRequest(query, "basic", false, maxResults), options: SerializerOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            detail = detail.Replace(_apiKey, "[REDACTED]", StringComparison.Ordinal);
            if (detail.Length > 2048)
            {
                detail = detail[..2048] + "…";
            }
            throw new HttpRequestException(
                $"Tavily search failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {detail}",
                inner: null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<TavilyResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JsonException("Tavily search returned an empty response.");
        return new WebSearchResponse(
            payload.Answer,
            payload.Results.Select(static result => new WebSearchResult(
                result.Title,
                new Uri(result.Url, UriKind.Absolute),
                result.Content,
                result.Score)).ToArray());
    }

    private sealed record SearchRequest(
        string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("include_answer")] bool IncludeAnswer,
        [property: JsonPropertyName("max_results")] int MaxResults);

    private sealed record TavilyResponse(string? Answer, TavilyResult[] Results);

    private sealed record TavilyResult(string Title, string Url, string Content, double Score);
}
