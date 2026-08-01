using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LeadMine.Infrastructure.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time.
/// <para>
/// Without this, the tooling would have to boot the API host to obtain a context.
/// That host resolves <c>ICurrentUser</c> from an HTTP request, which does not
/// exist during a migration, so migrations would fail. Here the audit dependency
/// is simply omitted — design time never calls SaveChanges.
/// </para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LeadMineDbContext>
{
    public LeadMineDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            // Allows overriding the target database without editing a file.
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=.\\SQLEXPRESS;Database=LeadMineDb;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<LeadMineDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(LeadMineDbContext).Assembly.FullName))
            .Options;

        return new LeadMineDbContext(options);
    }
}
