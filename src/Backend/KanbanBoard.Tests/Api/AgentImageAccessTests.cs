using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using KanbanBoard.Tests.Services;

namespace KanbanBoard.Tests.Api;

/// <summary>
/// 1.16.1: pictures pasted into descriptions and comments are Markdown links to
/// api/ui/attachments/{id}. Agents can open that exact link with their API key
/// (read-only), even when the board is behind a password.
/// </summary>
public sealed class AgentImageAccessTests : IClassFixture<PasswordProtectedFactory>
{
    private readonly PasswordProtectedFactory _factory;

    public AgentImageAccessTests(PasswordProtectedFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Uploads a unique screenshot the way an in-app report does; returns its markdown link target.</summary>
    private async Task<(string Path, byte[] Bytes)> PastedPicture()
    {
        var bytes = ProgramTagAttachmentServiceTests.Png.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var upload = await _factory.CreateReporterClient().PostAsync("/api/report/attachments", new MultipartFormDataContent { { file, "file", "screen.png" } });
        var image = (await upload.Content.ReadFromJsonAsync<AttachmentDto>(KanbanApiFactory.Json))!;
        Assert.StartsWith("api/ui/attachments/", image.Url);
        return ("/" + image.Url, bytes);
    }

    [Fact]
    public async Task AnAgent_OpensThePastedPictureLink_WithItsKey()
    {
        var (path, bytes) = await PastedPicture();

        var response = await _factory.CreateAgentClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheKeyAlone_IsEnough_NoAgentIdentityNeeded()
    {
        var (path, _) = await PastedPicture();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", KanbanApiFactory.TestApiKey);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task AWrongKey_OrNoSignIn_StillCannotSeeIt()
    {
        var (path, _) = await PastedPicture();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateAgentClient(apiKey: "wrong-key-0123456789abcdef").GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(path)).StatusCode);
        // The reporter key can upload pictures but not read them back.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateReporterClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task AnUnknownPicture_Is404_ForAnAgent()
    {
        var response = await _factory.CreateAgentClient().GetAsync($"/api/ui/attachments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EverythingElseOnTheBoardApi_StillRefusesAgents()
    {
        // Refused either by the board's sign-in (401) or by the "agents use /api/v1" rule (400).
        var refused = new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest };
        var agent = _factory.CreateAgentClient();
        Assert.Contains((await agent.GetAsync("/api/ui/board")).StatusCode, refused);
        Assert.Contains((await agent.GetAsync($"/api/ui/workitems/1")).StatusCode, refused);

        var file = new ByteArrayContent(ProgramTagAttachmentServiceTests.Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Contains((await agent.PostAsync("/api/ui/attachments", new MultipartFormDataContent { { file, "file", "x.png" } })).StatusCode, refused);
    }
}
