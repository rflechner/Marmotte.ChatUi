using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Infrastructure.OpenAI;

/// <summary>Revalidates identity and persisted connection settings before attaching a user's credential to an endpoint.</summary>
internal sealed class ProfileAccess(ICurrentUser currentUser, IConnectionProfileRepository profiles, IModelCredentialStore credentials)
{
    /// <summary>Rejects anonymous, foreign, missing, or modified profile snapshots before resolving credentials.</summary>
    /// <remarks>Uses trusted identity on every call; caller remains responsible for authorizing conversation history.</remarks>
    internal async Task<ModelCredential?> AuthorizeAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await currentUser.GetUserIdAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.Unauthenticated);
        if (profile.OwnerId != user)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        var stored = await profiles.FindAsync(user, profile.Id, cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.OwnerId != user)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }

        if (stored.Endpoint != profile.Endpoint || stored.Model != profile.Model || stored.UpdatedAt != profile.UpdatedAt
            || stored.Options.Temperature != profile.Options.Temperature || stored.Options.MaxOutputTokens != profile.Options.MaxOutputTokens)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }

        return await credentials.FindAsync(user, profile.Id, cancellationToken).ConfigureAwait(false);
    }
}
