namespace KanbanBoard.Api.Middleware;

/// <summary>
/// In-app "Report an issue" from the programs under test (1.12.0), bound from
/// the <c>IssueReporting</c> configuration section.
/// </summary>
/// <remarks>
/// The reporter key ships inside those programs, so treat it as semi-public:
/// it can only file new reports and upload screenshots (never read or change
/// the board), and requests are rate-limited per IP address.
/// </remarks>
public sealed class IssueReportingOptions
{
    public const string SectionName = "IssueReporting";

    /// <summary>Header carrying the reporter key.</summary>
    public const string KeyHeader = "X-Reporter-Key";

    /// <summary>All reporter endpoints live under this prefix.</summary>
    public const string PathPrefix = "/api/report";

    /// <summary>Rate limiter policy shared by the reporter endpoints.</summary>
    public const string RateLimitPolicy = "report";

    /// <summary>
    /// The reporter key. When empty (or too short) in-app reporting is off and
    /// <c>/api/report</c> answers 503.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Requests (reports + screenshot uploads) allowed per minute per IP address.</summary>
    public int RequestsPerMinute { get; set; } = 20;

    /// <summary>Tag added to every in-app report so they are easy to find on the board.</summary>
    public string Tag { get; set; } = "in-app-report";

    /// <summary>True when a usable key has been configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Trim().Length >= AiAgentApiOptions.MinimumKeyLength;
}
