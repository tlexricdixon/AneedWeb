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
// Local diagnostics for Razor route registration and request guarding.
// Never expose route introspection outside local development.
app.MapGet("/internal/route-check", (HttpContext context) =>
{
    var localHost = context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    context.Request.Host.Host is "127.0.0.1" or "::1" or "[::1]";
    if (!app.Environment.IsDevelopment() || !localHost)
        return Results.NotFound();

    var routes = typeof(App).Assembly.GetTypes()
        .SelectMany(type => type.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Components.RouteAttribute), false)
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>()
            .Select(attribute => new { Component = type.FullName, Route = attribute.Template }))
        .Where(route => route.Route.Contains("artist", StringComparison.OrdinalIgnoreCase))
        .OrderBy(route => route.Route)
        .ToArray();

    return Results.Json(new
    {
        Environment = app.Environment.EnvironmentName,
        Host = context.Request.Host.ToString(),
        RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
        Routes = routes
    });
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
