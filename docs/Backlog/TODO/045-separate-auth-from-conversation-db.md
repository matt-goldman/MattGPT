# 045 — Separate Auth Backing Store from Document DB Provider

**Status:** TODO  
**Depends on:** 040 (Optional Authentication with User-Scoped Data)

---

## Summary

Authentication (ASP.NET Core Identity) is currently coupled to the document DB provider selection: if `DocumentDb:Provider = Postgres`, Identity uses the same Postgres database; otherwise it silently falls back to SQLite. This makes it impossible to configure them independently, and there is no support for external identity providers.

This issue decouples authentication configuration from the document DB configuration, adds explicit options for bundling or separating them, and introduces Keycloak as a supported external identity provider via Aspire.

---

## Current State

In `Program.cs` the auth backing-store selection is a hard-coded branch:

```csharp
if (documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    builder.AddNpgsqlDbContext<AppIdentityDbContext>("mattgptdb");
else
    builder.Services.AddDbContext<AppIdentityDbContext>(options =>
        options.UseSqlite("Data Source=mattgpt-identity.db"));
```

`AuthOptions` only has a single `bool Enabled` property. There is no way to:
- Select Postgres as the auth store independently of the document DB selection.
- Use SQLite explicitly while also using Postgres as the document DB.
- Use an external identity provider like Keycloak.

---

## Problem

1. Users who want MongoDB as their document store and Postgres for auth (or vice versa) cannot achieve this.
2. Adding a new document DB provider automatically changes the auth backing store — surprising and brittle.
3. There is no path to Keycloak or any other external OIDC provider.

---

## Requirements

### 1. `Auth:UseDocumentDbForAuth` option

Add a boolean `Auth:UseDocumentDbForAuth` configuration option (default: `true`).

- When `true`, the auth backing store is derived from `DocumentDb:Provider`, same as today — but only for supported providers. Currently supported: `Postgres`. For all other document DB providers, fall back to SQLite with a logged warning.
- When `false`, the auth backing store is controlled independently via `Auth:AuthDbProvider` (see below).

This preserves backward compatibility: existing `Postgres`-document-DB deployments continue to bundle the auth tables in the same database by default.

### 2. Graceful fallback for unsupported providers

When `Auth:UseDocumentDbForAuth = true` and the selected document DB provider does not support ASP.NET Core Identity EF Core stores (i.e. anything other than `Postgres`), the application must:

1. Log a warning at startup:  
   `"Document DB provider '{provider}' does not support bundled Identity storage; falling back to SQLite for auth."`
2. Use SQLite (`mattgpt-identity.db`) as the auth backing store.
3. Continue to start normally — this must not be a fatal error.

### 3. Independent `Auth:AuthDbProvider` option

When `Auth:UseDocumentDbForAuth = false`, the auth backing store is selected via `Auth:AuthDbProvider`.

- Supported values: `"SQLite"` (default), `"Postgres"`.
- If `Auth:AuthDbProvider = Postgres`, the AppHost must provision (or reuse) a Postgres resource for auth and wire it to the API service. It is valid to share the same Postgres instance as the document DB, but both configs may also point to independent Postgres instances.
- If `Auth:AuthDbProvider` is omitted or unrecognised, default to `SQLite`.

### 4. External identity provider: Keycloak

Add a new `Auth:Provider` configuration option (default: `"Identity"`).

#### `Auth:Provider = Identity` (default)

Existing behaviour, with the improvements from requirements 1–3 above. ASP.NET Core Identity handles registration, login, logout, and token issuance.

#### `Auth:Provider = Keycloak`

1. No ASP.NET Core Identity registration takes place.
2. The API service configures JWT Bearer authentication against the Keycloak OIDC discovery endpoint.
3. In `AppHost.cs`:
   - Provision a Keycloak container using Aspire's Keycloak hosting integration (use Aspire MCP tooling to identify the correct NuGet package and API).
   - Pass the Keycloak connection string / authority URL to the API service as an environment variable (`Auth__Keycloak__Authority` or equivalent).
4. The Blazor Web frontend configures OIDC login against the same Keycloak instance.
5. `Auth:UseDocumentDbForAuth` and `Auth:AuthDbProvider` are ignored when `Auth:Provider = Keycloak`.

---

## Configuration Reference

After this issue the full `Auth` section looks like:

```json
"Auth": {
  "Enabled": true,
  "Provider": "Identity",
  "UseDocumentDbForAuth": true,
  "AuthDbProvider": "SQLite"
}
```

| Setting | Type | Default | Notes |
|---------|------|---------|-------|
| `Auth:Enabled` | bool | `false` | Existing setting. No change. |
| `Auth:Provider` | string | `"Identity"` | `"Identity"` or `"Keycloak"`. |
| `Auth:UseDocumentDbForAuth` | bool | `true` | Only applies when `Provider = Identity`. Bundles auth tables with document DB when `true`. |
| `Auth:AuthDbProvider` | string | `"SQLite"` | Only applies when `Provider = Identity` and `UseDocumentDbForAuth = false`. Supported: `"SQLite"`, `"Postgres"`. |

---

## Design

### `AuthOptions.cs`

