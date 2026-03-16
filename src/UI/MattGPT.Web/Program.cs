using Duende.AccessTokenManagement.OpenIdConnect;
using LumexUI.Extensions;
using MattGPT.ApiClient;
using MattGPT.ApiClient.Services;
using MattGPT.Web;
using MattGPT.Web.Auth.Keycloak;
using MattGPT.Web.Auth.NetCoreId;
using MattGPT.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
var builder = WebApplication.CreateBuilder(args);

// --- Azure App Configuration ---
// Uses the Aspire client integration which automatically handles emulator connections
// (anonymous auth) and production connections (DefaultAzureCredential) based on the
// connection string injected by the AppHost.
builder.AddAzureAppConfiguration("appconfig");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// --- Optional authentication ---
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

// Register MattGPT API client services (chat, conversations, search, settings).
// The failure handler type depends on the auth path chosen below.
IHttpClientBuilder mattGptClientBuilder;

if (authOptions.Enabled)
{
    builder.Services.AddHttpContextAccessor();

    var isKeycloak = authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

    if (isKeycloak)
    {
        mattGptClientBuilder = builder.Services.AddMattGptApiClient<KeycloakAuthFailureHandler>(new Uri("https+http://apiservice"));
        builder.AddKeycloakAuthentication();
        mattGptClientBuilder.AddUserAccessTokenHandler();
    }
    else
    {
        mattGptClientBuilder = builder.Services.AddMattGptApiClient<NetCoreIdAuthFailureHandler>(new Uri("https+http://apiservice"));

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

        builder.Services.AddTransient<NetCoreIdAuthDelegatingHandler>();
        mattGptClientBuilder.AddHttpMessageHandler<NetCoreIdAuthDelegatingHandler>();
    }

    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
    builder.Services.AddCascadingAuthenticationState();
}
else
{
    mattGptClientBuilder = builder.Services.AddMattGptApiClient<NoOpAuthFailureHandler>(new Uri("https+http://apiservice"));
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add LumexUI services.
builder.Services.AddLumexServices();

builder.Services.AddOutputCache();


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
    app.UseKeycloak();
}

app.MapDefaultEndpoints();


app.Run();
