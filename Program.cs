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
// Biography drafts must never be served publicly.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/internal/artist-content") &&
        (!app.Environment.IsDevelopment() ||
         context.Connection.RemoteIpAddress is not { } remote ||
         !System.Net.IPAddress.IsLoopback(remote)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
