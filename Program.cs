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
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error", createScopeForErrors: true); app.UseHsts(); }
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
