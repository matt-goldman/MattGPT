using LumexUI.Extensions;
using MattGPT.ApiClient;
using MattGPT.Web;
using MattGPT.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// --- Optional authentication ---
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

if (authOptions.Enabled)
{
    builder.Services.AddHttpContextAccessor();

    var isKeycloak = authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

    if (isKeycloak)
    {
        // --- Keycloak path: OIDC with authorization code + PKCE ---
        var keycloakBase = builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["Auth:Keycloak:ServerUrl"]
            ?? "http://localhost:8080";
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
    }
    else
    {
        // --- Legacy Identity path: cookie auth ---
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
            });
    }

    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddTransient<ApiAuthDelegatingHandler>();
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add LumexUI services.
builder.Services.AddLumexServices();

builder.Services.AddOutputCache();

// Register MattGPT API client services (chat, conversations, search, settings).
var mattGptClientBuilder = builder.Services.AddMattGptApiClient(new Uri("https+http://apiservice"));
if (authOptions.Enabled)
{
    mattGptClientBuilder.AddHttpMessageHandler<ApiAuthDelegatingHandler>();
}

// Configure Kestrel for large file uploads (up to 250 MB).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 262_144_000; // 250 MB
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets().AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// --- OIDC challenge/sign-out endpoints for Keycloak path ---
if (authOptions.Enabled && authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase))
{
    // Trigger the OIDC login challenge (redirects the browser to Keycloak).
    app.MapGet("/auth/login-oidc", (HttpContext context, string? returnUrl) =>
    {
        var redirectUri = IsLocalUrl(returnUrl) ? returnUrl! : "/";
        return Results.Challenge(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = redirectUri },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    }).AllowAnonymous();

    // Trigger OIDC sign-out (signs out locally and redirects to Keycloak end-session).
    app.MapPost("/auth/logout-oidc", (HttpContext context) =>
    {
        var authProperties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = "/"
        };

        return Results.SignOut(
            authProperties,
            [
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme
            ]);
    }).RequireAntiforgery();
}

app.MapDefaultEndpoints();

static bool IsLocalUrl(string? url)
{
    if (string.IsNullOrEmpty(url))
    {
        return false;
    }

    // Based on Microsoft.AspNetCore.Mvc.IUrlHelper.IsLocalUrl logic:
    if (url[0] == '/')
    {
        // Allow "/" or "/foo", but not "//" or "/\"
        if (url.Length == 1)
        {
            return true;
        }

        return url[1] != '/' && url[1] != '\\';
    }

    // Allow application-relative URLs like "~/foo"
    if (url.Length > 1 && url[0] == '~' && url[1] == '/')
    {
        return true;
    }

    return false;
}

app.Run();
