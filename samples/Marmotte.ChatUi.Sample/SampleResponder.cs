using System.Runtime.CompilerServices;
using Marmotte.ChatUi.Contracts;

namespace Marmotte.ChatUi.Sample;

/// <summary>A deterministic, credential-free substitute for a model stream. It never sends or persists conversation text.</summary>
public sealed class SampleResponder
{
    /// <summary>Streams a fixed demonstration response with optional partial failure, observing cancellation between words.</summary>
    /// <param name="conversationId">Identifier included in the demonstration updates.</param>
    /// <param name="messageId">Identifier of the response snapshot being updated.</param>
    /// <param name="fail">Whether to finish with a safe failure after retaining a partial response.</param>
    /// <param name="cancellationToken">Stops simulated work; the caller decides the final UI state.</param>
    /// <returns>Ordered deltas and a terminal update; no infrastructure is required.</returns>
    public async IAsyncEnumerable<GenerationUpdate> StreamAsync(Guid conversationId, Guid messageId, bool fail,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string response = "A little space for your next idea. This response comes from the sample service. "
            + "Your application supplies the messages and actions; Marmotte takes care of the conversation surface. "
            + "Try another theme, stop this response, or explore the attachment states below.";
        var index = 0;
        foreach (var word in response.Split(' '))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(110), cancellationToken);
            if (fail && index++ == 12)
            {
                yield return new GenerationUpdate(conversationId, messageId, string.Empty, GenerationStatus.Failed, ChatErrorCode.GenerationFailed);
                yield break;
            }

            yield return new GenerationUpdate(conversationId, messageId, word + " ", GenerationStatus.Streaming);
        }

        yield return new GenerationUpdate(conversationId, messageId, string.Empty, GenerationStatus.Completed);
    }
}
