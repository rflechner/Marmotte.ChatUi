using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Verifies the minimal web host's prerendering, interactivity markers, static assets, and missing-route behavior.</summary>
public sealed class HostTests
{
    [Fact]
    public async Task Public_home_renders_without_external_services_or_an_anonymous_circuit()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Marmotte.ChatUi", html, StringComparison.Ordinal);
        Assert.Contains("not production-ready", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\"type\":\"server\"", html, StringComparison.Ordinal);
        Assert.Matches("_framework/blazor\\.web(?:\\.[a-z0-9]+)?\\.js", html);
    }

    [Theory]
    [InlineData("/app.css", "text/css")]
    [InlineData("/_content/Marmotte.ChatUi.Components/marmotte-chatui.css", "text/css")]
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

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Development_health_endpoints_report_process_health(string path)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failed_dependency_blocks_readiness_without_failing_liveness_or_leaking_details()
    {
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck("dependency", () => HealthCheckResult.Unhealthy("private diagnostic"))));
        using var client = factory.CreateClient();
        using var readiness = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var liveness = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("Unhealthy", await readiness.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Production_does_not_expose_development_health_endpoints(string path)
    {
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Creates a development host with ephemeral data protection so tests do not read or write persistent key material.</summary>
    /// <returns>A host factory that the calling test must dispose asynchronously.</returns>
    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        });
    }
}
