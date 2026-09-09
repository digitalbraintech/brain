namespace DigitalBrain.Sdk;

// Credentials never travel with the endpoint: the bearer handler resolves them per request,
// so an endpoint is safe to log, compare and hand to a module.
public sealed class McpEndpoint
{
    public McpEndpoint(string name, Uri uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new ArgumentException(
                $"MCP endpoint '{name}' must be an absolute HTTPS URI (or loopback HTTP) without user info, query or fragment.",
                nameof(uri));
        }

        Name = name;
        Uri = uri;
    }

    public string Name { get; }

    public Uri Uri { get; }

    // Streamable HTTP addresses every request to the one endpoint URI; anything else is a
    // client following a link it was never given.
    internal bool Accepts(Uri request)
        => request.IsAbsoluteUri
            && request.UserInfo.Length == 0
            && string.Equals(
                request.GetLeftPart(UriPartial.Path),
                Uri.GetLeftPart(UriPartial.Path),
                StringComparison.Ordinal);

    public override string ToString() => $"{Name}: {Uri}";
}
