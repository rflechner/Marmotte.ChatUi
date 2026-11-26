namespace Marmotte.ChatUi.Contracts;

/// <summary>A zero-based offset page, limited to 100 records. Invalid values throw ArgumentOutOfRangeException.</summary>
public sealed record PageRequest
{
    public PageRequest(int offset = 0, int limit = 25)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        Offset = offset;
        Limit = limit;
    }

    public int Offset { get; }
    public int Limit { get; }
}

/// <summary>A detached page; HasMore indicates another page existed at read time. No total count is promised.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMore);
