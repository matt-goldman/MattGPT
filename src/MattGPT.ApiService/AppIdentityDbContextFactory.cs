using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MattGPT.ApiService;

/// <summary>
/// Design-time factory for <see cref="AppIdentityDbContext"/>, used by EF Core tooling.
/// </summary>
public class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=mattgptdb;Username=postgres")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
