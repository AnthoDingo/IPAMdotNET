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

