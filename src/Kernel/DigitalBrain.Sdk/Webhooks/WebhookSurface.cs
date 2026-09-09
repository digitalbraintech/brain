using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace DigitalBrain.Sdk.Webhooks;

/// <summary>Bounded raw-body transport for module-owned webhook authentication and ingestion.</summary>
public sealed class WebhookSurface : IHttpSurface
{
    private readonly WebhookDefinition _definition;
    private readonly IWebhookHandler _handler;
    private readonly TimeSpan _timeout;

    public WebhookSurface(WebhookDefinition definition, IWebhookHandler handler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(definition.Path) || !definition.Path.StartsWith('/')
            || definition.Path.Contains('?') || definition.Path.Contains('#') || definition.MaxBodyBytes <= 0)
        {
            throw new ArgumentException("A webhook requires an absolute route and positive body limit.", nameof(definition));
        }

        _timeout = definition.AcceptanceTimeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(8))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Webhook acceptance must finish within eight seconds.");
        }

        _definition = definition;
        _handler = handler;
    }

    public void Map(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals(new PathString(_definition.Path)))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            await AcceptAsync(context).ConfigureAwait(false);
        });
    }

    private async Task AcceptAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.Headers.Allow = "POST";
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        if (context.Request.ContentLength > _definition.MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        budget.CancelAfter(_timeout);
        // WaitAsync enforces the deadline even for a transport ignoring cancellation.
        // Such a pending read must retain its own buffer, never an array returned to a pool.
        var buffer = new byte[(int)Math.Min(_definition.MaxBodyBytes + 1L, 81920L)];
        try
        {
            using var body = new MemoryStream();
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length,
                    _definition.MaxBodyBytes - body.Length + 1)), budget.Token).AsTask().WaitAsync(budget.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (body.Length + read > _definition.MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                body.Write(buffer, 0, read);
            }

            var request = new WebhookRequest(body.ToArray(), context.Request.Headers.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Select(value => value ?? string.Empty).ToArray(),
                StringComparer.OrdinalIgnoreCase));
            var acceptance = await _handler.HandleAsync(request, budget.Token).WaitAsync(budget.Token).ConfigureAwait(false);
            context.Response.StatusCode = acceptance switch
            {
                WebhookAcceptance.Accepted => StatusCodes.Status202Accepted,
                WebhookAcceptance.Duplicate => StatusCodes.Status200OK,
                WebhookAcceptance.Ignored => StatusCodes.Status204NoContent,
                WebhookAcceptance.BadRequest => StatusCodes.Status400BadRequest,
                WebhookAcceptance.Unauthorized => StatusCodes.Status401Unauthorized,
                WebhookAcceptance.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
        }
        catch (BadHttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        catch (Exception)
        {
            // Payloads, credentials and provider exceptions must never become an HTTP response.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    }
}
