using Marmotte.ChatUi.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace Marmotte.ChatUi.Web.Tests;

/// <summary>Guards the telemetry boundary so enabling diagnostics cannot implicitly forward application log contents.</summary>
public sealed class DiagnosticsTests
{
    [Fact]
    public void Enabling_OTLP_does_not_register_a_log_export_provider()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317";
        builder.AddServiceDefaults();
        using var host = builder.Build();

        Assert.DoesNotContain(host.Services.GetServices<ILoggerProvider>(), provider => provider is OpenTelemetryLoggerProvider);
    }
}
