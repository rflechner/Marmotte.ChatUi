using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Applies shared authorization and input checks and translates domain failures into safe application errors.</summary>
internal static class Access
{
    /// <summary>Resolves the trusted identity for this operation without retaining it across calls.</summary>
    /// <param name="currentUser">The host-supplied identity adapter.</param>
    /// <param name="cancellationToken">Cancels identity resolution and is checked before invoking the adapter.</param>
    /// <returns>The authenticated user's stable application identity.</returns>
    /// <exception cref="ChatOperationException">The adapter returns no identity; the code is Unauthenticated.</exception>
    /// <exception cref="OperationCanceledException">Identity resolution is cancelled.</exception>
    internal static async Task<UserId> RequireUserAsync(ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await currentUser.GetUserIdAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ChatOperationException(ChatErrorCode.Unauthenticated);
    }

    /// <summary>Rejects a foreign record without revealing whether it exists.</summary>
    /// <param name="expected">The trusted current user's identity.</param>
    /// <param name="actual">The owner returned by the repository.</param>
    /// <exception cref="ChatOperationException">The owners differ; the code is NotFound.</exception>
    internal static void RequireOwner(UserId expected, UserId actual)
    {
        if (expected != actual)
        {
            throw new ChatOperationException(ChatErrorCode.NotFound);
        }
    }

    /// <summary>Rejects an unassigned request identifier before it reaches a repository.</summary>
    /// <exception cref="ChatOperationException">The identifier is empty; the code is InvalidRequest.</exception>
    internal static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }
    }

    /// <summary>Rejects a missing request object using the application's safe error contract.</summary>
    /// <exception cref="ChatOperationException">The value is null; the code is InvalidRequest.</exception>
    internal static void RequireValue<T>(T? value) where T : class
    {
        if (value is null)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }
    }

    /// <summary>Runs a domain operation and translates expected validation or lifecycle failures without exposing their text.</summary>
    /// <typeparam name="T">The domain operation's result type.</typeparam>
    /// <param name="operation">A synchronous domain validation or mutation, not an adapter I/O operation.</param>
    /// <returns>The operation's result when its invariants hold.</returns>
    /// <exception cref="ChatOperationException">Invalid arguments map to InvalidRequest; invalid transitions map to Conflict.</exception>
    /// <remarks>Cancellation and unexpected exceptions propagate unchanged.</remarks>
    internal static T Validate<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (ArgumentException)
        {
            throw new ChatOperationException(ChatErrorCode.InvalidRequest);
        }
        catch (InvalidOperationException)
        {
            throw new ChatOperationException(ChatErrorCode.Conflict);
        }
    }
}
