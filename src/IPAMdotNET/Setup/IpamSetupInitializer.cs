using AnthoDingo.Setup;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Setup;

public sealed class IpamSetupInitializer : ISetupInitializer
{
    public async Task InitializeDatabaseAsync(DbProvider provider, string connectionString, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, connectionString);
        await db.Database.MigrateAsync(ct);

        // Types d'équipements par défaut de phpIPAM.
        if (!await db.DeviceTypes.AnyAsync(ct))
        {
            string[] names = ["Commutateur", "Routeur", "Pare-feu", "Concentrateur", "Point d'accès sans fil",
                "Base de données", "Poste de travail", "Ordinateur portable", "Autre"];
            db.DeviceTypes.AddRange(names.Select(name => new DeviceType { Name = name }));
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task CreateAdminAsync(DbProvider provider, string connectionString, AdminAccount admin, CancellationToken ct = default)
    {
        await using AppDbContext db = AppDbContext.Create(provider, connectionString);
        User user = new() { UserName = User.NormalizeUserName(admin.UserName), PasswordHash = "", DisplayName = admin.DisplayName, IsAdmin = true };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, admin.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }
}

