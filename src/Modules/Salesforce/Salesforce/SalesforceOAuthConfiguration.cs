using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Salesforce;

// Deliberately not an options record: secrets have no public getters or diagnostic formatting.
internal sealed class SalesforceOAuthConfiguration
{
    internal const string Section = SalesforceModule.OAuthConfigurationRoot;
    private readonly string _clientId;
    private readonly string _clientSecret;

    internal SalesforceOAuthConfiguration(IConfiguration configuration)
    {
        _clientId = Required(configuration, "ConsumerKey");
        _clientSecret = Required(configuration, "ConsumerSecret");
        var origin = configuration[$"{Section}:PublicOrigin"];
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException("Salesforce OAuth PublicOrigin must be an HTTPS origin, or loopback HTTP for local development.");
        }
        PublicOrigin = uri;
        Owner = new OwnerId(configuration[DigitalBrainNames.Owner] ?? DigitalBrainNames.DefaultOwner);
    }

    internal Uri PublicOrigin { get; }
    internal OwnerId Owner { get; }
    internal static Uri AuthorizationEndpoint => new("https://login.salesforce.com/services/oauth2/authorize");
    internal static Uri TokenEndpoint => new("https://login.salesforce.com/services/oauth2/token");

    internal void Configure(OAuthOptions options)
    {
        options.ClientId = _clientId;
        options.ClientSecret = _clientSecret;
        options.AuthorizationEndpoint = AuthorizationEndpoint.AbsoluteUri;
        options.TokenEndpoint = TokenEndpoint.AbsoluteUri;
    }

    internal HttpRequestMessage RefreshRequest(string refreshToken)
        => new(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["refresh_token"] = refreshToken,
            }),
        };

    private static string Required(IConfiguration configuration, string name)
    {
        var value = configuration[$"{Section}:{name}"];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new InvalidOperationException($"Salesforce OAuth {name} is required. Supply the Salesforce consumer credentials through Aspire parameters.");
        }
        return value;
    }
}
