using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KanbanBoard.Tests.Api;

/// <summary>
/// 1.18.0: the reporting API tells the programs under test when it changed, like
/// the AI agent API does (X-API-Schema-Version, Link, GET /api/report/meta).
/// </summary>
public sealed class ReportContractTests : IClassFixture<ReportContractTests.Factory>
{
    public sealed class Factory : KanbanApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("IssueReporting:RequestsPerMinute", "1000");
        }
    }

    private readonly Factory _factory;

    public ReportContractTests(Factory factory)
    {
        _factory = factory;
    }

    private ApiContract Contract => _factory.Services.GetRequiredService<ApiContract>();

    private void AssertSignals(HttpResponseMessage response)
    {
        Assert.Equal(Contract.ReportSchemaVersion, response.Headers.GetValues(ApiContract.SchemaVersionHeader).Single());
        Assert.Contains("</api/openapi-report.json>; rel=\"service-desc\"", response.Headers.GetValues("Link"));
    }

    [Fact]
    public void TheReportingApi_HasItsOwnFingerprint()
    {
        Assert.Matches("^[0-9a-f]{12}$", Contract.ReportSchemaVersion);
        Assert.NotEqual(Contract.SchemaVersion, Contract.ReportSchemaVersion);
    }

    [Fact]
    public async Task EveryReportingResponse_CarriesTheSignal_EvenErrors()
    {
        var reporter = _factory.CreateReporterClient();
        AssertSignals(await reporter.PostAsJsonAsync("/api/report/issues", new { title = "Signal check" })); // 201
        AssertSignals(await reporter.PostAsJsonAsync("/api/report/issues", new { description = "no title" })); // 400
        AssertSignals(await reporter.GetAsync("/api/report/ping")); // 200
        AssertSignals(await _factory.CreateClient().GetAsync("/api/report/ping")); // 401
    }

    [Fact]
    public async Task Ping_ReportsTheFingerprint()
    {
        var ping = (await _factory.CreateReporterClient().GetFromJsonAsync<ReportPingDto>("/api/report/ping", KanbanApiFactory.Json))!;
        Assert.Equal(Contract.ReportSchemaVersion, ping.SchemaVersion);
    }

    [Theory]
    [InlineData(null, new[] { "1.18.0", "1.14.0", "1.13.0", "1.12.0" })]
    [InlineData("1.13.0", new[] { "1.18.0", "1.14.0" })]
    [InlineData("1.14.0", new[] { "1.18.0" })]
    [InlineData("1.18.0", new string[0])]
    public async Task Meta_ListsWhatChanged_SinceAVersion(string? since, string[] expected)
    {
        var url = since is null ? "/api/report/meta" : $"/api/report/meta?since={since}";
        var meta = (await _factory.CreateReporterClient().GetFromJsonAsync<ReportMetaDto>(url, KanbanApiFactory.Json))!;

        Assert.Equal(expected, meta.Changes.Select(c => c.Version));
        Assert.Equal(Contract.ReportSchemaVersion, meta.SchemaVersion);
        Assert.Equal("/api/openapi-report.json", meta.OpenApiUrl);
        Assert.Contains("X-API-Schema-Version", meta.Instructions);
    }

    [Fact]
    public async Task Meta_NeedsTheReporterKey_AndAValidVersion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/report/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _factory.CreateReporterClient().GetAsync("/api/report/meta?since=latest")).StatusCode);
    }

    [Fact]
    public async Task TheReportingDocument_SupportsConditionalRequests_AndCarriesTheSignal()
    {
        var client = _factory.CreateClient();
        var first = await client.GetAsync("/api/openapi-report.json");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(Contract.ReportSchemaVersion, first.Headers.GetValues(ApiContract.SchemaVersionHeader).Single());
        var etag = first.Headers.ETag!;

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/openapi-report.json");
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag));
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);

        var body = await first.Content.ReadAsStringAsync();
        Assert.Contains("/api/report/meta", body);
        Assert.Contains("Staying current", body);
    }

    [Fact]
    public void ReportApiChangeLog_NewestEntry_MatchesTheLiveContract()
    {
        var live = Contract.ReportSchemaVersion;
        var newest = ReportApiChangeLog.Entries[0];
        Assert.True(newest.SchemaVersion == live,
            $"The /api/report contract changed (fingerprint {live}, newest ReportApiChangeLog entry says {newest.SchemaVersion}). " +
            $"Add a ReportApiChangeLog entry describing the change for the programs under test with SchemaVersion = \"{live}\".");
    }

    [Fact]
    public void ReportApiChangeLog_IsNewestFirst_AndNotAheadOfTheApp()
    {
        var versions = ReportApiChangeLog.Entries.Select(e => e.Version).ToList();
        Assert.All(versions, v => Assert.True(AppVersion.IsValidSemVer(v), v));
        for (var i = 1; i < versions.Count; i++)
        {
            Assert.True(AppVersion.Compare(versions[i - 1], versions[i]) > 0, $"{versions[i - 1]} must be newer than {versions[i]}");
        }

        Assert.True(AppVersion.Compare(versions[0], AppVersion.Current.Version) <= 0);
        Assert.All(ReportApiChangeLog.Entries, e => Assert.NotEmpty(e.Summary));
    }
}
