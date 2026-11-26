using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Marmotte.ChatUi.ServiceDefaults;

/// <summary>Composes host diagnostics, service discovery, and development health endpoints without business dependencies.</summary>
public static class HostingExtensions
{
    /// <summary>Registers process health, runtime/HTTP metrics, HTTP traces, and service discovery for a host's lifetime.</summary>
    /// <typeparam name="TBuilder">The host builder type.</typeparam>
    /// <param name="builder">The host being composed; configuration controls the optional OTLP exporter.</param>
    /// <returns>The same builder for composition.</returns>
    /// <remarks>
    /// No body/custom-header capture, database statement instrumentation, or log export is configured. Query values are redacted by
    /// the instrumentation defaults. Callers must not opt out of that redaction or put sensitive data in URL paths.
    /// HTTP clients have a 30-second timeout and no automatic retries; generation adapters own their timeout policy.
    /// </remarks>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddServiceDiscovery();
            http.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health")
                        && !context.Request.Path.StartsWithSegments("/alive");
                    options.RecordException = false;
                })
                .AddHttpClientInstrumentation(options => options.RecordException = false));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            // Register only these signals: UseOtlpExporter also enables log export implicitly.
            telemetry.WithMetrics(metrics => metrics.AddOtlpExporter())
                .WithTracing(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }

    /// <summary>Maps terse readiness and process-liveness responses in Development only.</summary>
    /// <param name="app">The web host whose registered checks are evaluated.</param>
    /// <returns>The same application.</returns>
    /// <remarks>
    /// /health runs all registered checks; /alive runs only checks tagged live. Failing checks return 503.
    /// Dependency checks belong to their adapters when integrated. No endpoint or diagnostic detail is exposed in other environments.
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
        }

        return app;
    }
}
