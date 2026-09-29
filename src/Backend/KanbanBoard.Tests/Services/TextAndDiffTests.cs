using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;

namespace KanbanBoard.Tests.Services;

public class TextSanitizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  hello  ", "hello")]
    [InlineData("a\r\nb", "ab")]
    [InlineData("tab\there", "tabhere")]
    public void SingleLine_NormalizesInput(string? input, string? expected)
    {
        Assert.Equal(expected, TextSanitizer.SingleLine(input, 100));
    }

    [Fact]
    public void SingleLine_EnforcesMaxLength()
    {
        Assert.Equal("abc", TextSanitizer.SingleLine("abcdef", 3));
    }

    [Fact]
    public void MultiLine_KeepsNewlinesAndTabs_StripsOtherControls()
    {
        Assert.Equal("a\nb\n\tc", TextSanitizer.MultiLine("a\r\nb\r\tc\u0000", 100));
    }

    [Fact]
    public void MultiLine_BlankIsNull()
    {
        Assert.Null(TextSanitizer.MultiLine(" \n\t ", 100));
    }
}

public class WorkItemChangeTrackerTests
{
    private static readonly WorkItemSnapshot Baseline = new(
        "Title", "Desc", WorkItemType.Bug, WorkItemState.New, 2, "3 - Medium", null, @"Tools\QA", "Current");

    [Fact]
    public void Diff_IdenticalSnapshots_IsEmpty()
    {
        Assert.Empty(WorkItemChangeTracker.Diff(Baseline, Baseline with { }));
    }

    [Fact]
    public void Diff_ReportsOnlyChangedFields()
    {
        var after = Baseline with { State = WorkItemState.Closed, Priority = 1 };

        var diff = WorkItemChangeTracker.Diff(Baseline, after);

        Assert.Equal(2, diff.Count);
        Assert.Equal(new FieldChange("New", "Closed"), diff["State"]);
        Assert.Equal(new FieldChange("2", "1"), diff["Priority"]);
    }

    [Fact]
    public void Diff_Creation_ListsNonEmptyValuesWithNullOld()
    {
        var diff = WorkItemChangeTracker.Diff(WorkItemSnapshot.Empty, Baseline, isCreation: true);

        Assert.All(diff.Values, change => Assert.Null(change.Old));
        Assert.False(diff.ContainsKey("AssignedTo"));
        Assert.Equal("Title", diff["Title"].New);
    }

    [Fact]
    public void Diff_TruncatesVeryLongValues()
    {
        var huge = new string('x', WorkItemChangeTracker.MaxAuditValueLength + 500);
        var diff = WorkItemChangeTracker.Diff(Baseline, Baseline with { Description = huge });

        Assert.EndsWith("(truncated)", diff["Description"].New);
        Assert.True(diff["Description"].New!.Length < huge.Length);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        var diff = WorkItemChangeTracker.Diff(Baseline, Baseline with { AssignedTo = "Kim" });
        var json = WorkItemChangeTracker.Serialize(diff);

        Assert.Contains("\"old\":null", json);
        Assert.Equal(new FieldChange(null, "Kim"), WorkItemChangeTracker.Deserialize(json)["AssignedTo"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void Deserialize_CorruptJson_ReturnsEmpty(string? json)
    {
        Assert.Empty(WorkItemChangeTracker.Deserialize(json));
    }
}
