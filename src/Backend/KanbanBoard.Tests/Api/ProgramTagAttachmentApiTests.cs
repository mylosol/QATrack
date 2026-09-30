using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using KanbanBoard.Tests.Services;

namespace KanbanBoard.Tests.Api;

/// <summary>Programs, tags and image uploads over HTTP, for agents and the browser (1.4.0).</summary>
public sealed class ProgramTagAttachmentApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public ProgramTagAttachmentApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes, string fileName, string claimedType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(claimedType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Agent_ListsAndAddsPrograms()
    {
        var agent = _factory.CreateAgentClient();

        var programs = await agent.GetFromJsonAsync<List<ProgramDto>>("/api/v1/programs", KanbanApiFactory.Json);
        Assert.Equal("ProveOut", programs![0].Name);
        Assert.Equal("CallOut", programs[1].Name);

        var name = "Agent-" + Guid.NewGuid().ToString("N")[..8];
        var created = await agent.PostAsJsonAsync("/api/v1/programs", new { name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var again = await agent.PostAsJsonAsync("/api/v1/programs", new { name = name.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(name, (await again.Content.ReadFromJsonAsync<ProgramDto>(KanbanApiFactory.Json))!.Name);
    }

    [Fact]
    public async Task Agent_CreatesAndRetagsAnItem_AuditedAsAi()
    {
        var agent = _factory.CreateAgentClient("Tagger-Bot");

        var response = await agent.PostAsJsonAsync("/api/v1/workitems",
            new { title = "Tagged by agent", type = "Bug", program = "CallOut", tags = new[] { "api", "nightly" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        Assert.Equal("CallOut", created.Program);
        Assert.Equal(new[] { "api", "nightly" }, created.Tags);

        var patched = await agent.PatchAsJsonAsync($"/api/v1/workitems/{created.Id}", new { tags = new[] { "api" } });
        var item = (await patched.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        var entry = item.History![^1];
        Assert.True(entry.IsAiAction);
        Assert.Equal("Tagger-Bot", entry.AgentName);
        Assert.Equal(new FieldChange("api; nightly", "api"), entry.ChangedFields["Tags"]);

        var filtered = await agent.GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems?tag=API&program=callout", KanbanApiFactory.Json);
        Assert.Contains(filtered!, w => w.Id == created.Id);
    }

    [Fact]
    public async Task Agent_ReportsTheProgramVersionOfABug()
    {
        var agent = _factory.CreateAgentClient("Version-Bot");
        var response = await agent.PostAsJsonAsync("/api/v1/workitems",
            new { title = "Regression in 2.4.1", type = "Bug", program = "ProveOut", programVersion = "2.4.1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        Assert.Equal("2.4.1", created.ProgramVersion);

        var fetched = await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{created.Id}", KanbanApiFactory.Json);
        Assert.Equal("2.4.1", fetched!.ProgramVersion);
    }

    [Fact]
    public async Task ProgramVersion_LongerThan64_Is400()
    {
        var response = await _factory.CreateUiClient().PostAsJsonAsync("/api/ui/workitems",
            new { title = "x", type = "Bug", programVersion = new string('9', 65) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownProgram_Is400_ProblemDetails()
    {
        var response = await _factory.CreateUiClient().PostAsJsonAsync("/api/ui/workitems",
            new { title = "x", type = "Bug", program = "DoesNotExist" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DoesNotExist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Browser_AddsAProgram_ThatAppearsOnTheBoard()
    {
        var ui = _factory.CreateUiClient();
        var name = "Ui-" + Guid.NewGuid().ToString("N")[..8];

        var created = await ui.PostAsJsonAsync("/api/ui/programs", new { name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var board = await ui.GetFromJsonAsync<BoardDto>("/api/ui/board", KanbanApiFactory.Json);
        Assert.Equal(name, board!.Metadata.Programs[^1]);
    }

    [Fact]
    public async Task Browser_ProgramCreate_NeedsTheAntiForgeryHeader()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/ui/programs", new { name = "csrf" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Browser_UploadsAnImage_AndServesItBack_Safely()
    {
        var ui = _factory.CreateUiClient();
        var bytes = ProgramTagAttachmentServiceTests.Png.Concat(Guid.NewGuid().ToByteArray()).ToArray();

        var upload = await ui.PostAsync("/api/ui/attachments", ImageForm(bytes, "shot.png"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var dto = (await upload.Content.ReadFromJsonAsync<AttachmentDto>(KanbanApiFactory.Json))!;
        Assert.StartsWith("api/ui/attachments/", dto.Url);

        var image = await ui.GetAsync("/" + dto.Url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("private", image.Headers.CacheControl!.ToString());

        // Agents fetch the same id through their own API.
        var viaAgent = await _factory.CreateAgentClient().GetAsync($"/api/v1/attachments/{dto.Id}");
        Assert.Equal(bytes, await viaAgent.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_OfSvgOrHtml_IsRejected_WhateverItClaims()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");
        var response = await _factory.CreateUiClient().PostAsync("/api/ui/attachments", ImageForm(svg, "cat.png", "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PNG, JPEG, GIF or WebP", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_WithoutAntiForgeryHeader_IsForbidden()
    {
        var response = await _factory.CreateClient().PostAsync("/api/ui/attachments",
            ImageForm(ProgramTagAttachmentServiceTests.Png, "x.png"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutAFile_Is400()
    {
        var response = await _factory.CreateUiClient().PostAsync("/api/ui/attachments", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Agent_UploadsAnImage()
    {
        var bytes = ProgramTagAttachmentServiceTests.Png.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var response = await _factory.CreateAgentClient().PostAsync("/api/v1/attachments", ImageForm(bytes, "agent.png"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<AttachmentDto>(KanbanApiFactory.Json))!;
        Assert.Equal($"![agent.png](api/ui/attachments/{dto.Id:D})", dto.Markdown);
        Assert.EndsWith($"/api/v1/attachments/{dto.Id:D}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task UnknownAttachment_Is404()
    {
        var response = await _factory.CreateUiClient().GetAsync($"/api/ui/attachments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
