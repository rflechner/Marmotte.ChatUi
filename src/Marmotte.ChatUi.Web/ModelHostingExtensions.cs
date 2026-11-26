using Marmotte.ChatUi.Application.Ports;
using Marmotte.ChatUi.Infrastructure.OpenAI;

namespace Marmotte.ChatUi.Web;

/// <summary>Composes the model adapter with isolated request credentials and a reusable, credential-free HTTP transport.</summary>
internal static class ModelHostingExtensions
{
    /// <summary>Registers the same adapter instance for all model ports within a user scope; requires persistence and identity.</summary>
    internal static void AddHostModels(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("chatdb")))
        {
            return;
        }

        var seconds = builder.Configuration.GetValue("Models:OperationTimeoutSeconds", 120d);
        builder.Services.AddSingleton(new OpenAIAdapterOptions(TimeSpan.FromSeconds(seconds)));
        builder.Services.AddHttpClient("model-server", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .RemoveAllLoggers();
        builder.Services.AddScoped(services => new OpenAIModelAdapter(
            services.GetRequiredService<IHttpClientFactory>().CreateClient("model-server"),
            services.GetRequiredService<ICurrentUser>(),
            services.GetRequiredService<IConnectionProfileRepository>(),
            services.GetRequiredService<IModelCredentialStore>(),
            services.GetRequiredService<OpenAIAdapterOptions>(),
            services.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<IResponseGenerator>(services => services.GetRequiredService<OpenAIModelAdapter>());
        builder.Services.AddScoped<IModelConnection>(services => services.GetRequiredService<OpenAIModelAdapter>());
        builder.Services.AddScoped<IModelCapabilities>(services => services.GetRequiredService<OpenAIModelAdapter>());
    }
}
