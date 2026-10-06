using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace KanbanBoard.Tests.Api;

/// <summary>1.15.0: hosting on Linux behind Caddy (and Cloudflare).</summary>
public sealed class LinuxHostingTests
{
    private sealed class ProxiedFactory : KanbanApiFactory
    {
        private readonly bool _behindProxy;

        public ProxiedFactory(bool behindProxy) => _behindProxy = behindProxy;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ReverseProxy:Enabled", _behindProxy ? "true" : "false");
            builder.UseSetting("IssueReporting:RequestsPerMinute", "2");
        }
    }

    private static HttpClient Reporter(KanbanApiFactory factory, string clientIp)
    {
        var client = factory.CreateReporterClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        return client;
    }

    [Fact]
    public async Task BehindTheProxy_EachClientIpHasItsOwnReportLimit()
    {
        using var factory = new ProxiedFactory(behindProxy: true);
        var first = Reporter(factory, "203.0.113.10");
        var second = Reporter(factory, "198.51.100.20");

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await first.PostAsJsonAsync("/api/report/issues", new { title = $"a{i}" })).StatusCode);
        }

        Assert.Equal((HttpStatusCode)429, (await first.PostAsJsonAsync("/api/report/issues", new { title = "a-over" })).StatusCode);
        // Without forwarded headers every client would share Caddy's address and be blocked too.
        Assert.Equal(HttpStatusCode.Created, (await second.PostAsJsonAsync("/api/report/issues", new { title = "b0" })).StatusCode);
    }

    [Fact]
    public async Task WithoutTheProxySetting_ForwardedHeadersAreIgnored()
    {
        using var factory = new ProxiedFactory(behindProxy: false);
        var first = Reporter(factory, "203.0.113.10");
        var second = Reporter(factory, "198.51.100.20");

        for (var i = 0; i < 2; i++)
        {
            await first.PostAsJsonAsync("/api/report/issues", new { title = $"a{i}" });
        }

        // A spoofed header can't buy a fresh quota when nothing is meant to set it.
        Assert.Equal((HttpStatusCode)429, (await second.PostAsJsonAsync("/api/report/issues", new { title = "b0" })).StatusCode);
    }

    [Fact]
    public void HashPasswordTool_PrintsAVerifiableHash_AndRefusesShortPasswords()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, HashPasswordTool.Run(new StringReader("correct horse battery\n"), output, error));
        var hash = output.ToString().Trim();
        Assert.True(SharedPasswordHasher.Verify("correct horse battery", hash));
        Assert.Empty(error.ToString());

        Assert.Equal(2, HashPasswordTool.Run(new StringReader("short\n"), new StringWriter(), error));
        Assert.Contains("at least", error.ToString());
        Assert.Equal(2, HashPasswordTool.Run(new StringReader(string.Empty), new StringWriter(), new StringWriter()));
    }
}
