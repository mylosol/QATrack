using KanbanBoard.Api.Services;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Stamps every <c>/api/v1</c> response - including 401/400/404/500 - with
/// <c>X-API-Schema-Version</c> and <c>Link: &lt;/api/openapi.json&gt;; rel="service-desc"</c>,
/// so an agent notices a contract change on whatever call it happens to make.
/// Since 1.18.0 every <c>/api/report</c> response does the same for the
/// reporting API (its own fingerprint, linking <c>/api/openapi-report.json</c>).
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
        else if (context.Request.Path.StartsWithSegments(IssueReportingOptions.PathPrefix))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[ApiContract.SchemaVersionHeader] = contract.ReportSchemaVersion;
                context.Response.Headers.Append("Link", ApiContract.ReportLinkHeader(context.Request.PathBase));
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}
