namespace DigitalBrain.AI.WebSearch;

public interface IWebSearch
{
    Task<WebSearchResponse> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}

public sealed record WebSearchResponse(string? Answer, IReadOnlyList<WebSearchResult> Results);

public sealed record WebSearchResult(string Title, Uri Url, string Content, double Score);
