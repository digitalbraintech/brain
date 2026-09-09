using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubAuthentication
{
    internal static void AddGitHubAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new GitHubOAuthConfiguration(configuration);
        var definition = GitHubLogins.LoginDefinition;
        services.AddSingleton(settings);
        services.AddSingleton<GitHubLogins>();
        services.AddSingleton<IUserActionSource>(s => s.GetRequiredService<GitHubLogins>());
        services.AddSingleton<IHttpSurface>(s => new BrowserLoginSurface(s.GetRequiredService<GitHubLogins>()));
        services.AddHostedService<BrowserLoginWorker<GitHubLogins>>();
        services.AddSingleton<DigitalBrain.AI.IAgentToolSource, GitHubSetupTools>();
        services.AddLogging(logging =>
        {
            logging.AddFilter(typeof(GitHubOAuthHandler).FullName, LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Authentication.OAuth", LogLevel.None);
        });
        services.AddAuthentication().AddOAuth<OAuthOptions, GitHubOAuthHandler>(definition.Scheme, options =>
        {
            options.ClientId = settings.IsConfigured ? settings.ClientId : "not-configured";
            options.ClientSecret = settings.IsConfigured ? settings.ClientSecret : "not-configured";
            options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
            options.TokenEndpoint = "https://github.com/login/oauth/access_token";
            options.CallbackPath = definition.CallbackPath;
            options.SignInScheme = "GitHubNeverSignIn";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.Scope.Clear(); // GitHub App permissions belong to the App installation.
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
            options.BackchannelTimeout = TimeSpan.FromSeconds(30);
            options.BackchannelHttpHandler = new HttpClientHandler { AllowAutoRedirect = false };
            options.CorrelationCookie.HttpOnly = true;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = settings.PublicOrigin?.Scheme == "https" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    var request = BrowserLoginCorrelation.VerifiedRequest(context.HttpContext);
                    if (request is null || string.IsNullOrWhiteSpace(context.AccessToken))
                    {
                        throw new McpOperationException("GitHub authorization did not produce a verified user token.");
                    }
                    var logins = context.HttpContext.RequestServices.GetRequiredService<GitHubLogins>();
                    var setup = context.HttpContext.RequestServices.GetRequiredService<IGitHubSetup>();
                    await logins.AcceptForActorAsync(request, async (actor, scope, commit) =>
                    {
                        if (scope is null)
                        {
                            throw new McpOperationException("The repository setup intent is missing.");
                        }
                        var access = await GitHubUserAccess.ResolveAsync(context.Backchannel, context.AccessToken, settings.AppId,
                            scope, context.HttpContext.RequestAborted).ConfigureAwait(false);
                        Task<GitHubSetupResult>? connection = null;
                        using var verified = VerifiedActor.Enter(actor.Actor);
                        // Start the authorized mutation only inside the correlation's
                        // cancellation gate; publish completion after its durable ACK.
                        commit(() => connection = setup.ConnectAsync(actor.Chat.Owner, actor.Actor.PrincipalId, access,
                            context.HttpContext.RequestAborted));
                        await connection!.ConfigureAwait(false);
                    }).ConfigureAwait(false);
                },
                OnTicketReceived = async context =>
                {
                    context.HandleResponse();
                    await LoginPage.WriteAsync(context.HttpContext, "GitHub connected",
                        "Return to DigitalBrain. Repository access is saved. If webhook verification is still pending, the operator should send a signed GitHub App ping to the configured public URL within 10 minutes. Your original behavior draft will resume only after that proof arrives.", 200).ConfigureAwait(false);
                },
                OnRemoteFailure = async context =>
                {
                    context.HandleResponse();
                    context.HttpContext.RequestServices.GetRequiredService<GitHubLogins>()
                        .Reject(BrowserLoginCorrelation.VerifiedRequest(context.HttpContext));
                    await LoginPage.WriteAsync(context.HttpContext, "GitHub connection needs attention",
                        "The GitHub App and your account must both have access to the requested repository. Install or configure the App on GitHub, then return to DigitalBrain and reconnect.", 400).ConfigureAwait(false);
                },
                OnAccessDenied = async context =>
                {
                    context.HandleResponse();
                    context.HttpContext.RequestServices.GetRequiredService<GitHubLogins>()
                        .Reject(BrowserLoginCorrelation.VerifiedRequest(context.HttpContext));
                    await LoginPage.WriteAsync(context.HttpContext, "GitHub connection cancelled",
                        "Your saved behavior remains available in DigitalBrain.", 200).ConfigureAwait(false);
                },
            };
        });
    }
}
