using WebApp.Hubs;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// When running from the repo root (e.g. `dotnet run --project WebApp/WebApp.csproj`),
// the content root can be the repo root. Load WebApp-scoped appsettings in that case.
builder.Configuration
    .AddJsonFile(Path.Combine("WebApp", "appsettings.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine("WebApp", $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true);

// Optional local overrides (intentionally git-ignored) for developer-specific secrets.
builder.Configuration
    .AddJsonFile(Path.Combine("WebApp", "appsettings.Local.json"), optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// Session support (used by GitHub OAuth flow)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// HTTP client for GitHub OAuth token exchange
builder.Services.AddHttpClient();

// Register agent and conversation services
builder.Services.AddSingleton<IAgentService, AgentService>();
builder.Services.AddSingleton<IConversationService, ConversationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serve runtime content from wwwroot (e.g., uploaded images under /uploads).
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();

// Map SignalR hub
app.MapHub<ChatHub>("/chathub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
