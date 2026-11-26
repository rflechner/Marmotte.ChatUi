using Marmotte.ChatUi.ServiceDefaults;
using Marmotte.ChatUi.Web;
using Marmotte.ChatUi.Web.Components;
using Marmotte.ChatUi.Web.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.AddHostIdentity();
builder.AddHostPersistence();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapAccountEndpoints();
app.MapDefaultEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

app.Run();

/// <summary>Exposes the web entry point to in-process host tests.</summary>
public partial class Program;
