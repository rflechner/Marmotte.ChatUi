namespace Marmotte.ChatUi.Contracts;

/// <summary>Stable categories suitable for host-localized feedback, without raw provider or storage errors.</summary>
public enum ChatErrorCode
{
    Unauthenticated,
    NotFound,
    InvalidRequest,
    Conflict,
    UnsupportedCapability,
    GenerationFailed,
    StorageUnavailable,
    ModelServerUnavailable,
    ModelRequestRejected,
    ContextLimit,
    ResponseInterrupted
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
        ChatErrorCode.StorageUnavailable => "Storage is unavailable. Reload before retrying.",
        ChatErrorCode.ModelServerUnavailable => "The model server is unavailable or timed out. Check its address, start the server, and test the connection.",
        ChatErrorCode.ModelRequestRejected => "The model server rejected the request. Check the API key, model name and supported settings; load the model on the server, then test the connection.",
        ChatErrorCode.ContextLimit => "The model reached its context or output limit. Start a shorter conversation or adjust the output limit in connection settings.",
        ChatErrorCode.ResponseInterrupted => "The response was interrupted. Saved partial text is retained. Check the connection and send a new message to continue.",
        _ => "The response could not be generated."
    })
    {
        Code = code;
    }

    public ChatErrorCode Code { get; }
}
