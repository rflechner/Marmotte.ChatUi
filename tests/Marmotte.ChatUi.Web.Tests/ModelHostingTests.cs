using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Core.Tests;
using Marmotte.ChatUi.Domain;
using Marmotte.ChatUi.Infrastructure.OpenAI;
using Marmotte.ChatUi.ServiceDefaults;
using Marmotte.ChatUi.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Verifies host composition shares capability evidence within one scope while isolating different users' scopes.</summary>
public sealed class ModelHostingTests
{
    [Fact]
    public void Model_ports_share_one_scoped_adapter_and_override_the_default_http_timeout()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddServiceDefaults();
        builder.Configuration["ConnectionStrings:chatdb"] = "test-only";
        builder.Configuration["Models:OperationTimeoutSeconds"] = "180";
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICurrentUser>(_ => new TestIdentity(new UserId("test-user")));
        builder.Services.AddScoped<IConnectionProfileRepository, TestStore>();
        builder.Services.AddScoped<IModelCredentialStore, UnusedCredentials>();
        builder.AddHostModels();
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var generator = first.ServiceProvider.GetRequiredService<IResponseGenerator>();
        Assert.Same(generator, first.ServiceProvider.GetRequiredService<IModelConnection>());
        Assert.Same(generator, first.ServiceProvider.GetRequiredService<IModelCapabilities>());
        Assert.NotSame(generator, second.ServiceProvider.GetRequiredService<IResponseGenerator>());
        Assert.Equal(TimeSpan.FromSeconds(180), provider.GetRequiredService<OpenAIAdapterOptions>().OperationTimeout);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("model-server");
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    /// <summary>Fails if this composition-only test accidentally tries to read or mutate a real credential.</summary>
    private sealed class UnusedCredentials : IModelCredentialStore
    {
        public Task<ModelCredential?> FindAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(UserId ownerId, Guid profileId, ModelCredential credential, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(UserId ownerId, Guid profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
