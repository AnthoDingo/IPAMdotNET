using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using IPAMdotNet.Localization;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using AnthoDingo.Setup;
using AnthoDingo.Update;
using IPAMdotNet.Api;
using IPAMdotNet.Components;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using System.Threading.RateLimiting;
using IPAMdotNet.Setup;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Écrit par l'assistant /setup (moteur + chaîne de connexion).
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);

builder.Services.AddFileBasedSetup<IpamSetupInitializer>(options =>
{
    options.AllowedProviders = [DbProvider.SqlServer, DbProvider.Postgres, DbProvider.MySql];
    options.AllowUsernameAdmin = true;
    options.ConnectionStringName = "Default";
    // Licence en première page de l'assistant, acceptation obligatoire avant la connexion à la base.
    options.LicenseText = License.Text();
    options.RequireLicenseAcceptance = true;
});
// Après la licence : avertissement de non-affiliation.
builder.Services.AddSetupPreInstallTask<DisclaimerSetupTask>();

string? connectionString = builder.Configuration.GetConnectionString("Default");
bool databaseConfigured = Enum.TryParse(builder.Configuration["Setup:Provider"], out DbProvider provider) && connectionString is not null;
if (databaseConfigured)
{
    // L'utilisateur courant signe les entrées du journal des modifications.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<AppDbContext>(services => AppDbContext.Create(provider, connectionString)
        .WithAuditUser(() => services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User));

    // Migrations en attente après une mise à jour : tout est redirigé vers /update, un admin confirme leur application.
    builder.Services.AddDatabaseUpdate<AppDbContext>(options =>
    {
        options.ProductName = "IPAM.Net";
        options.IconCssClass = "ipam-update-logo";
        options.RequireAuthentication = true;
        options.SignInPath = "/Account/Login";
        options.AuthorizationPolicy = "Admin";
        // Connexion et déconnexion doivent rester accessibles pour changer de compte depuis /update.
        options.ExemptPathPrefixes.Add("/Account");
    });
}

// Clés de protection des données (cookies, mot de passe SMTP chiffré) persistées : sans elles, un redémarrage
// déconnecterait tout le monde et rendrait le mot de passe SMTP illisible. Le dossier est exclu de git.
builder.Services.AddDataProtection()
    .SetApplicationName("IPAMdotNet")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.Cookie.Name = "IPAMdotNet.Auth";
        // Durée fixée à la connexion selon les paramètres serveur (ExpiresUtc) : pas de prolongation glissante.
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"));

// Anti brute-force : 10 tentatives de connexion par minute et par IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Seuls les envois du formulaire comptent : afficher la page de connexion n'est pas une tentative.
    options.AddPolicy("login", context => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter("affichage"));
    // API : 300 requêtes par minute et par adresse IP.
    options.AddPolicy(ApiEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
});

// Agent de scan intégré (désactivable dans Administration › Agents de scan).
builder.Services.AddHostedService<ScanAgent>();

// Reprise unique des libellés des entrées du journal antérieures à leur résolution à l'écriture.
builder.Services.AddHostedService<ChangeLogBackfill>();

// Purge des entrées du journal système plus anciennes que la durée de conservation.
builder.Services.AddHostedService<LogPurge>();

// Uniquement pour la page /update d'AnthoDingo.Update (composant Blazor interactif côté serveur).
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Libellés [Display], messages de validation et noms d'énumération traduits par le catalogue (Localization/L.cs).
CatalogLocalizer catalog = new();
builder.Services.AddSingleton<IStringLocalizerFactory>(catalog);
builder.Services.AddSingleton<IStringLocalizer>(catalog);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AuthorizeFolder("/Administration", "Admin");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
}).AddDataAnnotationsLocalization();

WebApplication app = builder.Build();

app.UseSetupMiddleware("IPAM.Net");

// Après la garde d'installation, avant UseRouting : aucune page ne s'exécute sur un schéma obsolète.
app.UseMigrationsGate();

// Paramètres serveur en cache (titre, fonctionnalités, sécurité), relus après chaque enregistrement et toutes les 30 s.
app.Use(async (context, next) =>
{
    if (context.RequestServices.GetService<AppDbContext>() is { } db)
    {
        try
        {
            await SettingsStore.GetServerAsync(db);
        }
        catch (DbException)
        {
            // Base injoignable : valeurs par défaut, la garde /update affiche l'erreur.
        }
    }
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Exceptions non gérées : écrites dans le journal système, puis traitées par la page d'erreur.
app.UseErrorLogging();

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Langue de l'interface (textes, dates, nombres) : choix de l'utilisateur, sinon langue par défaut du serveur. Après l'authentification
// (claim de langue) et le chargement des paramètres ; /setup et /update, servis avant, restent en français.
CultureInfo[] cultures = [.. L.Languages.Select(l => CultureInfo.GetCultureInfo(l.Code))];
app.UseRequestLocalization(options =>
{
    options.DefaultRequestCulture = new RequestCulture(L.Source);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.RequestCultureProviders =
    [
        new CustomRequestCultureProvider(context => Task.FromResult<ProviderCultureResult?>(
            new ProviderCultureResult(context.User.FindFirstValue(ClaimsExtensions.LanguageClaim) ?? SettingsStore.Server.Language))),
    ];
});
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
// Les API injectent AppDbContext, enregistré seulement une fois la base configurée (avant /setup, il n'existe pas).
if (databaseConfigured)
{
    app.MapIpamApi();
    app.MapAgentApi();
}
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(ServiceCollectionExtensions.UpdateAssembly);

app.Run();

