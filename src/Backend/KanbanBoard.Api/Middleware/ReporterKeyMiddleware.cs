using KanbanBoard.Api.Services;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Authenticates in-app issue reports (<c>/api/report</c>, 1.12.0) with the
/// <c>X-Reporter-Key</c> and records them as human reports, not AI actions.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Fails closed: 503 when no reporter key is configured, or when it is
/// the same as the AI agent key (that would let a leaked reporter key pass
/// for an agent and vice versa).</item>
/// <item>Requests carrying AI agent headers are refused and pointed at
/// <c>/api/v1</c>, so agents can't file work as "human".</item>
/// <item>Constant-time key comparison; the key is never logged.</item>
/// </list>
/// </remarks>
public sealed class ReporterKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ReporterKeyMiddleware> _logger;

    public ReporterKeyMiddleware(RequestDelegate next, ILogger<ReporterKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptionsMonitor<IssueReportingOptions> options,
        IOptionsMonitor<AiAgentApiOptions> agentOptions,
        ActorContext actor)
    {
        if (!context.Request.Path.StartsWithSegments(IssueReportingOptions.PathPrefix))
        {
            await _next(context);
            return;
        }

        var settings = options.CurrentValue;
        var agentKey = agentOptions.CurrentValue.ApiKey.Trim();
        if (!settings.IsConfigured ||
            (agentKey.Length > 0 && ApiKeyAuthenticationMiddleware.KeysMatch(settings.ApiKey.Trim(), agentKey)))
        {
            _logger.LogError("Rejected {Path}: IssueReporting:ApiKey is not configured, or equals the AI agent key.", context.Request.Path);
            await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
                "In-app issue reporting is not configured",
                "The server administrator has not configured a reporter key.");
            return;
        }

        if (context.Request.Headers.ContainsKey(AgentHeaders.ApiKey) ||
            context.Request.Headers.ContainsKey(AgentHeaders.AgentIdentity))
        {
            await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                "AI agents must use /api/v1",
                "Requests carrying X-API-Key or X-Agent-Identity must be sent to the /api/v1 endpoints so they are audited as AI actions.");
            return;
        }

        var supplied = context.Request.Headers[IssueReportingOptions.KeyHeader].ToString();
        if (string.IsNullOrEmpty(supplied) || !ApiKeyAuthenticationMiddleware.KeysMatch(supplied, settings.ApiKey.Trim()))
        {
            _logger.LogWarning("Rejected {Method} {Path} from {Remote}: missing or invalid {Header}.",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress, IssueReportingOptions.KeyHeader);
            context.Response.Headers.WWWAuthenticate = $"ApiKey header=\"{IssueReportingOptions.KeyHeader}\"";
            await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status401Unauthorized,
                "Unauthorized", $"A valid '{IssueReportingOptions.KeyHeader}' header is required.");
            return;
        }

        // A human report; the reporter's name (if any) is applied by the service.
        actor.SetReporter(null);
        await _next(context);
    }
}
