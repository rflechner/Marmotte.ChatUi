using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Resolves owned persisted profiles for explicit connection probes and optional model discovery.</summary>
/// <remarks>Scope with the same model adapter as ChatService so successful streaming evidence can be reused.</remarks>
public sealed class ModelConnectionService(ICurrentUser currentUser, IConnectionProfileRepository profiles, IModelConnection connection)
{
    /// <summary>Probes only the chosen text mode using a fixed prompt; never uses conversation history.</summary>
    /// <param name="profileId">The owned persisted profile, resolved anew before the probe.</param>
    /// <param name="streaming">Whether to verify streaming rather than ordinary text generation.</param>
    /// <param name="cancellationToken">Cancels authorization and the provider's bounded probe.</param>
    /// <returns>Success and verified capabilities, or a safe provider failure.</returns>
    /// <exception cref="ChatOperationException">Profile authorization or validation fails.</exception>
    public async Task<ModelConnectionResult> CheckAsync(Guid profileId, bool streaming, CancellationToken cancellationToken = default)
    {
        var profile = await FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        return await connection.CheckAsync(profile, streaming, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Discovers model identifiers; unsupported discovery still permits manual entry.</summary>
    /// <param name="profileId">The owned saved profile whose model endpoint should be queried.</param>
    /// <param name="cancellationToken">Cancels authorization and the provider's bounded discovery call.</param>
    /// <returns>Opaque identifiers and support status, or a safe provider failure.</returns>
    /// <exception cref="ChatOperationException">Profile authorization or validation fails.</exception>
    public async Task<ModelDiscoveryResult> DiscoverAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var profile = await FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        return await connection.DiscoverAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps persisted safe provider failures to actionable presentation categories without exception text.</summary>
    /// <param name="failure">A provider-neutral failure, without raw server details.</param>
    /// <returns>A stable presentation category; unknown values map to GenerationFailed.</returns>
    public static ChatErrorCode ErrorCode(GenerationFailure failure) => failure switch
    {
        GenerationFailure.Unavailable => ChatErrorCode.ModelServerUnavailable,
        GenerationFailure.Rejected => ChatErrorCode.ModelRequestRejected,
        GenerationFailure.ContextLimit => ChatErrorCode.ContextLimit,
        GenerationFailure.Interrupted => ChatErrorCode.ResponseInterrupted,
        _ => ChatErrorCode.GenerationFailed
    };

    /// <summary>Resolves trusted ownership before exposing a profile to a provider adapter.</summary>
    private async Task<ConnectionProfile> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Access.RequireId(id);
        var user = await Access.RequireUserAsync(currentUser, cancellationToken).ConfigureAwait(false);
        var profile = await profiles.FindAsync(user, id, cancellationToken).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.NotFound);
        Access.RequireOwner(user, profile.OwnerId);
        return profile;
    }
}
