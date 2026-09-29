namespace KanbanBoard.Api.Middleware;

/// <summary>
/// Shared access password for the browser board, bound from the
/// <c>AccessControl</c> configuration section (normally written by
/// <c>deploy-iis.ps1 -SharedPassword</c> / <c>-Action SetPassword</c>).
/// </summary>
/// <remarks>
/// When <see cref="SharedPasswordHash"/> is empty the board is open, exactly as
/// before 1.3.0. The AI agent API (<c>/api/v1</c>) is never affected: it keeps
/// using its own API key.
/// </remarks>
public sealed class AccessControlOptions
{
    public const string SectionName = "AccessControl";

    /// <summary>
    /// PBKDF2 hash of the shared password
    /// (<c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt b64&gt;$&lt;hash b64&gt;</c>).
    /// Plain-text passwords are never stored.
    /// </summary>
    public string SharedPasswordHash { get; set; } = string.Empty;

    /// <summary>How long a browser stays signed in (sliding).</summary>
    public int SessionDays { get; set; } = 30;

    /// <summary>Sign-in attempts allowed per client IP per minute (brute-force brake).</summary>
    public int LoginAttemptsPerMinute { get; set; } = 5;

    /// <summary>
    /// Where the cookie-encryption keys are persisted (relative to the content
    /// root). Persisting them means app-pool recycles do not sign users out.
    /// </summary>
    public string KeyDirectory { get; set; } = "App_Data/keys";

    /// <summary>True when a password has been configured (the board is locked).</summary>
    public bool IsRequired => !string.IsNullOrWhiteSpace(SharedPasswordHash);
}

/// <summary>Constants shared by the cookie scheme, controller and middleware.</summary>
public static class AccessControlDefaults
{
    public const string Scheme = "QATrackAccess";
    public const string CookieName = "QATrack.Access";
    public const string PasswordVersionClaim = "qatrack:pwv";
    public const string LoginRateLimitPolicy = "login";
}
