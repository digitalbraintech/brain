using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orleans.Runtime;

namespace DigitalBrain.Abstractions.Scripting;

internal static class ApplicationWorkerCapabilityContext
{
    private const string ContextKey = "db.application-worker-capability";
    internal static string? Current => RequestContext.Get(ContextKey) as string;
    internal static IDisposable Enter(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        var previous = RequestContext.Get(ContextKey);
        RequestContext.Set(ContextKey, capability);
        return new Restore(previous);
    }
    private sealed class Restore(object? previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous is null) { RequestContext.Remove(ContextKey); }
            else { RequestContext.Set(ContextKey, previous); }
        }
    }
}

internal sealed class ApplicationWorkerCapabilityAuthority
{
    internal static ApplicationWorkerCapabilityAuthority Process { get; } = new();
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);
    private readonly object sync = new();
    internal string Issue(string owner, Guid principal, string applicationKey, string revision, DateTimeOffset expiresAt)
    {
        var grant = new Grant(owner, principal, applicationKey, revision, expiresAt.ToUnixTimeMilliseconds(), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(grant));
        var capability = $"{payload}.{Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}";
        lock (sync) { active.Add(capability); }
        return capability;
    }
    internal void Validate(string? capability, string owner, Guid principal, string applicationKey, string revision, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(capability)) { throw Unauthorized(); }
        var parts = capability.Split('.', 2);
        if (parts.Length != 2) { throw Unauthorized(); }
        byte[] supplied;
        try { supplied = Convert.FromBase64String(parts[1]); } catch (FormatException) { throw Unauthorized(); }
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0])), supplied)) { throw Unauthorized(); }
        Grant? grant;
        try { grant = JsonSerializer.Deserialize<Grant>(Convert.FromBase64String(parts[0])); }
        catch (Exception error) when (error is FormatException or JsonException) { throw Unauthorized(); }
        lock (sync) { if (!active.Contains(capability)) { throw Unauthorized(); } }
        if (grant is null || grant.ExpiresAt <= now.ToUnixTimeMilliseconds() || !StringComparer.Ordinal.Equals(grant.Owner, owner) || grant.Principal != principal || !StringComparer.Ordinal.Equals(grant.ApplicationKey, applicationKey) || !StringComparer.Ordinal.Equals(grant.Revision, revision)) { throw Unauthorized(); }
    }
    internal void Revoke(string capability) { lock (sync) { active.Remove(capability); } }
    private static Exception Unauthorized() => new Neurons.NeuronAuthorizationException("A valid server-issued worker capability is required for application execution.");
    private sealed record Grant(string Owner, Guid Principal, string ApplicationKey, string Revision, long ExpiresAt, string Nonce);
}
