using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using LiveLeaderboard.Core.Abstractions;
using LiveLeaderboard.Data.Redis;
using LiveLeaderboard.Data.Scoring;
using LiveLeaderboard.Data.Leaderboards;
using LiveLeaderboard.Web.Realtime;
using LiveLeaderboard.Web.Workers;
using LiveLeaderboard.Web.Components;
using LiveLeaderboard.Web.Components.Account;
using LiveLeaderboard.Data.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Scorer", policy => policy.RequireRole("Scorer", "Admin"));
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect($"{redisConnectionString},abortConnect=false"));
builder.Services.AddSingleton<RedisLeaderboardStore>();
builder.Services.AddScoped<RedisLeaderboardReader>();
builder.Services.AddScoped<RedisProjectionRebuilder>();
builder.Services.AddScoped<ILeaderboardReader>(services => services.GetRequiredService<RedisLeaderboardReader>());
builder.Services.AddScoped<ILeaderboardProjector>(services => services.GetRequiredService<RedisProjectionRebuilder>());
builder.Services.AddScoped<PublicLeaderboardService>();
builder.Services.AddScoped<ICompetitionCatalog>(services => services.GetRequiredService<PublicLeaderboardService>());
builder.Services.AddSingleton<IProjectionWorkStore, ProjectionOutboxStore>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<LeaderboardNotifier>();
builder.Services.AddSingleton<IProjectionNotifier>(services => services.GetRequiredService<LeaderboardNotifier>());
builder.Services.AddHostedService<LeaderboardProjectionWorker>();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services, app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();
app.MapHub<LeaderboardHub>("/hubs/leaderboard");

app.Run();
