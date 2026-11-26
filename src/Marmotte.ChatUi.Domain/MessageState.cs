namespace Marmotte.ChatUi.Domain;

/// <summary>The persisted lifecycle of a message; terminal assistant states retain partial content.</summary>
public enum MessageState
{
    Pending,
    Streaming,
    Completed,
    Cancelled,
    Failed
}

/// <summary>The speaker of a persisted conversation message.</summary>
public enum MessageRole
{
    User,
    Assistant
}

/// <summary>A safe failure category. Provider exception text and credentials must never be stored here.</summary>
public enum GenerationFailure
{
    Unavailable,
    Rejected,
    ContextLimit,
    Interrupted,
    Unknown
}
