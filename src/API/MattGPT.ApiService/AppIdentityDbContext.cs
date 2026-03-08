using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MattGPT.ApiService;

/// <summary>
/// EF Core context for ASP.NET Core Identity tables.
/// Used for user authentication when <see cref="AuthOptions.Enabled"/> is <c>true</c>.
/// Backed by the same Postgres instance (when Postgres is the document DB) or
/// a lightweight SQLite database (when MongoDB is the document DB).
/// </summary>
public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
}
