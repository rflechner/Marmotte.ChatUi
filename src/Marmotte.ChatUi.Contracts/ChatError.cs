namespace Marmotte.ChatUi.Contracts;

/// <summary>Stable categories suitable for host-localized feedback, without raw provider or storage errors.</summary>
public enum ChatErrorCode
{
    Unauthenticated,
    NotFound,
    InvalidRequest,
    Conflict,
    UnsupportedCapability,
    GenerationFailed
}

/// <summary>A safe application failure. NotFound intentionally covers both absent and other users' resources.</summary>
public sealed class ChatOperationException : Exception
{
    public ChatOperationException(ChatErrorCode code) : base(code switch
    {
        ChatErrorCode.Unauthenticated => "Authentication is required.",
        ChatErrorCode.NotFound => "The resource was not found.",
        ChatErrorCode.InvalidRequest => "The request is invalid.",
        ChatErrorCode.Conflict => "The operation conflicts with the current state. Reload before retrying.",
        ChatErrorCode.UnsupportedCapability => "The requested capability is not available.",
        _ => "The response could not be generated."
    })
    {
        Code = code;
    }

    public ChatErrorCode Code { get; }
}
