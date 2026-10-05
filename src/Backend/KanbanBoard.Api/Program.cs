using System.Text.Json.Serialization;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Request limits. Descriptions are capped at 1 MB of text and uploaded images
// at 5 MB, so 8 MB bodies are plenty; anything larger is rejected before
// model binding.
// ---------------------------------------------------------------------------
const long maxRequestBodyBytes = 8 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxRequestBodyBytes);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxRequestBodyBytes);

// ---------------------------------------------------------------------------
// Persistence: SQLite at App_Data/kanban.db, anchored to the content root so
// IIS working-directory quirks can never point us at a different file.
// ---------------------------------------------------------------------------
var connectionString = SqliteConnectionStringResolver.Resolve(
    builder.Configuration.GetConnectionString("Kanban"),
    builder.Environment.ContentRootPath);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<KanbanDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<DatabaseInitializer>();

// ---------------------------------------------------------------------------
// Application services. ActorContext is registered once and exposed through
// the read-only IActorContext interface to the services.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<ActorContext>();
builder.Services.AddScoped<IActorContext>(sp => sp.GetRequiredService<ActorContext>());
builder.Services.AddScoped<WorkItemService>();
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ProgramService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddSingleton<ApiContract>();
builder.Services.AddScoped<IssueReportService>();

// AI agent API settings (X-API-Key pre-shared secret, identity rules).
builder.Services.Configure<AiAgentApiOptions>(builder.Configuration.GetSection(AiAgentApiOptions.SectionName));

// In-app "Report an issue" from the programs under test (X-Reporter-Key, 1.12.0).
builder.Services.Configure<IssueReportingOptions>(builder.Configuration.GetSection(IssueReportingOptions.SectionName));
var reportingOptions = builder.Configuration.GetSection(IssueReportingOptions.SectionName).Get<IssueReportingOptions>()
                       ?? new IssueReportingOptions();

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Enums travel as their names ("Bug", "Active") for readable AI tool calls.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddKanbanOpenApi();

// ---------------------------------------------------------------------------
// Shared access password for the browser board (optional; see
// AccessControlOptions). Sessions are HttpOnly, SameSite=Strict cookies whose
// encryption keys persist in App_Data/keys (DPAPI-protected on Windows) so
// IIS app-pool recycles don't sign everyone out.
// ---------------------------------------------------------------------------
builder.Services.Configure<AccessControlOptions>(builder.Configuration.GetSection(AccessControlOptions.SectionName));
var accessOptions = builder.Configuration.GetSection(AccessControlOptions.SectionName).Get<AccessControlOptions>()
                    ?? new AccessControlOptions();

var keyDirectory = Path.IsPathRooted(accessOptions.KeyDirectory)
    ? accessOptions.KeyDirectory
    : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, accessOptions.KeyDirectory));
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("QATrack")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

builder.Services
    .AddAuthentication(AccessControlDefaults.Scheme)
    .AddCookie(AccessControlDefaults.Scheme, o =>
    {
        o.Cookie.Name = AccessControlDefaults.CookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        // Secure when the site is served over HTTPS; plain HTTP deployments still work.
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(Math.Max(1, accessOptions.SessionDays));
        o.SlidingExpiration = true;
        // API semantics: never redirect to a login page.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = AccessSessionValidator.ValidateAsync;
    });

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AccessControlDefaults.LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, accessOptions.LoginAttemptsPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    o.AddPolicy(IssueReportingOptions.RateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        "report:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, reportingOptions.RequestsPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    o.OnRejected = (ctx, _) =>
    {
        var isReport = ctx.HttpContext.Request.Path.StartsWithSegments(IssueReportingOptions.PathPrefix);
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        return new ValueTask(ApiKeyAuthenticationMiddleware.WriteProblemAsync(ctx.HttpContext,
            StatusCodes.Status429TooManyRequests,
            isReport ? "Too many reports" : "Too many sign-in attempts",
            "Wait a minute and try again."));
    };
});

var app = builder.Build();

if (!accessOptions.IsRequired)
{
    app.Logger.LogWarning("No shared access password is configured: anyone who can reach this site can use the board. Set one with 'deploy-iis.ps1 -Action SetPassword'.");
}
else if (!SharedPasswordHasher.IsWellFormed(accessOptions.SharedPasswordHash))
{
    // Fail closed: the board stays locked and no password can unlock it.
    app.Logger.LogError("AccessControl:SharedPasswordHash is malformed; nobody can sign in. Reset it with 'deploy-iis.ps1 -Action SetPassword'.");
}

// Apply forward-only migrations + WAL before the first request is served.
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync(app.Configuration.GetValue<bool>("Database:SeedSampleData"));
}

var aiApiOptions = app.Configuration.GetSection(AiAgentApiOptions.SectionName).Get<AiAgentApiOptions>();
if (aiApiOptions is null || !aiApiOptions.IsConfigured)
{
    // Fail closed but keep the board usable: /api/v1 answers 503 until a key is set.
    app.Logger.LogWarning("AiAgentApi:ApiKey is not configured; the AI agent API (/api/v1) is disabled.");
}

if (!reportingOptions.IsConfigured)
{
    app.Logger.LogWarning("IssueReporting:ApiKey is not configured; in-app issue reporting (/api/report) is disabled.");
}
else if (aiApiOptions is not null && string.Equals(aiApiOptions.ApiKey.Trim(), reportingOptions.ApiKey.Trim(), StringComparison.Ordinal))
{
    app.Logger.LogError("IssueReporting:ApiKey must differ from AiAgentApi:ApiKey; in-app issue reporting is disabled until it does.");
}

// ---------------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Static SPA assets from wwwroot (built by Vite). index.html is never cached so
// a redeploy is picked up immediately; hashed assets are cached aggressively.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var isHashedAsset = ctx.Context.Request.Path.StartsWithSegments("/assets");
        ctx.Context.Response.Headers.CacheControl = isHashedAsset
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});

// Swagger UI (/api/docs) must precede routing - see UseKanbanSwaggerUi.
app.UseKanbanSwaggerUi();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();

// /api/v1 (AI agents): contract headers on EVERY response (even 401s), so a
// changed X-API-Schema-Version is noticed on any call...
app.UseMiddleware<ApiContractHeadersMiddleware>();
// ...then authenticate the key, then capture the identity.
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseMiddleware<AgentIdentityMiddleware>();
// /api/report (in-app issue reports from the programs under test): reporter key, human actor.
app.UseMiddleware<ReporterKeyMiddleware>();
// /api/ui (browser): signed-in session when a shared password is configured...
app.UseMiddleware<AccessControlMiddleware>();
// ...plus anti-forgery header + human actor for /api/ui and /api/auth.
app.UseMiddleware<UiRequestGuardMiddleware>();

app.MapKanbanOpenApiSchema();

// Public, unauthenticated version probe for operators, monitoring and agents.
// It is also the source of truth for the SPA's "new version available" toast,
// so it must never be cached by the browser, a proxy or a CDN.
app.MapGet("/api/version", (HttpContext context, ApiContract contract) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            name = "QATrack",
            version = AppVersion.Current.Version,
            commit = AppVersion.Current.Commit,
            // Canonical build id compared by the SPA with the one baked into its bundle.
            build = AppVersion.Current.Build,
            informationalVersion = AppVersion.Current.InformationalVersion,
            // Fingerprint of the /api/v1 contract (see X-API-Schema-Version).
            apiSchemaVersion = contract.SchemaVersion,
        });
    })
    .ExcludeFromDescription();
app.MapControllers();

// Unknown API routes must return a JSON 404, never the SPA shell.
app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found"));
// Client-side routes fall back to the SPA.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
