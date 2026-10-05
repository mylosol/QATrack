using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace KanbanBoard.Tests.Api;

/// <summary>
/// 1.5.0: signalling API contract changes to AI agents - X-API-Schema-Version,
/// the service-desc Link, ETag on the OpenAPI document and GET /api/v1/meta.
/// </summary>
public sealed class ApiContractTests : IClassFixture<KanbanApiFactory>
{
    private const string FingerprintPattern = "^[0-9a-f]{12}$";

    private readonly KanbanApiFactory _factory;

    public ApiContractTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private string LiveFingerprint() => _factory.Services.GetRequiredService<ApiContract>().SchemaVersion;

    private OpenApiDocument FreshDocument()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISwaggerProvider>().GetSwagger(OpenApiDocumentation.DocumentName);
    }

    [Theory]
    [InlineData("/api/v1/workitems", true)]   // 200
    [InlineData("/api/v1/workitems/999999", true)] // 404
    [InlineData("/api/v1/board", false)]      // 401 without a key
    public async Task EveryV1Response_CarriesTheSchemaVersionAndServiceDescLink(string path, bool withKey)
    {
        var client = withKey ? _factory.CreateAgentClient() : _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Matches(FingerprintPattern, response.Headers.GetValues(ApiContract.SchemaVersionHeader).Single());
        Assert.Equal("</api/openapi.json>; rel=\"service-desc\"", response.Headers.GetValues("Link").Single());
    }

    [Fact]
    public async Task BrowserApi_IsNotStamped()
    {
        var response = await _factory.CreateUiClient().GetAsync("/api/ui/board");
        Assert.False(response.Headers.Contains(ApiContract.SchemaVersionHeader));
    }

    [Fact]
    public async Task TheFingerprint_IsTheSameEverywhereItIsReported()
    {
        var expected = LiveFingerprint();
        var agent = _factory.CreateAgentClient();

        var header = (await agent.GetAsync("/api/v1/board")).Headers.GetValues(ApiContract.SchemaVersionHeader).Single();
        var meta = await agent.GetFromJsonAsync<ApiMetaDto>("/api/v1/meta", KanbanApiFactory.Json);
        using var version = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/version"));
        var openApi = await _factory.CreateClient().GetAsync("/api/openapi.json");

        Assert.Equal(expected, header);
        Assert.Equal(expected, meta!.SchemaVersion);
        Assert.Equal(expected, version.RootElement.GetProperty("apiSchemaVersion").GetString());
        Assert.Equal(expected, openApi.Headers.GetValues(ApiContract.SchemaVersionHeader).Single());
    }

    [Fact]
    public void Fingerprint_IgnoresAppVersionAndHost_ButNotTheContract()
    {
        var baseline = ApiContract.Fingerprint(FreshDocument());

        var otherRelease = FreshDocument();
        otherRelease.Info.Version = "9.9.9";
        otherRelease.Servers.Add(new OpenApiServer { Url = "http://elsewhere:1234" });
        Assert.Equal(baseline, ApiContract.Fingerprint(otherRelease));

        var changedContract = FreshDocument();
        changedContract.Paths.Add("/api/v1/new-endpoint", new OpenApiPathItem());
        Assert.NotEqual(baseline, ApiContract.Fingerprint(changedContract));

        Assert.Equal(baseline, LiveFingerprint());
    }

    /// <summary>
    /// The guard: an API change (endpoint, field, parameter or doc comment) changes the fingerprint,
    /// and must come with a new ApiChangeLog entry telling agents what changed.
    /// </summary>
    [Fact]
    public void ApiChangeLog_NewestEntry_MatchesTheLiveContract()
    {
        var live = LiveFingerprint();
        var newest = ApiChangeLog.Entries[0];
        Assert.True(newest.SchemaVersion == live,
            $"The /api/v1 contract changed (fingerprint {live}, newest ApiChangeLog entry says {newest.SchemaVersion}). " +
            $"Add an ApiChangeLog entry describing the change with SchemaVersion = \"{live}\".");
    }

    [Fact]
    public async Task OpenApiDocument_ListsPathsInAFixedOrder_SoEveryHostComputesTheSameFingerprint()
    {
        // 1.14.0: controller discovery order differs between the test host and IIS;
        // the fingerprint hashes the document text, so the order must be fixed.
        using var doc = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/openapi.json"));
        var paths = doc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal).ToList(), paths);
    }

    [Fact]
    public async Task ComputingTheFingerprint_DoesNotBlankTheServedVersion()
    {
        _ = LiveFingerprint();
        using var doc = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/openapi.json"));
        Assert.Equal(AppVersion.Current.Version, doc.RootElement.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal(LiveFingerprint(), ApiContract.Fingerprint(FreshDocument()));
    }

    [Fact]
    public void ApiChangeLog_IsNewestFirst_AndNotAheadOfTheApp()
    {
        var versions = ApiChangeLog.Entries.Select(e => e.Version).ToList();
        Assert.All(versions, v => Assert.True(AppVersion.IsValidSemVer(v), v));
        for (var i = 1; i < versions.Count; i++)
        {
            Assert.True(AppVersion.Compare(versions[i - 1], versions[i]) > 0, $"{versions[i - 1]} must be newer than {versions[i]}");
        }

        Assert.True(AppVersion.Compare(versions[0], AppVersion.Current.Version) <= 0);
        Assert.All(ApiChangeLog.Entries, e => Assert.NotEmpty(e.Summary));
    }

    [Fact]
    public async Task OpenApiDocument_SupportsConditionalRequests()
    {
        var client = _factory.CreateClient();
        var first = await client.GetAsync("/api/openapi.json");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.Contains("no-cache", first.Headers.CacheControl!.ToString());

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/openapi.json");
        request.Headers.IfNoneMatch.Add(etag!);
        var unchanged = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

        var stale = new HttpRequestMessage(HttpMethod.Get, "/api/openapi.json");
        stale.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"outdated\""));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(stale)).StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_TellsAgentsTheRule_AndDocumentsTheHeaders()
    {
        using var doc = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/openapi.json"));

        var description = doc.RootElement.GetProperty("info").GetProperty("description").GetString()!;
        Assert.Contains("X-API-Schema-Version", description);
        Assert.Contains("re-read /api/openapi.json", description);
        Assert.Contains("503", description);

        var getBoard = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/board").GetProperty("get");
        Assert.Equal("getBoard", getBoard.GetProperty("operationId").GetString());
        var headers = getBoard.GetProperty("responses").GetProperty("200").GetProperty("headers");
        Assert.True(headers.TryGetProperty(ApiContract.SchemaVersionHeader, out _));
        Assert.True(headers.TryGetProperty("Link", out _));

        var meta = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/meta").GetProperty("get");
        Assert.Equal("getApiMeta", meta.GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task Meta_DescribesTheApi_AndListsAllChanges()
    {
        var meta = await _factory.CreateAgentClient().GetFromJsonAsync<ApiMetaDto>("/api/v1/meta", KanbanApiFactory.Json);

        Assert.Equal(AppVersion.Current.Version, meta!.Version);
        Assert.Equal("v1", meta.ApiVersion);
        Assert.Equal("/api/openapi.json", meta.OpenApiUrl);
        Assert.Equal("/api/docs", meta.DocsUrl);
        Assert.Contains("X-API-Schema-Version", meta.Instructions);
        Assert.Equal(ApiChangeLog.Entries.Select(e => e.Version), meta.Changes.Select(c => c.Version));
    }

    [Theory]
    [InlineData("1.3.0", new[] { "1.14.0", "1.13.0", "1.9.0", "1.8.0", "1.7.0", "1.6.0", "1.5.0", "1.4.0" })]
    [InlineData("1.7.0", new[] { "1.14.0", "1.13.0", "1.9.0", "1.8.0" })]
    [InlineData("1.8.0", new[] { "1.14.0", "1.13.0", "1.9.0" })]
    [InlineData("1.9.0-rc.1", new[] { "1.14.0", "1.13.0", "1.9.0" })]
    [InlineData("1.9.0", new[] { "1.14.0", "1.13.0" })]
    [InlineData("1.12.0", new[] { "1.14.0", "1.13.0" })]
    [InlineData("1.13.1", new[] { "1.14.0" })]
    [InlineData("1.14.0", new string[0])]
    [InlineData("0.9.0", new[] { "1.14.0", "1.13.0", "1.9.0", "1.8.0", "1.7.0", "1.6.0", "1.5.0", "1.4.0", "1.0.0" })]
    public async Task Meta_Since_ListsOnlyNewerChanges(string since, string[] expected)
    {
        var meta = await _factory.CreateAgentClient().GetFromJsonAsync<ApiMetaDto>($"/api/v1/meta?since={since}", KanbanApiFactory.Json);
        Assert.Equal(expected, meta!.Changes.Select(c => c.Version));
    }

    [Theory]
    [InlineData("1.4")]
    [InlineData("v1.4.0")]
    [InlineData("latest")]
    public async Task Meta_InvalidSince_Is400(string since)
    {
        var response = await _factory.CreateAgentClient().GetAsync($"/api/v1/meta?since={since}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Meta_RequiresTheApiKey_LikeTheRestOfV1()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/meta")).StatusCode);
    }
}
