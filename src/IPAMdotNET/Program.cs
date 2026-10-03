using AnthoDingo.Setup;
using IPAMdotNet.Data;
using System.Threading.RateLimiting;
using IPAMdotNet.Setup;
using Microsoft.AspNetCore.Authentication.Cookies;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Écrit par l'assistant /setup (moteur + chaîne de connexion).
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);

builder.Services.AddFileBasedSetup<IpamSetupInitializer>(options =>
{
    options.AllowedProviders = [DbProvider.SqlServer, DbProvider.Postgres, DbProvider.MySql];
    options.AllowUsernameAdmin = true;
    options.ConnectionStringName = "Default";
});
builder.Services.AddSetupStep<LicenseSetupStep>();

string? connectionString = builder.Configuration.GetConnectionString("Default");
if (Enum.TryParse(builder.Configuration["Setup:Provider"], out DbProvider provider) && connectionString is not null)
{
    builder.Services.AddScoped<AppDbContext>(_ => AppDbContext.Create(provider, connectionString));
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.Cookie.Name = "IPAMdotNet.Auth";
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"));

// Anti brute-force : 10 tentatives de connexion par minute et par IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AuthorizeFolder("/Administration", "Admin");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

WebApplication app = builder.Build();

app.UseSetupMiddleware("IPAMdotNet");

// Interface en français quelle que soit la culture du serveur (formats de nombres et de dates).
app.UseRequestLocalization("fr-FR");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

