using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.WebSearch;

internal sealed class WebSearchToolSource(IWebSearch search) : IAgentToolSource
{
    public ValueTask<IReadOnlyList<AITool>> GetToolsAsync(
        AgentToolContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireActive();
        Task<WebSearchResponse> SearchAsync(
            [Description("The search query.")] string query,
            [Description("Maximum number of results, from 1 through 20.")] int maxResults = 5,
            CancellationToken cancellationToken = default)
        {
            context.RequireActive();
            return search.SearchAsync(query, maxResults, cancellationToken);
        }

        return ValueTask.FromResult<IReadOnlyList<AITool>>([
            AIFunctionFactory.Create(SearchAsync, new AIFunctionFactoryOptions
            {
                Name = "search_web",
                Description = "Search the public web with Tavily and return ranked source titles, URLs, snippets, and scores.",
            }),
        ]);
    }
}
