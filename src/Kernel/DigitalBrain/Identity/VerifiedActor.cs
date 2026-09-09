using DigitalBrain.Abstractions;
using Orleans.Runtime;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Core;

// Ambient verified principal for the current authenticated turn. Uses Orleans
// RequestContext so it propagates Chat → Agent grain calls (AsyncLocal does not).
// Chat SendMessage.Actor enters here and propagates over grain calls.
public static class VerifiedActor
{
    private const string PrincipalKey = "db.verified-actor.principal";
    private const string UsernameKey = "db.verified-actor.username";

    public static ActorContext? Current
    {
        get
        {
            if (RequestContext.Get(PrincipalKey) is not Guid principalGuid
                || principalGuid == Guid.Empty
                || RequestContext.Get(UsernameKey) is not string username
                || string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            return new ActorContext(new PrincipalId(principalGuid), username);
        }
    }

    public static IDisposable Enter(ActorContext? actor)
    {
        var previousPrincipal = RequestContext.Get(PrincipalKey);
        var previousUsername = RequestContext.Get(UsernameKey);

        if (actor is null)
        {
            RequestContext.Remove(PrincipalKey);
            RequestContext.Remove(UsernameKey);
        }
        else
        {
            RequestContext.Set(PrincipalKey, actor.PrincipalId.Value);
            RequestContext.Set(UsernameKey, actor.Username);
        }

        return new Restore(previousPrincipal, previousUsername);
    }

    private sealed class Restore(object? previousPrincipal, object? previousUsername) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RestoreKey(PrincipalKey, previousPrincipal);
            RestoreKey(UsernameKey, previousUsername);
        }

        private static void RestoreKey(string key, object? previous)
        {
            if (previous is null)
            {
                RequestContext.Remove(key);
                return;
            }

            RequestContext.Set(key, previous);
        }
    }
}
