using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Guards the unauthenticated browser endpoints under <c>/api/ui</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>CSRF defense:</b> mutating requests must carry
/// <c>X-Requested-With: QATrack</c>. Browsers cannot attach custom headers to
/// cross-origin requests without a CORS preflight, and this app never grants
/// CORS, so a malicious page cannot forge board changes.</item>
/// <item><b>Audit integrity:</b> requests carrying AI credentials
/// (<c>X-API-Key</c> / <c>X-Agent-Identity</c>) are rejected here and pointed
/// at <c>/api/v1</c>, so agents cannot bypass AI tagging by using the UI API.</item>
/// <item><b>Actor:</b> marks the request as a human action, using the optional
/// self-reported <c>X-User-Display-Name</c> header.</item>
/// </list>
/// </remarks>
public sealed class UiRequestGuardMiddleware
{
    public const string PathPrefix = "/api/ui";
    /// <summary>Browser sign-in endpoints; guarded the same way (CSRF header, no agent headers).</summary>
    public const string AuthPathPrefix = "/api/auth";
    public const string RequestedWithHeader = "X-Requested-With";
    public const string RequestedWithValue = "QATrack";
    public const string DisplayNameHeader = "X-User-Display-Name";

    private readonly RequestDelegate _next;

    public UiRequestGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ActorContext actor)
    {
        if (!context.Request.Path.StartsWithSegments(PathPrefix) &&
            !context.Request.Path.StartsWithSegments(AuthPathPrefix))
        {
            await _next(context);
            return;
        }

        if (context.Request.Headers.ContainsKey(AgentHeaders.ApiKey) ||
            context.Request.Headers.ContainsKey(AgentHeaders.AgentIdentity))
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                "AI agents must use /api/v1",
                "Requests carrying X-API-Key or X-Agent-Identity must be sent to the /api/v1 endpoints so they are audited as AI actions.");
            return;
        }

        var isSafeMethod = HttpMethods.IsGet(context.Request.Method) ||
                           HttpMethods.IsHead(context.Request.Method) ||
                           HttpMethods.IsOptions(context.Request.Method);

        if (!isSafeMethod &&
            !string.Equals(context.Request.Headers[RequestedWithHeader], RequestedWithValue, StringComparison.Ordinal))
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden,
                "Missing anti-forgery header",
                $"Mutating requests to {PathPrefix} must include '{RequestedWithHeader}: {RequestedWithValue}'.");
            return;
        }

        actor.SetHuman(DecodeDisplayName(context.Request.Headers[DisplayNameHeader]));
        await _next(context);
    }

    /// <summary>
    /// The SPA percent-encodes the display name because HTTP header values
    /// must be ISO-8859-1 (names like "José" would otherwise be rejected by
    /// the browser). ActorContext sanitizes the decoded value afterwards.
    /// </summary>
    internal static string? DecodeDisplayName(string? raw) =>
        string.IsNullOrEmpty(raw) ? null : Uri.UnescapeDataString(raw);

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            options: null,
            contentType: "application/problem+json");
    }
}

/// <summary>Header names used by AI agents (spec 4.1).</summary>
public static class AgentHeaders
{
    public const string ApiKey = "X-API-Key";
    public const string AgentIdentity = "X-Agent-Identity";
}
