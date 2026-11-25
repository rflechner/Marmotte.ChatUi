using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Marmotte.ChatUi.Web.Tests;

public sealed class HostTests
{
    [Fact]
    public async Task Home_prerenders_without_external_services_and_enables_server_interactivity()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Marmotte.ChatUi", html, StringComparison.Ordinal);
        Assert.Contains("not production-ready", html, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"server\"", html, StringComparison.Ordinal);
        Assert.Matches("_framework/blazor\\.web(?:\\.[a-z0-9]+)?\\.js", html);
    }

    [Theory]
    [InlineData("/app.css", "text/css")]
    [InlineData("/images/marmotte-chatui-mascot.png", "image/png")]
    public async Task Host_serves_its_static_assets(string path, string mediaType)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_route_returns_not_found()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/missing-page", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        });
    }
}
