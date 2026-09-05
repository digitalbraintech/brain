using System.Net;
using System.Text;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubOAuthTests
{
    [Fact]
    public async Task Sole_unconfigured_provider_does_not_break_ordinary_kernel_requests()
    {
        var services = new ServiceCollection();
        services.AddGitHubAuthentication(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        var result = await context.AuthenticateAsync();
        Assert.True(result.None);
    }

    [Fact]
    public async Task AuthorizationUsesTheRequestedRepositoryAndTheConfiguredAppOnly()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new Responses(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("callback-only-token", request.Headers.Authorization?.Parameter);
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath == "/user/installations"
                ? """{"installations":[{"id":2,"app_id":999,"suspended_at":null},{"id":3,"app_id":7,"suspended_at":null}]}"""
                : """{"repositories":[{"id":42,"full_name":"acme/brain","name":"brain","owner":{"login":"acme"}}]}""";
        }));
        var access = await GitHubUserAccess.ResolveAsync(client, "callback-only-token", 7, "https://github.com/acme/brain.git", TestContext.Current.CancellationToken);
        Assert.Equal(new GitHubRepositoryAccess(7, 3, 42, "acme", "brain"), access);
        Assert.Equal(new[] { "/user/installations", "/user/installations/3/repositories" }, requests);
    }

    [Fact]
    public async Task MissingOrSuspendedAccessCannotCreateAConnection()
    {
        using var client = new HttpClient(new Responses(_ => """{"installations":[{"id":3,"app_id":7,"suspended_at":"2026-09-05T00:00:00Z"}]}"""));
        await Assert.ThrowsAsync<McpOperationException>(() => GitHubUserAccess.ResolveAsync(client,
            "token", 7, "https://github.com/acme/brain", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpOperationException>(() => GitHubUserAccess.ResolveAsync(client,
            "token", 7, "https://example.com/acme/brain", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void OAuthUsesPkceExactCallbackAndNoPersistentUserToken()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Microsoft:GitHub:App:AppId"] = "7",
            ["DigitalBrain:Microsoft:GitHub:App:ClientId"] = "configured-app-client",
            ["DigitalBrain:Microsoft:GitHub:App:ClientSecret"] = "private-test-value",
            ["DigitalBrain:Microsoft:GitHub:App:PublicOrigin"] = "https://brain.example/",
        }).Build();
        services.AddGitHubAuthentication(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OAuthOptions>>().Get("GitHubIntegration");
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        Assert.Equal("/integrations/github/callback", options.CallbackPath.Value);
        Assert.Empty(options.Scope);
    }

    private sealed class Responses(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json"),
            });
    }
}
