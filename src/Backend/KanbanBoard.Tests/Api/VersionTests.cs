using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>Semantic Versioning: parsing, the /api/version endpoint and single-source consistency.</summary>
public sealed class VersionTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public VersionTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private static string RootPackageJsonVersion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KanbanBoard.sln")))
        {
            dir = dir.Parent;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "package.json")));
        return doc.RootElement.GetProperty("version").GetString()!;
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.1.0-beta.1", true)]
    [InlineData("2.0.0-rc.1+abc1234", true)]
    [InlineData("0.0.1", true)]
    [InlineData("1.0", false)]
    [InlineData("v1.0.0", false)]
    [InlineData("01.0.0", false)]
    [InlineData("1.0.0-", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidSemVer_FollowsTheSpec(string? value, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsValidSemVer(value));
    }

    [Theory]
    [InlineData("1.5.0", "1.4.0", 1)]
    [InlineData("1.4.0", "1.10.0", -1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    [InlineData("1.5.0-rc.1", "1.5.0", -1)]
    [InlineData("1.5.0-rc.2", "1.5.0-rc.10", -1)]
    [InlineData("1.5.0-alpha", "1.5.0-alpha.1", -1)]
    [InlineData("1.5.0-beta", "1.5.0-alpha", 1)]
    [InlineData("1.5.0+abc", "1.5.0+def", 0)]
    public void Compare_FollowsSemVerPrecedence(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(AppVersion.Compare(a, b)));
        Assert.Equal(-expected, Math.Sign(AppVersion.Compare(b, a)));
    }

    [Fact]
    public void Compare_RejectsInvalidVersions()
    {
        Assert.Throws<FormatException>(() => AppVersion.Compare("1.4", "1.4.0"));
    }

    [Fact]
    public void Parse_SplitsBuildMetadataAndShortensCommit()
    {
        var v = AppVersion.Parse("1.2.3-rc.1+1c3f0969b507765d7755895b051ae3fb13618fe2");
        Assert.Equal("1.2.3-rc.1", v.Version);
        Assert.Equal("1c3f096", v.Commit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    public void Parse_InvalidInput_FallsBackSafely(string? value)
    {
        Assert.Equal("0.0.0", AppVersion.Parse(value).Version);
    }

    [Fact]
    public void RunningAssembly_VersionComesFromRootPackageJson()
    {
        Assert.Equal(RootPackageJsonVersion(), AppVersion.Current.Version);
        Assert.True(AppVersion.IsValidSemVer(AppVersion.Current.InformationalVersion));
    }

    [Fact]
    public async Task VersionEndpoint_IsPublic_AndReportsSemVer()
    {
        var body = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/version");

        Assert.Equal("QATrack", body.GetProperty("name").GetString());
        var version = body.GetProperty("version").GetString();
        Assert.Equal(RootPackageJsonVersion(), version);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), version!);
    }

    [Fact]
    public async Task VersionEndpoint_IsNeverCached_AndReportsCanonicalBuild()
    {
        var response = await _factory.CreateClient().GetAsync("/api/version");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.CacheControl?.NoStore, "The update notifier needs an uncacheable source of truth.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var version = body.GetProperty("version").GetString()!;
        var build = body.GetProperty("build").GetString()!;
        Assert.Matches(new Regex($"^{Regex.Escape(version)}(\\+[0-9a-f]{{7}})?$"), build);
    }

    [Theory]
    [InlineData("1.2.0+1c3f0969b507765d7755895b051ae3fb13618fe2", "1.2.0+1c3f096")]
    [InlineData("1.2.0", "1.2.0")]
    public void Build_IsVersionPlusShortCommit(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.Parse(informational).Build);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task SpaShell_IsServedNoCache_SoReloadAlwaysGetsTheNewBuild(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return; // SPA not built in this checkout; the E2E suite covers the built bundle.
        }

        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task OpenApiInfoVersion_IsTheAppSemVer()
    {
        var doc = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/openapi.json");
        Assert.Equal(RootPackageJsonVersion(), doc.GetProperty("info").GetProperty("version").GetString());
    }
}
