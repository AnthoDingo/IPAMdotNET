using System.Data.Common;
using AnthoDingo.Setup;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Data;

public abstract partial class AppDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Subnet> Subnets => Set<Subnet>();
    public DbSet<Vlan> Vlans => Set<Vlan>();
    public DbSet<VlanDomain> VlanDomains => Set<VlanDomain>();
    public DbSet<Vrf> Vrfs => Set<Vrf>();
    public DbSet<Nameserver> Nameservers => Set<Nameserver>();
    public DbSet<NatRule> NatRules => Set<NatRule>();
    public DbSet<NatRuleObject> NatRuleObjects => Set<NatRuleObject>();
    public DbSet<BgpPeer> BgpPeers => Set<BgpPeer>();
    public DbSet<BgpPeerSubnet> BgpPeerSubnets => Set<BgpPeerSubnet>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DeviceType> DeviceTypes => Set<DeviceType>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Rack> Racks => Set<Rack>();
    public DbSet<CircuitProvider> CircuitProviders => Set<CircuitProvider>();
    public DbSet<Circuit> Circuits => Set<Circuit>();
    public DbSet<CircuitType> CircuitTypes => Set<CircuitType>();
    public DbSet<LogicalCircuit> LogicalCircuits => Set<LogicalCircuit>();
    public DbSet<LogicalCircuitMember> LogicalCircuitMembers => Set<LogicalCircuitMember>();
    public DbSet<PstnPrefix> PstnPrefixes => Set<PstnPrefix>();
    public DbSet<PstnNumber> PstnNumbers => Set<PstnNumber>();
    public DbSet<ChangeLog> ChangeLogs => Set<ChangeLog>();
    public DbSet<FavoriteSubnet> FavoriteSubnets => Set<FavoriteSubnet>();
    public DbSet<IpRequest> IpRequests => Set<IpRequest>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<SectionPermission> SectionPermissions => Set<SectionPermission>();
    public DbSet<AuthMethod> AuthMethods => Set<AuthMethod>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<IpAddress> IpAddresses => Set<IpAddress>();
    public DbSet<RemoteAgent> RemoteAgents => Set<RemoteAgent>();

    /// <summary>Ajoute une entrée au journal système.</summary>
    public async Task LogAsync(LogSeverity severity, string category, string message, string? userName, string? ipAddress)
    {
        LogEntries.Add(new LogEntry
        {
            Date = DateTime.UtcNow,
            Severity = severity,
            Category = category,
            Message = message.Length > 1000 ? message[..1000] : message,
            UserName = userName,
            IpAddress = ipAddress,
        });
        await SaveChangesAsync();
    }

    /// <summary>
    /// Comme <see cref="LogAsync"/>, sans jamais lever d'exception : utilisé par la connexion/déconnexion, qui s'exécutent
    /// avant l'application des migrations (/update) — la table des journaux peut alors ne pas encore exister.
    /// </summary>
    public async Task TryLogAsync(LogSeverity severity, string category, string message, string? userName, string? ipAddress)
    {
        try
        {
            await LogAsync(severity, category, message, userName, ipAddress);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            ChangeTracker.Clear();
        }
    }

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
        await Vlans.Where(x => x.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));
        await IpAddresses.Where(x => x.CustomerId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, (int?)null));
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
        await IpAddresses.Where(x => x.DeviceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceId, (int?)null));
        await NatRules.Where(x => x.DeviceId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceId, (int?)null));
        await Circuits.Where(x => x.DeviceAId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceAId, (int?)null));
        await Circuits.Where(x => x.DeviceBId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeviceBId, (int?)null));
    }

    public async Task DetachCircuitTypeAsync(int id)
    {
        await Circuits.Where(x => x.TypeId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TypeId, (int?)null));
    }

    /// <summary>
    /// Avant la suppression d'un sous-réseau (et donc de ses adresses, en cascade) : les règles NAT gardent le texte, perdent le lien ;
    /// ses associations aux pairs BGP sont supprimées.
    /// Clés en NO ACTION : SQL Server refuse le double chemin sous-réseau → NAT et sous-réseau → adresse → NAT.
    /// </summary>
    public async Task DetachSubnetAsync(int id)
    {
        await NatRuleObjects.Where(x => x.SubnetId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SubnetId, (int?)null));
        await BgpPeerSubnets.Where(x => x.SubnetId == id).ExecuteDeleteAsync();
        await DetachAddressesAsync(IpAddresses.Where(a => a.SubnetId == id).Select(a => a.Id));
    }

    /// <summary>Avant la suppression d'adresses : les règles NAT gardent le texte, perdent le lien.</summary>
    public async Task DetachAddressesAsync(IQueryable<int> ids)
    {
        await NatRuleObjects.Where(x => x.AddressId != null && ids.Contains(x.AddressId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AddressId, (int?)null));
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

        modelBuilder.Entity<VlanDomain>(entity =>
        {
            entity.HasIndex(d => d.Name).IsUnique();
            entity.HasMany(d => d.Sections).WithMany().UsingEntity("VlanDomainSections");
        });
        modelBuilder.Entity<Vlan>(entity =>
        {
            entity.HasIndex(v => new { v.DomainId, v.Number }).IsUnique();
            // Un domaine qui contient des VLAN ne peut pas être supprimé (les formulaires le refusent avant).
            entity.HasOne(v => v.Domain).WithMany().OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Vrf>().HasIndex(v => v.Name).IsUnique();
        modelBuilder.Entity<BgpPeer>().HasOne(b => b.Vrf).WithMany().OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BgpPeerSubnet>(entity =>
        {
            entity.HasIndex(x => new { x.BgpPeerId, x.SubnetId, x.Direction }).IsUnique();
            entity.HasOne(x => x.BgpPeer).WithMany(p => p.Subnets).OnDelete(DeleteBehavior.Cascade);
            // NO ACTION : supprimées par DetachSubnetAsync (chemins multiples refusés par SQL Server, via les VRF).
            entity.HasOne(x => x.Subnet).WithMany().OnDelete(DeleteBehavior.ClientCascade);
        });
        // NAT : liens en NO ACTION, vidés par DetachSubnetAsync / DetachAddressesAsync / DetachDeviceAsync.
        modelBuilder.Entity<NatRule>().HasOne(n => n.Device).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<NatRuleObject>(entity =>
        {
            entity.HasOne(o => o.NatRule).WithMany(n => n.Objects).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(o => o.Subnet).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(o => o.Address).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        // Infrastructure : clés étrangères facultatives en NO ACTION, vidées par les méthodes Detach*Async.
        modelBuilder.Entity<Subnet>().HasOne(s => s.Location).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Subnet>().HasOne(s => s.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasOne(d => d.DeviceType).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(d => d.Location).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(d => d.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(d => d.Rack).WithMany(r => r.Devices).OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasMany(d => d.Sections).WithMany().UsingEntity("DeviceSections");
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
            entity.HasOne(c => c.DeviceA).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(c => c.DeviceB).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(c => c.Type).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
            entity.HasOne(c => c.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<CircuitType>().HasIndex(t => t.Name).IsUnique();

        modelBuilder.Entity<Vlan>().HasOne(v => v.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<IpAddress>().HasOne(a => a.Customer).WithMany().OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<LogicalCircuit>().HasIndex(l => l.Cid).IsUnique();

        modelBuilder.Entity<LogicalCircuitMember>(entity =>
        {
            entity.HasIndex(m => new { m.LogicalCircuitId, m.CircuitId }).IsUnique();
            entity.HasOne(m => m.LogicalCircuit).WithMany(l => l.Members).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.Circuit).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PstnPrefix>(entity =>
        {
            entity.HasIndex(p => p.Prefix).IsUnique();
            entity.HasOne(p => p.Device).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        // Outils
        modelBuilder.Entity<ChangeLog>(entity =>
        {
            entity.HasIndex(c => c.Date);
            entity.HasIndex(c => new { c.EntityType, c.EntityId });
        });

        modelBuilder.Entity<FavoriteSubnet>(entity =>
        {
            entity.HasIndex(f => new { f.UserId, f.SubnetId }).IsUnique();
            entity.HasOne(f => f.User).WithMany().OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(f => f.Subnet).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IpRequest>(entity =>
        {
            entity.HasIndex(r => r.State);
            entity.HasOne(r => r.Subnet).WithMany().OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.RequestedBy).WithMany().HasForeignKey(r => r.RequestedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.ProcessedBy).WithMany().HasForeignKey(r => r.ProcessedById).OnDelete(DeleteBehavior.Restrict);
        });

        // Maintenance
        modelBuilder.Entity<CustomField>(entity =>
        {
            entity.Property(f => f.EntityType).HasMaxLength(50);
            entity.HasIndex(f => new { f.EntityType, f.Name }).IsUnique();
        });

        modelBuilder.Entity<CustomFieldValue>(entity =>
        {
            entity.HasIndex(v => new { v.FieldId, v.EntityId }).IsUnique();
            entity.HasIndex(v => v.EntityId);
            entity.HasOne(v => v.Field).WithMany().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LogEntry>().HasIndex(l => l.Date);

        // Serveur : utilisateurs, groupes, permissions, authentification, API
        modelBuilder.Entity<User>().HasOne(u => u.AuthMethod).WithMany().OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasIndex(g => g.Name).IsUnique();
            entity.HasMany(g => g.Users).WithMany(u => u.Groups).UsingEntity("UserGroups");
        });
        modelBuilder.Entity<SectionPermission>(entity =>
        {
            entity.HasKey(p => new { p.SectionId, p.GroupId });
            entity.HasOne(p => p.Section).WithMany().OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Group).WithMany().OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AuthMethod>().HasIndex(a => a.Name).IsUnique();
        modelBuilder.Entity<ApiKey>().HasIndex(k => k.KeyHash).IsUnique();
        modelBuilder.Entity<ApiKey>().HasOne(k => k.User).WithMany().OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RemoteAgent>().HasIndex(a => a.KeyHash).IsUnique();
        // Agent supprimé : ses sous-réseaux reviennent à l'agent intégré (chemin unique, SET NULL accepté par SQL Server).
        modelBuilder.Entity<Subnet>().HasOne(s => s.ScanAgent).WithMany().OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<IpAddress>(entity =>
        {
            entity.Property(a => a.Address).HasMaxLength(16);
            entity.HasIndex(a => new { a.SubnetId, a.Address }).IsUnique();
            entity.HasOne(a => a.Subnet).WithMany().OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.Tag).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(a => a.Device).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(t => t.Name).IsUnique();
            entity.Property(t => t.BackgroundColor).HasMaxLength(7);
            entity.Property(t => t.TextColor).HasMaxLength(7);
        });

        modelBuilder.Entity<PstnNumber>(entity =>
        {
            entity.HasIndex(n => new { n.PrefixId, n.Number }).IsUnique();
            entity.HasOne(n => n.Prefix).WithMany(p => p.Numbers).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(n => n.Device).WithMany().OnDelete(DeleteBehavior.ClientSetNull);
        });
    }
}
