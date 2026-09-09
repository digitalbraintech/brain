using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Sdk;

// One implementation per provider. TConnection identifies the owner's current connection
// including an account revision; when it changes, cached sessions and prepared tools are stale.
// This credential store is not an authorization boundary. Specialist clients additionally bind
// their identity to a verified principal and validate access through ForHttp's authorize callback.
public interface IMcpCredentials<TConnection>
    where TConnection : notnull
{
    // Throws McpAuthenticationRequiredException when the owner has no connection, so no
    // unauthenticated request ever leaves the kernel.
    TConnection Connection(OwnerId owner);

    Task<string> AccessTokenAsync(OwnerId owner, TConnection connection, bool refresh, CancellationToken cancellationToken);

    // The server refused the connection's credentials twice; the provider decides what to forget.
    Task RejectAsync(OwnerId owner, TConnection connection, CancellationToken cancellationToken);
}
