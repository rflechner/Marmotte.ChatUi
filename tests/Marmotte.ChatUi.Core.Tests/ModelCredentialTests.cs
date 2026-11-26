using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Domain;

namespace Marmotte.ChatUi.Core.Tests;

/// <summary>Checks credential authorization and safe validation without requiring a database or model server.</summary>
public sealed class ModelCredentialTests
{
    [Fact]
    public async Task Credential_operations_recheck_identity_and_never_accept_an_owner_from_the_request()
    {
        var identity = new TestIdentity(new UserId("alice"));
        var store = new CredentialProbe();
        var service = new ModelCredentialService(identity, store);
        var id = Guid.NewGuid();
        await service.SaveAsync(id, "private-test-value", TestContext.Current.CancellationToken);
        Assert.Equal(identity.User, store.Owner);
        Assert.Equal("[REDACTED]", (await service.ResolveAsync(id, TestContext.Current.CancellationToken))!.ToString());
        identity.User = new UserId("bob");
        await service.DeleteAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal(identity.User, store.Owner);
        identity.User = null;
        foreach (var operation in new Func<Task>[]
        {
            () => service.SaveAsync(id, "value", TestContext.Current.CancellationToken),
            () => service.ResolveAsync(id, TestContext.Current.CancellationToken),
            () => service.DeleteAsync(id, TestContext.Current.CancellationToken)
        })
        {
            var error = await Assert.ThrowsAsync<ChatOperationException>(operation);
            Assert.Equal(ChatErrorCode.Unauthenticated, error.Code);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("private\nvalue")]
    public async Task Invalid_credentials_fail_without_reaching_storage_or_echoing_input(string value)
    {
        var store = new CredentialProbe();
        var service = new ModelCredentialService(new TestIdentity(new UserId("alice")), store);
        var error = await Assert.ThrowsAsync<ChatOperationException>(() => service.SaveAsync(Guid.NewGuid(), value, TestContext.Current.CancellationToken));
        Assert.Equal(ChatErrorCode.InvalidRequest, error.Code);
        Assert.Null(store.Owner);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_credentials_empty_ids_and_cancelled_operations_do_not_reach_storage()
    {
        var store = new CredentialProbe();
        var service = new ModelCredentialService(new TestIdentity(new UserId("alice")), store);
        Assert.Equal(ChatErrorCode.InvalidRequest, (await Assert.ThrowsAsync<ChatOperationException>(() => service.SaveAsync(Guid.NewGuid(), new string('x', 8193), TestContext.Current.CancellationToken))).Code);
        Assert.Equal(ChatErrorCode.InvalidRequest, (await Assert.ThrowsAsync<ChatOperationException>(() => service.ResolveAsync(Guid.Empty, TestContext.Current.CancellationToken))).Code);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync(Guid.NewGuid(), cancellation.Token));
        Assert.Null(store.Owner);
    }

    /// <summary>Records only the trusted owner forwarded by the service; does not model persistence or enforce profile ownership.</summary>
    private sealed class CredentialProbe : IModelCredentialStore
    {
        internal UserId? Owner { get; private set; }
        private ModelCredential? credential;

        public Task<ModelCredential?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken)
        {
            Owner = ownerId;
            return Task.FromResult(credential);
        }

        public Task SaveAsync(UserId ownerId, Guid profileId, ModelCredential value, CancellationToken cancellationToken)
        {
            Owner = ownerId;
            credential = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken)
        {
            Owner = ownerId;
            credential = null;
            return Task.CompletedTask;
        }
    }
}
