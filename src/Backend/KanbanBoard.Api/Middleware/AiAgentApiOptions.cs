namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Settings for the secured AI agent API, bound from the <c>AiAgentApi</c>
/// configuration section (appsettings.json / appsettings.Production.json).
/// </summary>
public sealed class AiAgentApiOptions
{
    public const string SectionName = "AiAgentApi";

    /// <summary>Keys shorter than this are treated as "not configured".</summary>
    public const int MinimumKeyLength = 16;

    /// <summary>
    /// Pre-shared secret agents send in <c>X-API-Key</c>. When empty (or too
    /// short) the <c>/api/v1</c> surface is disabled and answers 503, while
    /// the browser board keeps working.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maximum accepted length of the <c>X-Agent-Identity</c> header.</summary>
    public int MaxAgentIdentityLength { get; set; } = 100;

    /// <summary>True when a usable key has been configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Trim().Length >= MinimumKeyLength;
}
