namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Adds hardening headers to every response. The SPA is served with a strict
/// Content-Security-Policy (no inline script, same-origin only); Swagger UI
/// under <c>/api/docs</c> gets a slightly relaxed policy because its bundled
/// page relies on inline styles/scripts.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>CSP for the board SPA and API responses.</summary>
    public const string StrictCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; " +
        "form-action 'self'; frame-ancestors 'none'";

    /// <summary>CSP for Swagger UI.</summary>
    public const string DocsCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] =
                context.Request.Path.StartsWithSegments("/api/docs") ? DocsCsp : StrictCsp;
            return Task.CompletedTask;
        });

        return _next(context);
    }
}
