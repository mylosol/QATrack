using KanbanBoard.Api.Models;

namespace KanbanBoard.Api.Services;

/// <summary>Derives <see cref="DiscussionStatus"/> (1.9.0).</summary>
public static class Discussion
{
    /// <summary>
    /// A human comment newer than the last agent comment is awaiting an agent;
    /// otherwise an agent comment newer than the last human comment and the
    /// last human read is an unread reply.
    /// </summary>
    public static DiscussionStatus? StatusOf(DateTime? lastHumanComment, DateTime? lastAgentComment, DateTime? humanReadAt)
    {
        if (lastHumanComment is not null && (lastAgentComment is null || lastHumanComment > lastAgentComment))
        {
            return DiscussionStatus.AwaitingAgent;
        }

        if (lastAgentComment is not null &&
            (lastHumanComment is null || lastAgentComment > lastHumanComment) &&
            (humanReadAt is null || lastAgentComment > humanReadAt))
        {
            return DiscussionStatus.UnreadReply;
        }

        return null;
    }

    /// <inheritdoc cref="StatusOf(DateTime?, DateTime?, DateTime?)"/>
    public static DiscussionStatus? StatusOf(WorkItem item) =>
        StatusOf(item.LastHumanCommentAt, item.LastAgentCommentAt, item.HumanReadAt);

    /// <summary>Server-side (SQL) equivalent of <see cref="StatusOf(WorkItem)"/>.</summary>
    public static IQueryable<WorkItem> Where(IQueryable<WorkItem> source, DiscussionStatus status) => status switch
    {
        DiscussionStatus.AwaitingAgent => source.Where(w =>
            w.LastHumanCommentAt != null &&
            (w.LastAgentCommentAt == null || w.LastHumanCommentAt > w.LastAgentCommentAt)),
        DiscussionStatus.UnreadReply => source.Where(w =>
            w.LastAgentCommentAt != null &&
            (w.LastHumanCommentAt == null || w.LastAgentCommentAt > w.LastHumanCommentAt) &&
            (w.HumanReadAt == null || w.LastAgentCommentAt > w.HumanReadAt)),
        _ => source,
    };
}
