using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SprintSync.Api.Tenancy;

namespace SprintSync.Api.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the model
/// without running <c>Program</c> (no live DB or tenant needed for scaffolding).
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=SprintSync;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options, new TenantContext());
    }
}
