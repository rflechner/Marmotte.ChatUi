using System.ClientModel.Primitives;
using Marmotte.ChatUi.Application.Ports;

namespace Marmotte.ChatUi.Infrastructure.OpenAI;

/// <summary>Applies only this operation's optional model credential; keyless servers receive no Authorization header.</summary>
/// <remarks>Owned by one SDK client and never cached or registered as a singleton. No synthetic key is needed.</remarks>
internal sealed class ProfileAuthenticationPolicy(ModelCredential? credential) : AuthenticationPolicy
{
    /// <summary>Sets authentication on the operation's request before forwarding it synchronously.</summary>
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Authenticate(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    /// <summary>Sets authentication on the operation's request before forwarding it asynchronously.</summary>
    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Authenticate(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    /// <summary>Never shares headers through HttpClient defaults and never logs the credential.</summary>
    private void Authenticate(PipelineMessage message)
    {
        if (credential is not null)
        {
            message.Request.Headers.Set("Authorization", "Bearer " + credential.Value);
        }
    }
}
