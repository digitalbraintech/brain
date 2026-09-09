using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Sdk;

// Only fixed application strings are rendered. Never reflect an OAuth response or URL.
public static class LoginPage
{
    public static Task WriteAsync(HttpContext context, string title, string message, int status)
    {
        ArgumentNullException.ThrowIfNull(context);
        PrivateHeaders(context);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(
            $"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>{title}</title><body style=\"font:18px system-ui;max-width:640px;margin:12vh auto;padding:24px\"><h1>{title}</h1><p>{message}</p></body></html>",
            context.RequestAborted);
    }

    public static void PrivateHeaders(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";
    }
}
