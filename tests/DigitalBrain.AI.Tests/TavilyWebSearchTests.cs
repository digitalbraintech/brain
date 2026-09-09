using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI.WebSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.AI.Tests;

public sealed class TavilyWebSearchTests
{
    [Fact]
    public void Registration_adds_search_and_shared_agent_tool_only_when_enabled()
    {
        var disabled = new ServiceCollection();
        WebSearchHosting.Add(disabled, new ConfigurationBuilder().Build());
        Assert.DoesNotContain(disabled, descriptor => descriptor.ServiceType == typeof(IWebSearch));
        Assert.DoesNotContain(disabled, descriptor => descriptor.ServiceType == typeof(IAgentToolSource));

        var enabled = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [TavilyWebSearch.EnabledConfigurationKey] = "true",
            [TavilyWebSearch.ApiKeyConfigurationKey] = "sentinel-key",
        }).Build();
        WebSearchHosting.Add(enabled, configuration);
        enabled.AddSingleton<IConfiguration>(configuration);

        Assert.Single(enabled, descriptor => descriptor.ServiceType == typeof(IWebSearch));
        Assert.Single(enabled, descriptor => descriptor.ServiceType == typeof(IAgentToolSource));
        using var provider = enabled.BuildServiceProvider();
        Assert.IsType<TavilyWebSearch>(provider.GetRequiredService<IWebSearch>());
        Assert.IsType<WebSearchToolSource>(provider.GetRequiredService<IAgentToolSource>());
    }

    [Fact]
    public async Task Search_sends_authenticated_basic_request_and_returns_ranked_results()
    {
        var handler = new FixtureHandler(HttpStatusCode.OK, """
            {
              "query": "current Aspire release",
              "answer": "Aspire 13 is current.",
              "results": [
                {
                  "title": "Aspire releases",
                  "url": "https://aspire.dev/releases",
                  "content": "Release notes",
                  "score": 0.91,
                  "raw_content": null,
                  "favicon": "https://aspire.dev/favicon.ico",
                  "images": []
                }
              ],
              "images": [],
              "response_time": 0.42,
              "request_id": "fixture-request"
            }
            """);
        var search = new TavilyWebSearch(new HttpClient(handler), "fixture-key");

        var response = await search.SearchAsync("current Aspire release", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Aspire 13 is current.", response.Answer);
        var result = Assert.Single(response.Results);
        Assert.Equal("Aspire releases", result.Title);
        Assert.Equal(new Uri("https://aspire.dev/releases"), result.Url);
        Assert.Equal("Release notes", result.Content);
        Assert.Equal(0.91, result.Score);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(new Uri("https://api.tavily.com/search"), handler.Request.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("fixture-key", handler.Request.Headers.Authorization.Parameter);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal("current Aspire release", request.RootElement.GetProperty("query").GetString());
        Assert.Equal("basic", request.RootElement.GetProperty("search_depth").GetString());
        Assert.False(request.RootElement.GetProperty("include_answer").GetBoolean());
    }

    [Fact]
    public async Task Search_exposes_provider_failure_without_fabricating_results()
    {
        var search = new TavilyWebSearch(
            new HttpClient(new FixtureHandler(HttpStatusCode.TooManyRequests, "{\"detail\":\"quota exhausted\"}")),
            "fixture-key");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            search.SearchAsync("anything", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Contains("quota exhausted", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_failure_never_copies_the_api_key_from_provider_content()
    {
        const string apiKey = "fixture-secret-key";
        var search = new TavilyWebSearch(
            new HttpClient(new FixtureHandler(HttpStatusCode.Unauthorized, $"{{\"detail\":\"bad key {apiKey}\"}}")),
            apiKey);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            search.SearchAsync("anything", cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(apiKey, error.Message, StringComparison.Ordinal);
        Assert.Contains("401", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_tool_rejects_invocation_after_its_turn_context_is_disposed()
    {
        var source = new WebSearchToolSource(new NeverCalledSearch());
        var context = new AgentToolContext(
            new NeuronId("agent", new OwnerId("owner"), "web"),
            principal: null,
            new NoopRequests());
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(
            await source.GetToolsAsync(context, TestContext.Current.CancellationToken)));
        context.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "latest release" },
                TestContext.Current.CancellationToken));
    }

    private sealed class FixtureHandler(HttpStatusCode statusCode, string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class NeverCalledSearch : IWebSearch
    {
        public Task<WebSearchResponse> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disposed tool context must reject the call before reaching the provider.");
    }

    private sealed class NoopRequests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(
            string instanceName,
            AgentRequest request,
            CancellationToken cancellationToken = default)
            where TAgent : IAgent
            => throw new NotSupportedException();
    }
}