```csharp
public class AuthOptions
{
    public const string SectionName = "Auth";

    public bool Enabled { get; set; }

    /// <summary>
    /// Identity provider backend. "Identity" = ASP.NET Core Identity (default).
    /// "Keycloak" = external OIDC provider provisioned via Aspire.
    /// </summary>
    public string Provider { get; set; } = "Identity";

    /// <summary>
    /// When true (default), the auth backing store uses the same database as DocumentDb:Provider
    /// if that provider is supported; otherwise falls back to SQLite.
    /// Only applies when Provider = "Identity".
    /// </summary>
    public bool UseDocumentDbForAuth { get; set; } = true;

    /// <summary>
    /// Explicit auth backing-store provider when UseDocumentDbForAuth = false.
    /// Supported: "SQLite" (default), "Postgres".
    /// Only applies when Provider = "Identity".
    /// </summary>
    public string AuthDbProvider { get; set; } = "SQLite";
}
```

### `Program.cs` — Identity registration logic

Replace the current hard-coded branch with a helper that consults the new options:

```
Auth:Enabled = true AND Provider = "Identity"
  UseDocumentDbForAuth = true
    DocumentDb:Provider = "Postgres"  → AddNpgsqlDbContext<AppIdentityDbContext>("mattgptdb")
    Anything else                     → warn + UseSqlite("mattgpt-identity.db")
  UseDocumentDbForAuth = false
    AuthDbProvider = "Postgres"       → AddNpgsqlDbContext<AppIdentityDbContext>("mattgpt-identity-db")
    AuthDbProvider = "SQLite" / other → UseSqlite("mattgpt-identity.db")

Auth:Enabled = true AND Provider = "Keycloak"
  → AddAuthentication().AddJwtBearer(...) using Auth:Keycloak:Authority
  → No Identity registration
```

When `Auth:AuthDbProvider = Postgres` and `UseDocumentDbForAuth = false`, the Aspire connection name for the auth DB is `"mattgpt-identity-db"` (a distinct name to avoid collision if Postgres is also used as the document DB under `"mattgptdb"`).

### `AppHost.cs` — Provisioning

Read additional config values at the top of `AppHost.cs`:

```csharp
var authProvider   = builder.Configuration["Auth:Provider"] ?? "Identity";
var useDocDbForAuth = bool.TryParse(builder.Configuration["Auth:UseDocumentDbForAuth"], out var v) ? v : true;
var authDbProvider  = builder.Configuration["Auth:AuthDbProvider"] ?? "SQLite";
```

Provisioning logic:

- If `Auth:Provider = Keycloak` → provision Keycloak container; pass authority URL to apiservice.
- If `Auth:Provider = Identity`, `UseDocumentDbForAuth = false`, `AuthDbProvider = Postgres`:
  - If a `postgresDb` resource already exists (document DB or vector store), reuse it and add a `"mattgpt-identity-db"` database to the same server (or reuse it if the names align).
  - Otherwise provision a new Postgres server resource specifically for auth and add the `"mattgpt-identity-db"` database.
  - Pass the reference/wait to `apiService`.

Pass new config values to `apiService` via `WithEnvironment`:

```csharp
apiService
    .WithEnvironment("Auth__Provider", authProvider)
    .WithEnvironment("Auth__UseDocumentDbForAuth", useDocDbForAuth.ToString())
    .WithEnvironment("Auth__AuthDbProvider", authDbProvider);
```

### Keycloak Web UI configuration

When `Auth:Provider = Keycloak`, the Blazor Web project must:
- Register OIDC authentication middleware against the Keycloak authority URL.
- Redirect unauthenticated users to Keycloak login rather than a local `/login` page.
- The logout flow must redirect to Keycloak's end-session endpoint.

Use Aspire MCP tooling to identify the correct NuGet packages for both the Aspire Keycloak hosting integration and the ASP.NET Core OIDC client.

---

## Files Likely Affected

| File | Change |
|------|--------|
| `src/API/MattGPT.ApiService/AuthOptions.cs` | Add `Provider`, `UseDocumentDbForAuth`, `AuthDbProvider` properties |
| `src/API/MattGPT.ApiService/Program.cs` | Replace hard-coded auth-store branch with new options-driven logic; add Keycloak JWT Bearer path |
| `Orchestration/MattGPT.AppHost/AppHost.cs` | Read new config; provision Keycloak and/or independent auth Postgres when required; pass env vars |
| `src/UI/MattGPT.Web/Program.cs` | Add OIDC middleware path for Keycloak |
| `docs/UserGuides/configuration.md` | Document the new `Auth` configuration options |

---

## Acceptance Criteria

- [ ] `Auth:UseDocumentDbForAuth = true` with `DocumentDb:Provider = Postgres` uses the existing Postgres database for Identity — no behavioural change from today.
- [ ] `Auth:UseDocumentDbForAuth = true` with `DocumentDb:Provider = MongoDB` (or any non-Postgres provider) logs a warning and uses SQLite. App starts and authenticates successfully.
- [ ] `Auth:UseDocumentDbForAuth = false`, `Auth:AuthDbProvider = SQLite` uses SQLite for Identity regardless of the document DB provider.
- [ ] `Auth:UseDocumentDbForAuth = false`, `Auth:AuthDbProvider = Postgres` provisions (or reuses) a Postgres resource for Identity in Aspire and connects the API service to it. Identity tables are created in that database.
- [ ] `Auth:Provider = Keycloak` provisions a Keycloak container in the Aspire AppHost, no ASP.NET Core Identity is registered, and the API service validates JWTs against Keycloak's OIDC endpoint.
- [ ] `Auth:Provider = Keycloak` redirects unauthenticated Blazor users to Keycloak login.
- [ ] `Auth:Enabled = false` continues to work with zero regressions regardless of any Provider/UseDocumentDbForAuth/AuthDbProvider setting.
- [ ] All existing tests pass (`dotnet test MattGPT.slnx`).
- [ ] `docs/UserGuides/configuration.md` updated with the new `Auth` settings table.
