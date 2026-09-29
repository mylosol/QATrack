using System.Text.Json.Serialization;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Request limits. Descriptions are capped at 1 MB of text, so 4 MB bodies are
// plenty and anything larger is rejected before model binding.
// ---------------------------------------------------------------------------
const long maxRequestBodyBytes = 4 * 1024 * 1024;
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

// AI agent API settings (X-API-Key pre-shared secret, identity rules).
builder.Services.Configure<AiAgentApiOptions>(builder.Configuration.GetSection(AiAgentApiOptions.SectionName));

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

var app = builder.Build();

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

app.UseRouting();

// /api/v1 (AI agents): authenticate the key first, then capture the identity.
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseMiddleware<AgentIdentityMiddleware>();
// /api/ui (browser): anti-forgery header + human actor.
app.UseMiddleware<UiRequestGuardMiddleware>();

app.MapKanbanOpenApi();
app.MapControllers();

// Unknown API routes must return a JSON 404, never the SPA shell.
app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found"));
// Client-side routes fall back to the SPA.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
