using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KanbanBoard.Tests.Services;

/// <summary>Programs, tags and image attachments (1.4.0).</summary>
public sealed class ProgramTagAttachmentServiceTests : IAsyncLifetime, IDisposable
{
    /// <summary>Smallest valid PNG signature plus a little payload.</summary>
    internal static readonly byte[] Png =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };

    private readonly TempSqliteDatabase _temp = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => _temp.InitializeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _temp.Dispose();

    private static ActorContext Human(string name = "Dana")
    {
        var actor = new ActorContext();
        actor.SetHuman(name);
        return actor;
    }

    private WorkItemService Items() => new(_temp.CreateContext(), Human(), _clock);

    private ProgramService Programs() =>
        new(_temp.CreateContext(), _clock, NullLogger<ProgramService>.Instance, Human());

    private AttachmentService Attachments() => new(_temp.CreateContext(), Human("Uploader"), _clock);

    private static CreateWorkItemRequest Bug(string? program = null, params string[] tags) => new()
    {
        Title = "Crash",
        Type = WorkItemType.Bug,
        Program = program,
        Tags = tags.Length == 0 ? null : tags.ToList(),
    };

    // ---- programs --------------------------------------------------------

    [Fact]
    public async Task FreshDatabase_HasTheTwoInitialPrograms_InOrder()
    {
        var programs = await Programs().ListAsync();
        Assert.Equal(new[] { "ProveOut", "CallOut" }, programs.Select(p => p.Name));
    }

    [Fact]
    public async Task CreateProgram_AppendsAtTheEnd_AndIsIdempotentIgnoringCase()
    {
        var (added, created) = await Programs().CreateAsync(new CreateProgramRequest { Name = "  FieldTest  " });
        Assert.True(created);
        Assert.Equal("FieldTest", added.Name);
        Assert.Equal(2, added.SortOrder);

        var (again, createdAgain) = await Programs().CreateAsync(new CreateProgramRequest { Name = "fieldtest" });
        Assert.False(createdAgain);
        Assert.Equal(added.Id, again.Id);
        Assert.Equal("FieldTest", again.Name);
        Assert.Equal(3, (await Programs().ListAsync()).Count);
    }

    [Fact]
    public async Task CreateProgram_Blank_IsRejected()
    {
        await Assert.ThrowsAsync<WorkItemValidationException>(() =>
            Programs().CreateAsync(new CreateProgramRequest { Name = " \t " }));
    }

    [Fact]
    public async Task CreateItem_WithProgram_MatchesIgnoringCase_AndRecordsIt()
    {
        var created = await Items().CreateAsync(Bug("proveout"));

        Assert.Equal("ProveOut", created.Program);
        Assert.Equal("ProveOut", created.History![0].ChangedFields["Program"].New);
    }

    [Fact]
    public async Task CreateItem_WithUnknownProgram_IsRejectedWithTheKnownNames()
    {
        var ex = await Assert.ThrowsAsync<WorkItemValidationException>(() => Items().CreateAsync(Bug("Nope")));
        Assert.Contains("ProveOut", ex.Errors["Program"][0]);
    }

    [Fact]
    public async Task UpdateProgram_ChangesAndClears_WithHistory()
    {
        var created = await Items().CreateAsync(Bug("ProveOut"));

        var moved = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Program = "CallOut" });
        Assert.Equal("CallOut", moved.Program);
        Assert.Equal(new FieldChange("ProveOut", "CallOut"), moved.History![^1].ChangedFields["Program"]);

        var cleared = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Program = "" });
        Assert.Null(cleared.Program);
        Assert.Equal(new FieldChange("CallOut", null), cleared.History![^1].ChangedFields["Program"]);

        var unchanged = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Title = "Crash!" });
        Assert.Null(unchanged.Program);
    }

    // ---- tags ------------------------------------------------------------

    [Fact]
    public void NormalizeTags_SplitsTrimsAndDedupesIgnoringCase()
    {
        var tags = WorkItemService.NormalizeTags(new[] { " ui, login ", "UI", "", null, "smoke;  regression" });
        Assert.Equal(new[] { "ui", "login", "smoke", "regression" }, tags);
    }

    [Fact]
    public void NormalizeTags_RejectsOverlongTags_AndTooMany()
    {
        Assert.Throws<WorkItemValidationException>(() =>
            WorkItemService.NormalizeTags(new[] { new string('x', WorkItemDefaults.TagMaxLength + 1) }));
        Assert.Throws<WorkItemValidationException>(() =>
            WorkItemService.NormalizeTags(Enumerable.Range(0, WorkItemDefaults.MaxTagsPerItem + 1).Select(i => $"t{i}")));
        Assert.Equal(WorkItemDefaults.MaxTagsPerItem,
            WorkItemService.NormalizeTags(Enumerable.Range(0, WorkItemDefaults.MaxTagsPerItem).Select(i => $"t{i}")).Count);
    }

    [Fact]
    public async Task Tags_AreSortedOnTheDto_AndSharedAcrossItems()
    {
        var first = await Items().CreateAsync(Bug(null, "ui", "Login"));
        var second = await Items().CreateAsync(Bug(null, "LOGIN"));

        Assert.Equal(new[] { "Login", "ui" }, first.Tags);
        // The first spelling wins; "LOGIN" reuses the existing tag.
        Assert.Equal(new[] { "Login" }, second.Tags);
        await using var db = _temp.CreateContext();
        Assert.Equal(2, await db.Tags.CountAsync());
        Assert.Equal("Login; ui", first.History![0].ChangedFields["Tags"].New);
    }

    [Fact]
    public async Task UpdateTags_ReplacesTheSet_AndRecordsTheDiff()
    {
        var created = await Items().CreateAsync(Bug(null, "ui", "login"));

        var updated = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Tags = new() { "login", "api" } });

        Assert.Equal(new[] { "api", "login" }, updated.Tags);
        Assert.Equal(new FieldChange("login; ui", "api; login"), updated.History![^1].ChangedFields["Tags"]);
    }

    [Fact]
    public async Task UpdateTags_SameSetInAnotherOrder_IsANoOp()
    {
        var created = await Items().CreateAsync(Bug(null, "ui", "login"));

        var updated = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Tags = new() { "LOGIN", "ui" } });

        Assert.Single(updated.History!);
    }

    [Fact]
    public async Task UpdateTags_EmptyListRemovesAll_NullLeavesThemAlone()
    {
        var created = await Items().CreateAsync(Bug(null, "ui"));

        var untouched = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Title = "Renamed" });
        Assert.Equal(new[] { "ui" }, untouched.Tags);

        var cleared = await Items().UpdateAsync(created.Id, new UpdateWorkItemRequest { Tags = new() });
        Assert.Empty(cleared.Tags);
        Assert.Equal(new FieldChange("ui", null), cleared.History![^1].ChangedFields["Tags"]);
    }

    [Fact]
    public async Task Filters_ByProgramAndTag_IgnoringCase()
    {
        await Items().CreateAsync(Bug("ProveOut", "ui"));
        await Items().CreateAsync(Bug("CallOut", "ui", "api"));
        await Items().CreateAsync(Bug(null, "api"));

        Assert.Single(await Items().ListAsync(new WorkItemQuery { Program = "proveout" }));
        Assert.Equal(2, (await Items().ListAsync(new WorkItemQuery { Tag = "API" })).Count);
        Assert.Single(await Items().ListAsync(new WorkItemQuery { Program = "CallOut", Tag = "ui" }));
        Assert.Empty(await Items().ListAsync(new WorkItemQuery { Tag = "missing" }));
    }

    [Fact]
    public async Task Board_ListsProgramsAndTags_InItsMetadata()
    {
        await Items().CreateAsync(Bug("CallOut", "zeta", "Alpha"));

        var board = await new BoardService(_temp.CreateContext()).GetBoardAsync(new WorkItemQuery());

        Assert.Equal(new[] { "ProveOut", "CallOut" }, board.Metadata.Programs);
        Assert.Equal(new[] { "Alpha", "zeta" }, board.Metadata.Tags);
        var card = Assert.Single(board.Columns[0].Items);
        Assert.Equal("CallOut", card.Program);
    }

    // ---- attachments -----------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0 }, "image/gif")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp")]
    public void DetectImageType_RecognizesSupportedFormats(byte[] bytes, string expected)
    {
        Assert.Equal(expected, AttachmentService.DetectImageType(bytes));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("GIF8")]
    public void DetectImageType_RejectsEverythingElse(string text)
    {
        Assert.Null(AttachmentService.DetectImageType(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public async Task Upload_StoresOnce_AndReturnsMarkdown()
    {
        var first = await Attachments().UploadAsync(new MemoryStream(Png), "screen shot.png");
        var second = await Attachments().UploadAsync(new MemoryStream(Png), "copy.png");

        Assert.Equal(first.Id, second.Id);
        // The duplicate keeps its own name for the alt text.
        Assert.Equal($"![copy.png](api/ui/attachments/{first.Id:D})", second.Markdown);
        Assert.Equal("image/png", first.ContentType);
        Assert.Equal($"api/ui/attachments/{first.Id:D}", first.Url);
        Assert.Equal($"![screen shot.png](api/ui/attachments/{first.Id:D})", first.Markdown);

        var stored = await Attachments().FindAsync(first.Id);
        Assert.Equal(Png, stored!.Content);
        Assert.Equal("Uploader", stored.UploadedBy);
    }

    [Fact]
    public async Task Upload_TrustsTheBytes_NotTheFileName()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>");
        await Assert.ThrowsAsync<WorkItemValidationException>(() => Attachments().UploadAsync(new MemoryStream(svg), "fake.png"));

        var png = await Attachments().UploadAsync(new MemoryStream(Png), "looks-like.svg");
        Assert.Equal("image/png", png.ContentType);
    }

    [Fact]
    public async Task Upload_RejectsEmptyAndOversizedFiles()
    {
        await Assert.ThrowsAsync<WorkItemValidationException>(() => Attachments().UploadAsync(new MemoryStream(), "empty.png"));

        var huge = new byte[WorkItemDefaults.AttachmentMaxBytes + 1];
        Png.CopyTo(huge, 0);
        await Assert.ThrowsAsync<WorkItemValidationException>(() => Attachments().UploadAsync(new MemoryStream(huge), "huge.png"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Desktop\bug.png", "image/png", "bug.png")]
    [InlineData("../../etc/passwd", "image/png", "passwd")]
    [InlineData("a](javascript:x)[.png", "image/png", "ajavascript:x.png")]
    [InlineData("", "image/jpeg", "image.jpg")]
    [InlineData(null, "image/webp", "image.webp")]
    public void CleanFileName_StripsPathsAndMarkdownSyntax(string? input, string type, string expected)
    {
        Assert.Equal(expected, AttachmentService.CleanFileName(input, type));
    }
}
