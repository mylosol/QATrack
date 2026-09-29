namespace KanbanBoard.Api.Services;

/// <summary>
/// Describes who is performing the current request. Populated once per request
/// by the actor middleware and consumed by the services when stamping audit
/// fields (LastModifiedBy, AiModified, WorkItemHistory).
/// </summary>
public interface IActorContext
{
    /// <summary>Name written to LastModifiedBy / History.Author.</summary>
    string DisplayName { get; }

    /// <summary>True when the request came through the secured AI agent API.</summary>
    bool IsAi { get; }

    /// <summary>The validated X-Agent-Identity header for AI requests; otherwise null.</summary>
    string? AgentIdentity { get; }
}

/// <summary>
/// Mutable, request-scoped implementation of <see cref="IActorContext"/>.
/// Defaults to an anonymous human browser user.
/// </summary>
public sealed class ActorContext : IActorContext
{
    /// <summary>Name used for browser edits when the user has not supplied one.</summary>
    public const string DefaultHumanName = "Web UI User";

    public string DisplayName { get; private set; } = DefaultHumanName;

    public bool IsAi { get; private set; }

    public string? AgentIdentity { get; private set; }

    /// <summary>Marks the request as a human (browser) action.</summary>
    /// <param name="displayName">Optional self-reported name; sanitized, falls back to the default.</param>
    public void SetHuman(string? displayName)
    {
        IsAi = false;
        AgentIdentity = null;
        DisplayName = TextSanitizer.SingleLine(displayName, 64) ?? DefaultHumanName;
    }

    /// <summary>Marks the request as an authenticated AI agent action.</summary>
    /// <param name="agentIdentity">Already validated X-Agent-Identity value.</param>
    public void SetAiAgent(string agentIdentity)
    {
        if (string.IsNullOrWhiteSpace(agentIdentity))
        {
            throw new ArgumentException("Agent identity is required.", nameof(agentIdentity));
        }

        IsAi = true;
        AgentIdentity = agentIdentity;
        DisplayName = agentIdentity;
    }
}
