using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace MattGPT.Web.Auth.Keycloak;

public static class AppExtensions
{
    public static WebApplicationBuilder AddKeycloakAuthentication(this WebApplicationBuilder builder)
    {
       // --- Keycloak path: OIDC with authorization code + PKCE ---
        var keycloakBase = builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["Auth:Keycloak:ServerUrl"]
            ?? builder.Configuration["KEYCLOAK_HTTPS"] ?? throw new InvalidOperationException("Keycloak server URL must be provided via configuration.");
        var keycloakRealm = builder.Configuration["Auth:Keycloak:Realm"] ?? "mattgpt";
        var keycloakAuthority = $"{keycloakBase.TrimEnd('/')}/realms/{keycloakRealm}";
        var oidcClientId = builder.Configuration["Auth:Keycloak:ClientId"] ?? "mattgpt-web";

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;
        })
        .AddOpenIdConnect(options =>
        {
            options.Authority = keycloakAuthority;
            options.ClientId = oidcClientId;
            options.ResponseType = "code";
            options.SaveTokens = true;
            options.UsePkce = true;
            options.RequireHttpsMetadata = true;
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            // Preserve raw OIDC/JWT claim names (e.g. "sub", "email") rather than
            // mapping them to WS-Federation types like ClaimTypes.NameIdentifier.
            options.MapInboundClaims = false;
            // Only relax issuer validation in local development where the issuer may vary per host.
            options.TokenValidationParameters.ValidateIssuer = !builder.Environment.IsDevelopment();
        });
        
        builder.Services.AddOpenIdConnectAccessTokenManagement();

        return builder;
    }

    public static WebApplication UseKeycloak(this WebApplication app)
    {
        // Trigger the OIDC login challenge (redirects the browser to Keycloak).
        app.MapGet("/auth/login-oidc", (HttpContext context, string? returnUrl) =>
        {
            var redirectUri = returnUrl.IsLocalUrl() ? returnUrl! : "/";
            return Results.Challenge(
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = redirectUri },
                [OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        // Trigger OIDC sign-out (signs out locally and redirects to Keycloak end-session).
        app.MapGet("/auth/logout-oidc", async (HttpContext context) =>
        {
            // Retrieve the id_token so Keycloak's end-session endpoint can identify the session.
            var idToken = await context.GetTokenAsync("id_token");

            var authProperties = new AuthenticationProperties
            {
                RedirectUri = "/"
            };

            if (idToken is not null)
            {
                authProperties.SetParameter("id_token_hint", idToken);
            }

            return Results.SignOut(
                authProperties,
                [
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    OpenIdConnectDefaults.AuthenticationScheme
                ]);
        }).AllowAnonymous();

        return app;
    }
}