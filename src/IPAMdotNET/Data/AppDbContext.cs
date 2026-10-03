using AnthoDingo.Setup;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Data;

public abstract class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Subnet> Subnets => Set<Subnet>();
    public DbSet<Vlan> Vlans => Set<Vlan>();
    public DbSet<Vrf> Vrfs => Set<Vrf>();
    public DbSet<Nameserver> Nameservers => Set<Nameserver>();
    public DbSet<NatRule> NatRules => Set<NatRule>();
    public DbSet<BgpPeer> BgpPeers => Set<BgpPeer>();

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

        modelBuilder.Entity<Section>().HasIndex(s => s.Name).IsUnique();

        modelBuilder.Entity<Subnet>(entity =>
        {
            entity.Property(s => s.Address).HasMaxLength(16);
            entity.HasIndex(s => new { s.SectionId, s.Address, s.PrefixLength }).IsUnique();
            entity.HasOne(s => s.Section).WithMany(s => s.Subnets).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(s => s.Vlan).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(s => s.Vrf).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(s => s.Nameserver).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Vlan>().HasIndex(v => v.Number).IsUnique();
        modelBuilder.Entity<Vrf>().HasIndex(v => v.Name).IsUnique();
        modelBuilder.Entity<BgpPeer>().HasOne(b => b.Vrf).WithMany().OnDelete(DeleteBehavior.SetNull);
    }
}
