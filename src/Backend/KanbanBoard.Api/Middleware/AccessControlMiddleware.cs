using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Requires a signed-in browser session for all board data (<c>/api/ui</c>)
/// when a shared password is configured.
/// </summary>
/// <remarks>
/// The SPA shell and bundles stay public - they contain no board data - so
/// the page can render its sign-in dialog. <c>/api/v1</c> (API key),
/// <c>/api/version</c>, <c>/api/auth/*</c> and the API docs are not gated.
/// The setting is read live, so setting or removing the password takes
/// effect without a restart.
/// </remarks>
public sealed class AccessControlMiddleware
{
    private readonly RequestDelegate _next;

    public AccessControlMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<AccessControlOptions> options)
    {
        if (!context.Request.Path.StartsWithSegments(UiRequestGuardMiddleware.PathPrefix) ||
            !options.CurrentValue.IsRequired ||
            context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status401Unauthorized,
            "Sign-in required", "Enter the shared QATrack password to use the board.");
    }
}

/// <summary>
/// Validates each session cookie against the CURRENT password: a cookie issued
/// before the password was changed (different fingerprint) is rejected.
/// </summary>
public static class AccessSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AccessControlOptions>>().CurrentValue;
        if (!options.IsRequired)
        {
            return;
        }

        var issuedFor = context.Principal?.FindFirst(AccessControlDefaults.PasswordVersionClaim)?.Value;
        if (!string.Equals(issuedFor, SharedPasswordHasher.Fingerprint(options.SharedPasswordHash), StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AccessControlDefaults.Scheme);
        }
    }
}
