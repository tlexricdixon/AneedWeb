using AneedWeb.Components;
using AneedWeb.Data;
using AneedWeb.Services;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<LibraryRepository>();
builder.Services.AddHttpClient("Live365", c => { c.BaseAddress = new Uri("https://api.live365.com/"); c.Timeout = TimeSpan.FromSeconds(10); });
builder.Services.AddSingleton<BroadcastService>();
var app = builder.Build();
// Keep draft reviews unavailable outside local development.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/internal/artist-content"))
    {
        var localHost = context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        context.Request.Host.Host == "127.0.0.1" ||
                        context.Request.Host.Host == "[::1]" ||
                        context.Request.Host.Host == "::1";
        if (!app.Environment.IsDevelopment() || !localHost)
        {
            app.Logger.LogWarning("Blocked biography preview: environment={Environment}, host={Host}, remote={Remote}",
                app.Environment.EnvironmentName, context.Request.Host, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }

    await next(context);
});
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
