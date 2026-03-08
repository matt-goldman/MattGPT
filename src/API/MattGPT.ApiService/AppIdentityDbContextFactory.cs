using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MattGPT.ApiService;

/// <summary>
/// Design-time factory for <see cref="AppIdentityDbContext"/>, used by EF Core tooling.
/// The connection string is read from the <c>MATTGPT_IDENTITY_DB</c> environment variable
/// when running migrations. Falls back to a local dev default if the variable is not set.
/// </summary>
public class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MATTGPT_IDENTITY_DB")
            ?? "Host=localhost;Database=mattgptdb;Username=postgres";

        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppIdentityDbContext(options);
    }
}
