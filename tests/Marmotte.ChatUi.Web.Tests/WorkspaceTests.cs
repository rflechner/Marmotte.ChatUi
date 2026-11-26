using Bunit;
using Marmotte.ChatUi.Application;
using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Contracts;
using Marmotte.ChatUi.Core.Tests;
using Marmotte.ChatUi.Domain;
using Marmotte.ChatUi.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Exercises settings feedback across delayed parent renders without requiring a server or a model credential.</summary>
public sealed class WorkspaceTests
{
    [Fact]
    public async Task Saving_a_keyless_profile_retains_confirmation_after_the_parent_refreshes_its_selected_profile()
    {
        using var context = new BunitContext();
        var identity = new TestIdentity(new UserId("alice"));
        var store = new TestStore();
        var profiles = new ConnectionProfileService(identity, store, new TestClock());
        var original = await profiles.CreateAsync(new("Original", new Uri("http://localhost/api"), "original"), TestContext.Current.CancellationToken);
        context.Services.AddSingleton<IConnectionProfileService>(profiles);
        context.Services.AddSingleton(new ModelCredentialService(identity, new UnusedCredentials()));
        context.Services.AddSingleton(new ModelConnectionService(identity, store, new UnusedConnection()));
        context.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        Guid savedId = Guid.Empty;
        var cut = context.Render<ConnectionSettings>(parameters => parameters
            .Add(component => component.Profile, original)
            .Add(component => component.OnSaved, (Guid id) => savedId = id));
        cut.FindAll("button").Single(button => button.TextContent == "New connection").Click();
        cut.Find("input[maxlength='100']").Change("Keyless");
        cut.Find("input[type='url']").Change("http://localhost/v1");
        cut.Find("input[list]").Change("local-model");
        await cut.Find("form").SubmitAsync();
        Assert.NotEqual(Guid.Empty, savedId);
        var saved = await profiles.LoadAsync(savedId, TestContext.Current.CancellationToken);
        cut.Render(parameters => parameters.Add(component => component.Profile, saved));
        Assert.Equal("Connection saved. You can now test it or send a message.", cut.Find("[role='status']").TextContent);
        Assert.Empty(cut.Find("[role='alert']").TextContent);
        Assert.Equal(2, (await profiles.ListAsync(new PageRequest(), TestContext.Current.CancellationToken)).Items.Count);
    }

    /// <summary>Fails if saving ordinary keyless settings unexpectedly reads or writes a credential.</summary>
    private sealed class UnusedCredentials : IModelCredentialStore
    {
        public Task<ModelCredential?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(UserId ownerId, Guid profileId, ModelCredential credential, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Fails if saving a profile silently triggers discovery or consumes inference resources.</summary>
    private sealed class UnusedConnection : IModelConnection
    {
        public Task<ModelDiscoveryResult> DiscoverAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ModelConnectionResult> CheckAsync(ConnectionProfile profile, bool streaming, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
