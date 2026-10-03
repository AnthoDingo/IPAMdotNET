using AnthoDingo.Setup;
using AnthoDingo.Update;
using IPAMdotNet.Components;
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
    // L'utilisateur courant signe les entrées du journal des modifications.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<AppDbContext>(services => AppDbContext.Create(provider, connectionString)
        .WithAuditUser(services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));

    // Migrations en attente après une mise à jour : tout est redirigé vers /update, un admin confirme leur application.
    builder.Services.AddDatabaseUpdate<AppDbContext>(options =>
    {
        options.ProductName = "IPAMdotNet";
        options.RequireAuthentication = true;
        options.SignInPath = "/Account/Login";
        options.AuthorizationPolicy = "Admin";
        // Connexion et déconnexion doivent rester accessibles pour changer de compte depuis /update.
        options.ExemptPathPrefixes.Add("/Account");
    });
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

// Uniquement pour la page /update d'AnthoDingo.Update (composant Blazor interactif côté serveur).
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

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

// Après la garde d'installation, avant UseRouting : aucune page ne s'exécute sur un schéma obsolète.
app.UseMigrationsGate();

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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(ServiceCollectionExtensions.UpdateAssembly);

app.Run();

