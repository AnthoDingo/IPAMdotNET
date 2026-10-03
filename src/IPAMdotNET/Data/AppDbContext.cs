using AnthoDingo.Setup;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Data;

public abstract class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public static AppDbContext Create(DbProvider provider, string connectionString) => provider switch
    {
        DbProvider.SqlServer => new SqlServerDbContext(new DbContextOptionsBuilder<SqlServerDbContext>().UseSqlServer(connectionString).Options),
        DbProvider.Postgres => new PostgresDbContext(new DbContextOptionsBuilder<PostgresDbContext>().UseNpgsql(connectionString).Options),
        DbProvider.MySql => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseMySQL(connectionString).Options),
        _ => throw new NotSupportedException($"Base de données non supportée : {provider}."),
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.UserName).HasMaxLength(100);
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.Property(u => u.PasswordHash).HasMaxLength(256);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
        });
    }
}
