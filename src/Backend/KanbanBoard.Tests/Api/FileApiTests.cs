using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using KanbanBoard.Tests.Services;
using Microsoft.AspNetCore.Hosting;

namespace KanbanBoard.Tests.Api;

/// <summary>1.14.0: log files attached to work items.</summary>
public sealed class FileApiTests : IClassFixture<FileApiTests.Factory>
{
    /// <summary>Lifts the reporter rate limit: every test here shares one IP.</summary>
    public sealed class Factory : KanbanApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("IssueReporting:RequestsPerMinute", "1000");
        }
    }

    private readonly Factory _factory;

    public FileApiTests(Factory factory)
    {
        _factory = factory;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string name, string clientType = "application/octet-stream")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(clientType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    private async Task<int> NewItem()
    {
        var created = await (await _factory.CreateUiClient("Robert").PostAsJsonAsync("/api/ui/workitems", new { title = "Has logs", type = "Bug" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json);
        return created!.Id;
    }

    [Fact]
    public async Task Person_AttachesALog_EveryoneSeesIt_AndItIsInHistory()
    {
        var id = await NewItem();
        var log = Encoding.UTF8.GetBytes("2026-10-05 10:00:01 ERROR Export failed: timeout\n\tat Exporter.Run()\n");

        var response = await _factory.CreateUiClient("Robert").PostAsync($"/api/ui/workitems/{id}/files", Upload(log, @"C:\logs\app.log"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = (await response.Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;
        Assert.Equal("app.log", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(log.Length, file.Length);
        Assert.Equal("Robert", file.AddedBy);
        Assert.False(file.IsAiAction);
        Assert.Equal($"api/v1/workitems/{id}/files/{file.Id}", file.Url);

        var agent = _factory.CreateAgentClient();
        var card = (await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;
        Assert.Equal(1, card.FileCount);
        Assert.Equal("app.log", Assert.Single(card.Files!).FileName);
        var entry = card.History!.Last();
        Assert.Equal(new FieldChange(null, "app.log"), entry.ChangedFields["Files"]);
        Assert.Equal("Robert", entry.Author);
        Assert.False(card.AiModified);

        // The list and board carry the count.
        var listed = await agent.GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems", KanbanApiFactory.Json);
        Assert.Equal(1, listed!.Single(w => w.Id == id).FileCount);
        Assert.Null(listed!.Single(w => w.Id == id).Files);

        // The agent downloads it.
        var download = await agent.GetAsync(file.Url.Insert(0, "/"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("text/plain", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", download.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(log, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task TheBoard_ViewsTextInline_AndDownloadsOnRequest()
    {
        var id = await NewItem();
        var ui = _factory.CreateUiClient("Robert");
        var file = (await (await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload("hello"u8.ToArray(), "out.txt")))
            .Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;

        var view = await ui.GetAsync($"/api/ui/workitems/{id}/files/{file.Id}");
        Assert.Equal("inline", view.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("out.txt", view.Content.Headers.ContentDisposition.FileNameStar);

        var save = await ui.GetAsync($"/api/ui/workitems/{id}/files/{file.Id}?download=true");
        Assert.Equal("attachment", save.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 }, "logs.zip", FileService.Zip)]
    [InlineData(new byte[] { 0x1F, 0x8B, 0x08, 0, 0, 0 }, "app.log.gz", FileService.Gzip)]
    [InlineData(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 4 }, "logs.7z", FileService.SevenZip)]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'h', 0, (byte)'i', 0 }, "win.log", FileService.Text)]
    [InlineData(new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'\n' }, "ansi.log", FileService.Text)]
    public async Task ArchivesAndAnyTextEncoding_AreAccepted(byte[] bytes, string name, string expectedType)
    {
        var id = await NewItem();
        var response = await _factory.CreateUiClient().PostAsync($"/api/ui/workitems/{id}/files", Upload(bytes, name, "text/plain"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedType, (await response.Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!.ContentType);
    }

    [Fact]
    public async Task ArchivesAreAlwaysDownloads_AndWindowsTextKeepsItsCharset()
    {
        var id = await NewItem();
        var ui = _factory.CreateUiClient();
        var zip = (await (await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(new byte[] { 0x50, 0x4B, 0x05, 0x06, 9 }, "bundle.zip")))
            .Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;
        var ansi = (await (await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(new byte[] { (byte)'n', 0xE9, (byte)'e' }, "ansi.log")))
            .Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;

        var zipResponse = await ui.GetAsync($"/api/ui/workitems/{id}/files/{zip.Id}");
        Assert.Equal("attachment", zipResponse.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/zip", zipResponse.Content.Headers.ContentType!.MediaType);

        var ansiResponse = await ui.GetAsync($"/api/ui/workitems/{id}/files/{ansi.Id}");
        Assert.Equal("windows-1252", ansiResponse.Content.Headers.ContentType!.CharSet);
    }

    [Fact]
    public async Task BinariesAndImages_AreRefused_WithAHelpfulMessage()
    {
        var id = await NewItem();
        var ui = _factory.CreateUiClient();

        var exe = await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(new byte[] { (byte)'M', (byte)'Z', 0x90, 0, 3, 0 }, "tool.log", "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, exe.StatusCode);
        Assert.Contains("log or other text files", await exe.Content.ReadAsStringAsync());

        var png = await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(ProgramTagAttachmentServiceTests.Png, "screen.png", "image/png"));
        Assert.Equal(HttpStatusCode.BadRequest, png.StatusCode);
        Assert.Contains("screenshots", await png.Content.ReadAsStringAsync());

        var empty = await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(Array.Empty<byte>(), "empty.log"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task A15MbLog_IsAccepted_ButOver20MbIsNot()
    {
        var id = await NewItem();
        var ui = _factory.CreateUiClient();
        var line = Encoding.UTF8.GetBytes("2026-10-05 10:00:00 INFO a perfectly ordinary log line\n");

        var big = Enumerable.Repeat(line, 15 * 1024 * 1024 / line.Length).SelectMany(b => b).ToArray();
        Assert.Equal(HttpStatusCode.Created, (await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(big, "big.log"))).StatusCode);

        var tooBig = Enumerable.Repeat(line, (21 * 1024 * 1024 / line.Length) + 1).SelectMany(b => b).ToArray();
        var response = await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload(tooBig, "huge.log"));
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, response.StatusCode.ToString());
    }

    [Fact]
    public async Task Removing_HidesTheFile_RecordsIt_AndKeepsTheAuditTrail()
    {
        var id = await NewItem();
        var ui = _factory.CreateUiClient("Alex");
        var file = (await (await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload("oops"u8.ToArray(), "wrong.log")))
            .Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;

        Assert.Equal(HttpStatusCode.NoContent, (await ui.DeleteAsync($"/api/ui/workitems/{id}/files/{file.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.DeleteAsync($"/api/ui/workitems/{id}/files/{file.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.GetAsync($"/api/ui/workitems/{id}/files/{file.Id}")).StatusCode);

        var card = (await ui.GetFromJsonAsync<WorkItemDto>($"/api/ui/workitems/{id}", KanbanApiFactory.Json))!;
        Assert.Equal(0, card.FileCount);
        Assert.Empty(card.Files!);
        var removal = card.History!.Last();
        Assert.Equal(new FieldChange("wrong.log", null), removal.ChangedFields["Files"]);
        Assert.Equal("Alex", removal.Author);
    }

    [Fact]
    public async Task Agents_AttachTestOutput_AsAnAiAction_AndCannotRemove()
    {
        var id = await NewItem();
        var agent = _factory.CreateAgentClient("Test-Runner");
        var response = await agent.PostAsync($"/api/v1/workitems/{id}/files", Upload("PASS 41\nFAIL 1\n"u8.ToArray(), "results.txt"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = (await response.Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;
        Assert.True(file.IsAiAction);
        Assert.Equal("Test-Runner", file.AddedBy);

        var listed = await agent.GetFromJsonAsync<List<WorkItemFileDto>>($"/api/v1/workitems/{id}/files", KanbanApiFactory.Json);
        Assert.Single(listed!);
        var card = (await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;
        Assert.True(card.AiModified);
        Assert.True(card.History!.Last().IsAiAction);

        var delete = await agent.DeleteAsync($"/api/v1/workitems/{id}/files/{file.Id}");
        Assert.Contains(delete.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task WrongCardOrUnknownFile_Is404()
    {
        var id = await NewItem();
        var other = await NewItem();
        var ui = _factory.CreateUiClient();
        var file = (await (await ui.PostAsync($"/api/ui/workitems/{id}/files", Upload("x"u8.ToArray(), "a.log")))
            .Content.ReadFromJsonAsync<WorkItemFileDto>(KanbanApiFactory.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await ui.GetAsync($"/api/ui/workitems/{other}/files/{file.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.GetAsync($"/api/ui/workitems/{id}/files/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.PostAsync("/api/ui/workitems/999999/files", Upload("x"u8.ToArray(), "a.log"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateAgentClient().GetAsync("/api/v1/workitems/999999/files")).StatusCode);
    }

    [Fact]
    public async Task Uploads_NeedTheAntiForgeryHeader_AndTheAgentKey()
    {
        var id = await NewItem();
        Assert.NotEqual(HttpStatusCode.Created, (await _factory.CreateClient().PostAsync($"/api/ui/workitems/{id}/files", Upload("x"u8.ToArray(), "a.log"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync($"/api/v1/workitems/{id}/files", Upload("x"u8.ToArray(), "a.log"))).StatusCode);
    }

    [Fact]
    public async Task Reporter_UploadsLogs_AndAttachesThemToTheReport()
    {
        var reporter = _factory.CreateReporterClient();
        var upload = await reporter.PostAsync("/api/report/files", Upload("crash at 10:02\n"u8.ToArray(), "crash.log"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var stored = (await upload.Content.ReadFromJsonAsync<ReportFileDto>(KanbanApiFactory.Json))!;
        Assert.Equal("crash.log", stored.FileName);

        var response = await reporter.PostAsJsonAsync("/api/report/issues", new { title = "Crash with log", reporter = "Jane", files = new[] { stored.Id } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<ReportReceiptDto>(KanbanApiFactory.Json))!;
        Assert.Equal(new[] { "crash.log" }, receipt.Files);

        var card = (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{receipt.Id}", KanbanApiFactory.Json))!;
        var file = Assert.Single(card.Files!);
        Assert.Equal("Jane (in-app report)", file.AddedBy);
        Assert.False(file.IsAiAction);
        Assert.False(card.AiModified);
    }

    [Fact]
    public async Task Reporter_UnknownFileIds_OrScreenshotsAsFiles_Are400_AndFileNothing()
    {
        var reporter = _factory.CreateReporterClient();
        var image = (await (await reporter.PostAsync("/api/report/attachments", Upload(ProgramTagAttachmentServiceTests.Png.Concat(Guid.NewGuid().ToByteArray()).ToArray(), "s.png", "image/png")))
            .Content.ReadFromJsonAsync<AttachmentDto>(KanbanApiFactory.Json))!;

        foreach (var ids in new[] { new[] { Guid.NewGuid() }, new[] { image.Id } })
        {
            var title = $"Bad files {Guid.NewGuid():N}";
            var response = await reporter.PostAsJsonAsync("/api/report/issues", new { title, files = ids });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("POST /api/report/files", await response.Content.ReadAsStringAsync());
            var all = await _factory.CreateAgentClient().GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems", KanbanApiFactory.Json);
            Assert.DoesNotContain(all!, w => w.Title == title);
        }

        var six = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await reporter.PostAsJsonAsync("/api/report/issues", new { title = "Too many", files = six })).StatusCode);
    }

    [Fact]
    public void DetectFileType_KnowsTextFromBinary()
    {
        Assert.Equal(FileService.Text, FileService.DetectFileType("plain\r\n\tlog \u001b[31mred\u001b[0m\n"u8));
        Assert.Null(FileService.DetectFileType(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Null(FileService.DetectFileType(new byte[] { (byte)'a', 0, (byte)'b' }));
        Assert.Equal("utf-8", FileService.TextCharset("héllo"u8));
        Assert.Equal("windows-1252", FileService.TextCharset(new byte[] { (byte)'h', 0xE9 }));
        Assert.Equal("utf-16le", FileService.TextCharset(new byte[] { 0xFF, 0xFE, 0x41, 0 }));
    }
}
