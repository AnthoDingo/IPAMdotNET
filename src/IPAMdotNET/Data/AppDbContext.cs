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
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DeviceType> DeviceTypes => Set<DeviceType>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<CircuitProvider> CircuitProviders => Set<CircuitProvider>();
    public DbSet<Circuit> Circuits => Set<Circuit>();
    public DbSet<PstnPrefix> PstnPrefixes => Set<PstnPrefix>();
    public DbSet<PstnNumber> PstnNumbers => Set<PstnNumber>();

    /// <summary>
    /// Vide les références vers un emplacement, un client, un rack, un type ou un équipement avant sa suppression.
    /// Ces clés étrangères sont en NO ACTION côté base : SQL Server refuse les chemins multiples de SET NULL
    /// (ex. emplacement → équipement, directement et via un rack).
    /// </summary>
    public async Task DetachLocationAsync(int id)
    {
        await Subnets.Where(x => x.LocationId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationId, (int?)null));
        await Devices.Where(x => x.LocationId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationId, (int?)null));
        await Racks.Where(x => x.LocationId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationId, (int?)null));
        await Circuits.Where(x => x.LocationAId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationAId, (int?)null));
        await Circuits.Where(x => x.LocationBId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LocationBId, (int?)null));
    }

    public async Task DetachCustomerAsync(int id)
    {
        await Subnets.Where(x => x.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));
        await Devices.Where(x => x.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));
        await Racks.Where(x => x.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));
        await Circuits.Where(x => x.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));
    }

    public async Task DetachRackAsync(int id)
    {
        await Devices.Where(x => x.RackId == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.RackId, (int?)null)
            .SetProperty(x => x.RackStart, (int?)null)
            .SetProperty(x => x.RackSize, (int?)null));
    }

    public async Task DetachDeviceTypeAsync(int id)
    {
        await Devices.Where(x => x.DeviceTypeId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceTypeId, (int?)null));
    }

    public async Task DetachDeviceAsync(int id)
    {
        await PstnPrefixes.Where(x => x.DeviceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceId, (int?)null));
        await PstnNumbers.Where(x => x.DeviceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceId, (int?)null));
    }

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

        // Infrastructure : clés étrangères facultatives en NO ACTION, vidées par les méthodes Detach*Async.
        modelBuilder.Entity<Subnet>().HasOne(s => s.Location).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Subnet>().HasOne(s => s.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasOne(d => d.DeviceType).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(d => d.Location).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(d => d.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(d => d.Rack).WithMany(r => r.Devices).OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<Rack>(entity =>
        {
            entity.HasOne(r => r.Location).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(r => r.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        // Pas de HasData avec des Id explicites : PostgreSQL n'avancerait pas la séquence d'identité.
        // Les données initiales sont insérées par IpamSetupInitializer.
        modelBuilder.Entity<DeviceType>().HasIndex(t => t.Name).IsUnique();

        modelBuilder.Entity<CircuitProvider>().HasIndex(p => p.Name).IsUnique();

        modelBuilder.Entity<Circuit>(entity =>
        {
            entity.HasIndex(c => new { c.ProviderId, c.Cid }).IsUnique();
            entity.HasOne(c => c.Provider).WithMany().OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.LocationA).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(c => c.LocationB).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(c => c.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<PstnPrefix>(entity =>
        {
            entity.HasIndex(p => p.Prefix).IsUnique();
            entity.HasOne(p => p.Device).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<PstnNumber>(entity =>
        {
            entity.HasIndex(n => new { n.PrefixId, n.Number }).IsUnique();
            entity.HasOne(n => n.Prefix).WithMany(p => p.Numbers).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(n => n.Device).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });
    }
}
