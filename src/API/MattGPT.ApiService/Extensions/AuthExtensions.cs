using System.Security.Claims;
using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MattGPT.ApiService.Extensions;

/// <summary>
/// Extensions for adding authentication services to the host builder
/// </summary>
public static class AuthExtensions
{
    private static AuthOptions? _authOptions;
    private static DocumentDbOptions? _documentDbOptions;
    
    /// <param name="builder"></param>
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Parses authentication options from config and adds authentication services to the builder
        /// </summary>
        /// <returns><see cref="WebApplicationBuilder"/></returns>
        public WebApplicationBuilder AddOptionalAuthentication ()
        {
            // --- Optional authentication ---
            builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
            _authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();


            if (!_authOptions.Enabled) return builder;
        
            var isKeycloak = _authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

            if (isKeycloak)
            {
                // --- Keycloak path: validate JWTs issued by the Keycloak realm ---
                var keycloakBase = builder.Configuration.GetConnectionString("keycloak")
                                   ?? builder.Configuration["Auth:Keycloak:ServerUrl"]
                                   ?? builder.Configuration["KEYCLOAK_HTTPS"]
                                   ?? "https://localhost:8080";
                var keycloakRealm = builder.Configuration["Auth:Keycloak:Realm"] ?? "mattgpt";
                var keycloakAuthority = $"{keycloakBase.TrimEnd('/')}/realms/{keycloakRealm}";

                builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Authority = keycloakAuthority;
                        options.Audience = builder.Configuration["Auth:Keycloak:Audience"] ?? "account";

                        if (builder.Environment.IsDevelopment())
                        {
                            options.TokenValidationParameters.ValidateIssuer = false; // Dev tunnel doesn't use host header forwarding, so issuer name doesn't match authority.
                        }

                        options.RequireHttpsMetadata = true;
                        options.TokenValidationParameters.NameClaimType = ClaimTypes.NameIdentifier;
                    });

                builder.Services.AddAuthorizationBuilder()
                    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                        .RequireAuthenticatedUser()
                        .Build());
            }
            else
            {
                // --- Legacy Identity path ---

                // ASP.NET Core Identity with standard API endpoints.
                builder.Services.AddIdentityApiEndpoints<IdentityUser>()
                    .AddEntityFrameworkStores<AppIdentityDbContext>();
            
                _documentDbOptions = builder.Configuration.GetSection(DocumentDbOptions.SectionName).Get<DocumentDbOptions>() ?? new DocumentDbOptions();

                switch (_authOptions.UseDocumentDbForAuth)
                {
                    // Identity backing store: driven by AuthOptions settings.
                    case true when _documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase):
                        builder.AddNpgsqlDbContext<AppIdentityDbContext>("mattgptdb");
                        break;
                    case false when _authOptions.AuthDbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase):
                        builder.AddNpgsqlDbContext<AppIdentityDbContext>("mattgpt-identity-db");
                        break;
                    default:
                    {
                        if (_authOptions.UseDocumentDbForAuth && !_documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine(
                                $"[WARNING] Document DB provider '{_documentDbOptions.Provider}' does not support bundled Identity storage; " +
                                "falling back to SQLite for auth.");
                        }
                        builder.Services.AddDbContext<AppIdentityDbContext>(options =>
                            options.UseSqlite("Data Source=mattgpt-identity.db"));
                        break;
                    }
                }

                // Trusted header scheme for Blazor BFF → API service-to-service calls.
                builder.Services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, ServiceToServiceAuthHandler>(
                        ServiceToServiceAuthHandler.SchemeName, null);

                builder.Services.AddAuthorizationBuilder()
                    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                        .AddAuthenticationSchemes(
                            IdentityConstants.BearerScheme,
                            IdentityConstants.ApplicationScheme,
                            ServiceToServiceAuthHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .Build());
            }

            return builder;
        }
    }

    /// <param name="app"></param>
    extension(WebApplication app)
    {
        /// <summary>
        /// Maps optional authentication middleware.
        /// </summary>
        /// <returns><see cref="WebApplication"/></returns>
        public WebApplication UseOptionalAuthentication()
        {
            if (_authOptions is null || !_authOptions.Enabled) return app;
            
            app.UseAuthentication();
            app.UseAuthorization();
            
            // if using Keycloak, we are done - auth is enabled
            if (_authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase)) return app;

            if (_documentDbOptions is null)
            {
                throw new InvalidOperationException("DocumentDbOptions must be configured to use legacy authentication.");
            }
            
            // Legacy Identity endpoints — not needed when using Keycloak.
            var authGroup = app.MapGroup("/auth");
            authGroup.MapIdentityApi<IdentityUser>().AllowAnonymous();
            authGroup.MapGet("/me", (HttpContext context) =>
            {
                var user = context.User;
                
                if (user.Identity?.IsAuthenticated != true)
                    return Results.Unauthorized();
                
                return Results.Ok(new
                {
                    id      = user.FindFirstValue(ClaimTypes.NameIdentifier),
                    email   = user.FindFirstValue(ClaimTypes.Email),
                });
            }).RequireAuthorization();

            // Ensure Identity database schema exists.
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
            if (_documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
                db.Database.Migrate();
            else
                db.Database.EnsureCreated();


            return app;
        }
    }
}