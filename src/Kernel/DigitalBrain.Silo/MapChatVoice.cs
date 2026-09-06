using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Kernel;

// Upload voice → local Whisper → durable chat turn (server-orchestrated).
internal static class ChatVoiceHttpMaps
{
    public const long MaxUploadBytes = VoiceUploadLimits.MaxBytes;

    public static IEndpointRouteBuilder MapChatVoice(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.ChatVoicePath,
            static async Task (
                HttpContext http,
                string chatName,
                IDigitalBrain brain,
                IWorkspaceInject inject,
                IAudioTranscriptionService transcription,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(transcription);
                cancellationToken.ThrowIfCancellationRequested();

                var actor = HttpActor.Current;

                if (string.IsNullOrWhiteSpace(chatName)
                    || !TryPrincipalResource(actor.PrincipalId, chatName, out _))
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!transcription.IsReady)
                {
                    http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await http.Response.WriteAsJsonAsync(
                        new
                        {
                            error = transcription.ErrorMessage
                                ?? "Whisper is not ready. Retry after the model finishes loading.",
                        },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!http.Request.HasFormContentType)
                {
                    http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    return;
                }

                var form = await http.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
                var file = form.Files.GetFile("audio") ?? form.Files.FirstOrDefault();
                if (file is null || file.Length <= 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (file.Length > MaxUploadBytes)
                {
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "voice.wav" : file.FileName;
                string text;
                try
                {
                    await using var stream = file.OpenReadStream();
                    text = await transcription
                        .TranscribeAsync(stream, fileName, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                    await http.Response.WriteAsJsonAsync(
                        new { error = ex.Message },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                    await http.Response.WriteAsJsonAsync(
                        new { error = "Transcription produced empty text." },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                using (VerifiedActor.Enter(actor))
                {
                    await SseResponse.WriteAsync(
                        http.Response,
                        ChatTurnStream.SendAsync(brain, inject, chatName, text.Trim(), actor, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
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

}
