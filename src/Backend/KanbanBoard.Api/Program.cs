using KanbanBoard.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Apply forward-only migrations + WAL before the first request is served.
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync(app.Configuration.GetValue<bool>("Database:SeedSampleData"));
}

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
