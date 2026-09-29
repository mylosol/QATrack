using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Enforces the pre-shared <c>X-API-Key</c> on every <c>/api/v1</c> request (spec 4.1).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Comparison is constant-time over SHA-256 digests, so neither content
/// nor length of the secret leaks through response timing.</item>
/// <item>The supplied key is never logged.</item>
/// <item>If no usable key is configured the API fails closed with 503 rather
/// than silently accepting anonymous agents.</item>
/// </list>
/// </remarks>
public sealed class ApiKeyAuthenticationMiddleware
{
    public const string PathPrefix = "/api/v1";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<AiAgentApiOptions> options)
    {
        if (!context.Request.Path.StartsWithSegments(PathPrefix))
        {
            await _next(context);
            return;
        }

        var settings = options.CurrentValue;
        if (!settings.IsConfigured)
        {
            _logger.LogError("Rejected {Path}: AiAgentApi:ApiKey is not configured (minimum {Min} characters).",
                context.Request.Path, AiAgentApiOptions.MinimumKeyLength);
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
                "AI agent API is not configured",
                "The server administrator has not configured an API key for the AI agent API.");
            return;
        }

        var supplied = context.Request.Headers[AgentHeaders.ApiKey].ToString();
        if (string.IsNullOrEmpty(supplied) || !KeysMatch(supplied, settings.ApiKey.Trim()))
        {
            _logger.LogWarning("Rejected {Method} {Path} from {Remote}: missing or invalid {Header}.",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress, AgentHeaders.ApiKey);
            context.Response.Headers.WWWAuthenticate = $"ApiKey header=\"{AgentHeaders.ApiKey}\"";
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized,
                "Unauthorized",
                $"A valid '{AgentHeaders.ApiKey}' header is required.");
            return;
        }

        await _next(context);
    }

    /// <summary>Constant-time comparison of two secrets of arbitrary length.</summary>
    internal static bool KeysMatch(string supplied, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    internal static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            options: null,
            contentType: "application/problem+json");
    }
}
