namespace KanbanBoard.Tests.Infrastructure;

/// <summary>Deterministic <see cref="TimeProvider"/> for timestamp assertions.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now;

    public ManualClock(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
