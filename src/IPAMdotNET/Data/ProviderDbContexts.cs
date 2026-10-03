using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Data;

// Un contexte par moteur pour avoir un jeu de migrations par moteur (Migrations/<Moteur>).
// Les constructeurs sans paramètre et chaînes factices ne servent qu'à `dotnet ef` (design-time).

public class SqlServerDbContext : AppDbContext
{
    public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : base(options) { }
    public SqlServerDbContext() : base(new DbContextOptionsBuilder<SqlServerDbContext>().UseSqlServer("Server=design").Options) { }
}

public class PostgresDbContext : AppDbContext
{
    public PostgresDbContext(DbContextOptions<PostgresDbContext> options) : base(options) { }
    public PostgresDbContext() : base(new DbContextOptionsBuilder<PostgresDbContext>().UseNpgsql("Host=design").Options) { }
}

public class MySqlDbContext : AppDbContext
{
    public MySqlDbContext(DbContextOptions<MySqlDbContext> options) : base(options) { }
    public MySqlDbContext() : base(new DbContextOptionsBuilder<MySqlDbContext>().UseMySQL("Server=design").Options) { }
}
