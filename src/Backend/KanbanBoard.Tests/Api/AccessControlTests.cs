using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KanbanBoard.Tests.Api;

/// <summary>A test host with a shared access password configured.</summary>
public class PasswordProtectedFactory : KanbanApiFactory
{
    public const string Password = "correct horse battery";

    /// <summary>Low iteration count keeps the suite fast; format and code path are identical.</summary>
    public static readonly string Hash = SharedPasswordHasher.Hash(Password, iterations: 10_000);

    protected virtual int LoginAttemptsPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("AccessControl:SharedPasswordHash", Hash);
        builder.UseSetting("AccessControl:LoginAttemptsPerMinute", LoginAttemptsPerMinute.ToString());
    }

    /// <summary>A browser-like client with its own cookie jar.</summary>
    public HttpClient CreateBrowser()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "QATrack");
        return client;
    }
}

public sealed class AccessControlTests : IClassFixture<PasswordProtectedFactory>
{
    private readonly PasswordProtectedFactory _factory;

    public AccessControlTests(PasswordProtectedFactory factory)
    {
        _factory = factory;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { password });

    [Fact]
    public async Task Status_ReportsPasswordRequired_AndNotSignedIn()
    {
        var status = await _factory.CreateBrowser().GetFromJsonAsync<StatusDto>("/api/auth/status");
        Assert.True(status!.Required);
        Assert.False(status.Authenticated);
    }

    [Theory]
    [InlineData("/api/ui/board")]
    [InlineData("/api/ui/workitems/1")]
    public async Task BoardData_WithoutSession_Is401(string path)
    {
        var response = await _factory.CreateBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Mutations_WithoutSession_Are401_AndPersistNothing()
    {
        var anonymous = _factory.CreateBrowser();
        var create = await anonymous.PostAsJsonAsync("/api/ui/workitems", new { title = "sneaky", type = "Bug" });
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);

        var browser = _factory.CreateBrowser();
        await Login(browser, PasswordProtectedFactory.Password);
        var board = await browser.GetStringAsync("/api/ui/board");
        Assert.DoesNotContain("sneaky", board);
    }

    [Fact]
    public async Task WrongPassword_Is401_AndGrantsNothing()
    {
        var browser = _factory.CreateBrowser();
        var response = await Login(browser, "wrong password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/ui/board")).StatusCode);
    }

    [Fact]
    public async Task CorrectPassword_SetsHardenedPersistentCookie_AndUnlocksTheBoard()
    {
        var browser = _factory.CreateBrowser();
        var response = await Login(browser, PasswordProtectedFactory.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("QATrack.Access=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/ui/board")).StatusCode);
        var status = await browser.GetFromJsonAsync<StatusDto>("/api/auth/status");
        Assert.True(status!.Authenticated);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var browser = _factory.CreateBrowser();
        await Login(browser, PasswordProtectedFactory.Password);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/ui/board")).StatusCode);
    }

    [Fact]
    public async Task Login_WithoutAntiForgeryHeader_IsRejected()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await Login(client, PasswordProtectedFactory.Password);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/version")]
    [InlineData("/api/openapi.json")]
    [InlineData("/api/auth/status")]
    [InlineData("/")]
    public async Task PublicEndpoints_StayReachable(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentApi_IsUnaffected_AndStillUsesItsApiKey()
    {
        var response = await _factory.CreateAgentClient().GetAsync("/api/v1/board");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/board")).StatusCode);
    }

    private sealed record StatusDto(bool Required, bool Authenticated);
}

/// <summary>Changing the password must sign existing browsers out.</summary>
public sealed class PasswordChangeTests
{
    [Fact]
    public async Task SessionIssuedForAnOldPassword_IsRejected()
    {
        using var factory = new PasswordProtectedFactory();
        var browser = factory.CreateBrowser();
        await browser.PostAsJsonAsync("/api/auth/login", new { password = PasswordProtectedFactory.Password });
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/ui/board")).StatusCode);

        // Simulate the operator running 'deploy-iis.ps1 -Action SetPassword' (config reloads live).
        var options = (Microsoft.Extensions.Options.IOptionsMonitor<KanbanBoard.Api.Middleware.AccessControlOptions>)
            factory.Services.GetService(typeof(Microsoft.Extensions.Options.IOptionsMonitor<KanbanBoard.Api.Middleware.AccessControlOptions>))!;
        options.CurrentValue.SharedPasswordHash = SharedPasswordHasher.Hash("a brand new password", 10_000);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/ui/board")).StatusCode);
    }
}

/// <summary>Brute-force brake on the sign-in endpoint.</summary>
public sealed class LoginRateLimitTests : IClassFixture<LoginRateLimitTests.StrictFactory>
{
    public sealed class StrictFactory : PasswordProtectedFactory
    {
        protected override int LoginAttemptsPerMinute => 3;
    }

    private readonly StrictFactory _factory;

    public LoginRateLimitTests(StrictFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FourthAttemptWithinAMinute_Is429_EvenWithTheRightPassword()
    {
        var browser = _factory.CreateBrowser();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/auth/login", new { password = "guess" + i })).StatusCode);
        }

        var blocked = await browser.PostAsJsonAsync("/api/auth/login", new { password = PasswordProtectedFactory.Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many sign-in attempts", await blocked.Content.ReadAsStringAsync());
    }
}

/// <summary>No password configured: behaves exactly as before 1.3.0.</summary>
public sealed class OpenBoardTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public OpenBoardTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StatusSaysNotRequired_AndBoardIsOpen()
    {
        var client = _factory.CreateUiClient();
        var status = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/status");
        Assert.False(status.GetProperty("required").GetBoolean());
        Assert.True(status.GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/board")).StatusCode);
    }
}
