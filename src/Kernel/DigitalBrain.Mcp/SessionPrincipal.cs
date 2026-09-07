using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Mcp;

// The caller's Session neuron name. Over HTTP it comes from ?principal= or the
// X-DigitalBrain-Principal header; otherwise "claude". One name, one Session, across connections.
public sealed class SessionPrincipal
{
    public const string Default = "claude";
    public const string QueryKey = "principal";
    public const string HeaderName = "X-DigitalBrain-Principal";

    public SessionPrincipal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public string Name { get; }

    public static SessionPrincipal FromHttp(HttpContext? context)
    {
        var name = context?.Request.Query[QueryKey].FirstOrDefault()
            ?? context?.Request.Headers[HeaderName].FirstOrDefault();
        return new(string.IsNullOrWhiteSpace(name) ? Default : name);
    }
}
