using System.Text.RegularExpressions;
using KanbanBoard.Api.Services;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Middleware;

/// <summary>
/// AI identity interceptor (spec 4.1). Runs after API-key authentication on
/// <c>/api/v1</c>, validates <c>X-Agent-Identity</c> and marks the request's
/// <see cref="ActorContext"/> as an AI agent. The services then stamp
/// AiModified / AiAgentIdentity and write IsAiAction history rows.
/// </summary>
public sealed partial class AgentIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public AgentIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Letters, digits, space and a small set of punctuation common in agent
    /// names ("Claude-Code-Agent-v1", "codex/fixer@2.1 (ci)"). Anything else -
    /// including control characters and markup - is rejected, because the
    /// value is displayed in the UI and persisted in the audit trail.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._\-:/()@+#]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedIdentity();

    public async Task InvokeAsync(HttpContext context, ActorContext actor, IOptionsMonitor<AiAgentApiOptions> options)
    {
        if (!context.Request.Path.StartsWithSegments(ApiKeyAuthenticationMiddleware.PathPrefix))
        {
            await _next(context);
            return;
        }

        var raw = context.Request.Headers[AgentHeaders.AgentIdentity].ToString();
        var error = Validate(raw, options.CurrentValue.MaxAgentIdentityLength, out var identity);
        if (error is not null)
        {
            await ApiKeyAuthenticationMiddleware.WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                $"Invalid {AgentHeaders.AgentIdentity} header", error);
            return;
        }

        actor.SetAiAgent(identity!);
        await _next(context);
    }

    /// <summary>
    /// Validates an agent identity. Returns an error message, or null when
    /// valid (with the trimmed identity in <paramref name="identity"/>).
    /// </summary>
    internal static string? Validate(string? raw, int maxLength, out string? identity)
    {
        identity = raw?.Trim();
        if (string.IsNullOrEmpty(identity))
        {
            return $"The '{AgentHeaders.AgentIdentity}' header is required and must name the calling agent (e.g. 'Claude-Code-Agent-v1').";
        }

        if (identity.Length > maxLength)
        {
            return $"The '{AgentHeaders.AgentIdentity}' header must be at most {maxLength} characters.";
        }

        if (!AllowedIdentity().IsMatch(identity))
        {
            return $"The '{AgentHeaders.AgentIdentity}' header may only contain letters, digits, spaces and . _ - : / ( ) @ + #, and must start with a letter or digit.";
        }

        return null;
    }
}
