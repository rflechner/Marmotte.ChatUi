using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application.Ports;

/// <summary>Resolves trusted server identity for each operation, returning null for an anonymous/expired session.</summary>
/// <remarks>Host-scoped; never cache identity across users or derive it from a submitted owner field.</remarks>
public interface ICurrentUser
{
    ValueTask<UserId?> GetUserIdAsync(CancellationToken cancellationToken);
}
