using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Kernel;

internal static class OwnerCommandsHttpMaps
{
    public static IEndpointRouteBuilder MapOwnerCommands(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.OwnerCommandsPath,
            static async Task (
                HttpContext http,
                OwnerCommandRequest request,
                IDigitalBrain brain,
                IWorkspaceInject inject,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var actor = HttpActor.Current;

                if (string.IsNullOrWhiteSpace(request.Kind))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (string.Equals(request.Kind, HttpSurfacePaths.KindChatSend, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.ChatName) || string.IsNullOrWhiteSpace(request.Text))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    using (VerifiedActor.Enter(actor))
                    {
                        await SseResponse.WriteAsync(
                            http.Response,
                            ChatTurnStream.SendAsync(
                                brain,
                                inject,
                                request.ChatName,
                                request.Text,
                                actor,
                                cancellationToken),
                            cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }

                if (string.Equals(request.Kind, HttpSurfacePaths.KindChatCancelTurn, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.ChatName)
                        || !TryParseCommandId(request.CommandId, out var cancelCommandId)
                        || string.IsNullOrWhiteSpace(request.TurnId)
                        || !Guid.TryParse(request.TurnId, out var turnGuid)
                        || turnGuid == Guid.Empty)
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    if (!TryPrincipalResource(actor.PrincipalId, request.ChatName, out var chatInstance))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await brain.Get<IChat>(chatInstance)
                        .SendAsync(new CancelTurn(cancelCommandId, new TurnId(turnGuid), actor), cancellationToken)
                        .ConfigureAwait(false);
                    http.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                if (string.Equals(request.Kind, HttpSurfacePaths.KindSurfaceOpen, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(request.SurfaceName)
                        || string.IsNullOrWhiteSpace(request.SurfaceKey)
                        || string.IsNullOrWhiteSpace(request.Title))
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    string surfaceInstance;
                    try
                    {
                        surfaceInstance = PrincipalSurface.InstanceName(actor.PrincipalId, request.SurfaceName);
                    }
                    catch (ArgumentException)
                    {
                        http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    await brain.Get<IUIRenderer>(surfaceInstance)
                        .SendAsync(
                            new OpenSurface(CommandId.New(), request.SurfaceKey, request.Title),
                            cancellationToken)
                        .ConfigureAwait(false);
                    http.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                http.Response.StatusCode = StatusCodes.Status400BadRequest;
            });

        return endpoints;
    }

    private static bool TryPrincipalResource(PrincipalId principal, string localName, out string instanceName)
    {
        try
        {
            instanceName = PrincipalScoped.InstanceName(principal, localName);
            return true;
        }
        catch (ArgumentException)
        {
            instanceName = "";
            return false;
        }
    }

    private static bool TryParseCommandId(string? value, out CommandId commandId)
    {
        commandId = default;
        if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            return false;
        }

        commandId = new CommandId(id);
        return true;
    }

}
