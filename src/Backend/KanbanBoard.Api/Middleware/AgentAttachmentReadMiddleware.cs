using KanbanBoard.Api.Services;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Lets AI agents open pictures pasted into descriptions and comments (1.16.1).
/// </summary>
/// <remarks>
/// Pasted pictures are Markdown links to <c>api/ui/attachments/{id}</c>, the
/// board's own address, which needs a board sign-in and refuses agent headers.
/// A GET of exactly that path with a valid <c>X-API-Key</c> is answered here,
/// before those checks, so an agent can follow the link it reads. Read-only:
/// every other <c>/api/ui</c> request is still refused to agents, and a wrong
/// key is a 401 (never a fallback to the board's sign-in).
/// </remarks>
public sealed class AgentAttachmentReadMiddleware
{
    private const string Prefix = "/api/ui/attachments/";
    private readonly RequestDelegate _next;

    public AgentAttachmentReadMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<AiAgentApiOptions> options, AttachmentService attachments)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) ||
            !request.Headers.ContainsKey(AgentHeaders.ApiKey) ||
            request.Path.Value is not { } path ||
            !path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(path[Prefix.Length..], "D", out var id))
        {
            await _next(context);
            return;
        }

        var settings = options.CurrentValue;
        var supplied = request.Headers[AgentHeaders.ApiKey].ToString();
        if (!settings.IsConfigured || !ApiKeyAuthenticationMiddleware.KeysMatch(supplied, settings.ApiKey.Trim()))
        {
            context.Response.Headers.WWWAuthenticate = $"ApiKey header=\"{AgentHeaders.ApiKey}\"";
            await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status401Unauthorized,
                "Unauthorized", $"A valid '{AgentHeaders.ApiKey}' header is required to open this picture.");
            return;
        }

        var attachment = await attachments.FindAsync(id, context.RequestAborted);
        if (attachment is null)
        {
            await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status404NotFound,
                "Not Found", $"Attachment {id} was not found.");
            return;
        }

        context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        context.Response.ContentType = attachment.ContentType;
        context.Response.ContentLength = attachment.Content.Length;
        if (HttpMethods.IsGet(request.Method))
        {
            await context.Response.Body.WriteAsync(attachment.Content, context.RequestAborted);
        }
    }
}
