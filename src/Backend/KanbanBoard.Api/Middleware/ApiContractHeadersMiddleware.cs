using KanbanBoard.Api.Services;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Stamps every <c>/api/v1</c> response - including 401/400/404/500 - with
/// <c>X-API-Schema-Version</c> and <c>Link: &lt;/api/openapi.json&gt;; rel="service-desc"</c>,
/// so an agent notices a contract change on whatever call it happens to make.
/// </summary>
public sealed class ApiContractHeadersMiddleware
{
    public const string PathPrefix = "/api/v1";

    private readonly RequestDelegate _next;

    public ApiContractHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context, ApiContract contract)
    {
        if (context.Request.Path.StartsWithSegments(PathPrefix))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[ApiContract.SchemaVersionHeader] = contract.SchemaVersion;
                context.Response.Headers.Append("Link", ApiContract.LinkHeader(context.Request.PathBase));
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}
