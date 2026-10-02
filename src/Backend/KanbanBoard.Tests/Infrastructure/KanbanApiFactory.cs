using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace KanbanBoard.Tests.Infrastructure;

/// <summary>
/// Boots the real application pipeline in-memory against a private temp
/// SQLite database, so integration tests exercise middleware, routing,
/// model binding, EF and migrations exactly as production does.
/// </summary>
public class KanbanApiFactory : WebApplicationFactory<Program>
{
    /// <summary>API key configured for the test host.</summary>
    public const string TestApiKey = "test-api-key-0123456789abcdef";

    /// <summary>Reporter key configured for the test host (in-app issue reports).</summary>
    public const string TestReporterKey = "test-reporter-key-0123456789abcdef";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "qatrack-tests", "api-" + Guid.NewGuid().ToString("N"));

    public KanbanApiFactory()
    {
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "kanban.db");
    }

    /// <summary>Absolute path of the database used by this host.</summary>
    public string DatabasePath { get; }

    /// <summary>JSON options matching the server (camelCase + string enums).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Kanban", $"Data Source={DatabasePath};Cache=Shared;Mode=ReadWriteCreate;");
        builder.UseSetting("Database:SeedSampleData", "false");
        builder.UseSetting("AiAgentApi:ApiKey", TestApiKey);
        builder.UseSetting("IssueReporting:ApiKey", TestReporterKey);
        // Keep cookie-encryption keys out of the repo's App_Data.
        builder.UseSetting("AccessControl:KeyDirectory", Path.Combine(_directory, "keys"));
    }

    /// <summary>Client pre-configured like the browser SPA (anti-forgery header).</summary>
    public HttpClient CreateUiClient(string? displayName = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "QATrack");
        if (displayName is not null)
        {
            client.DefaultRequestHeaders.Add("X-User-Display-Name", displayName);
        }

        return client;
    }

    /// <summary>Client pre-configured like an AI agent.</summary>
    public HttpClient CreateAgentClient(string identity = "Claude-Code-Agent-v1", string apiKey = TestApiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        client.DefaultRequestHeaders.Add("X-Agent-Identity", identity);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Client configured like a program under test reporting an issue.</summary>
    public HttpClient CreateReporterClient(string reporterKey = TestReporterKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Reporter-Key", reporterKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
