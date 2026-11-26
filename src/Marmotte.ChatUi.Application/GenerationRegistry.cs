using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Application;

/// <summary>Coordinates generation and recovery across user scopes in a single host process, without retaining history or credentials.</summary>
/// <remarks>Register as a singleton. This is not a distributed lease; multiple Web processes sharing a database are unsupported.</remarks>
public sealed class GenerationRegistry
{
    private readonly Dictionary<(UserId Owner, Guid Conversation), Lease> active = [];
    private readonly object sync = new();

    /// <summary>Exclusively reserves an already authorized conversation until the returned lease is disposed.</summary>
    /// <remarks>A busy conversation yields Conflict. The caller owns the lease and must release it even after failed storage writes.</remarks>
    internal Lease Acquire(UserId owner, Guid conversation)
    {
        lock (sync)
        {
            if (active.ContainsKey((owner, conversation)))
            {
                throw new ChatOperationException(ChatErrorCode.Conflict);
            }

            var cancellation = new CancellationTokenSource();
            var lease = new Lease(this, owner, conversation, cancellation);
            active.Add((owner, conversation), lease);
            return lease;
        }
    }

    /// <summary>Requests cancellation only within the authenticated owner's active conversation.</summary>
    internal Task CancelAsync(UserId owner, Guid conversation)
    {
        lock (sync)
        {
            if (active.TryGetValue((owner, conversation), out var lease))
            {
                lease.Cancel();
                return lease.Completion.Task;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Owns one exclusive reservation and its cancellation source; disposal releases it exactly once.</summary>
    internal sealed class Lease(GenerationRegistry registry, UserId owner, Guid conversation, CancellationTokenSource cancellation) : IDisposable
    {
        internal CancellationToken Token => cancellation.Token;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool disposed;

        /// <summary>Signals the owning iterator; the registry lock prevents a race with source disposal.</summary>
        internal void Cancel() => cancellation.Cancel();

        public void Dispose()
        {
            lock (registry.sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                registry.active.Remove((owner, conversation));
                cancellation.Dispose();
                Completion.TrySetResult();
            }
        }
    }
}
