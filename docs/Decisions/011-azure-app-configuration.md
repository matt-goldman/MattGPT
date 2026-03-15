## ADR-011: Azure App Configuration for Centralised Settings

**Date:** 2026-03-15
**Status:** Accepted
**Related Issues:** [046-azure-app-configuration.md](../Backlog/Done/046-azure-app-configuration.md)

### Context

`AppHost.cs` was acting as a "configuration dispatcher": it read every application-level setting from its own `appsettings.json`, then fanned those values out to child services via a large number of `.WithEnvironment()` calls (roughly 16 environment variables per run). This caused several problems:

1. **Wrong responsibility.** The AppHost is an infrastructure orchestrator, not a config management layer.
2. **Maintenance friction.** Adding or removing a setting required changes in three places: AppHost `appsettings.json`, the read-side of `AppHost.cs`, and the `WithEnvironment` call site.
3. **Deployed scenarios lacked a clean story.** Without a centralised config service, each deployed container had to be individually configured with the same values.

### Decision

Introduce **Azure App Configuration** as the single source of truth for all application-level settings.

- **AppHost** provisions an `AzureAppConfigurationResource` named `"appconfig"`.
- In **run (local dev) mode**, `RunAsEmulator()` starts the `mcr.microsoft.com/azure-app-configuration/app-configuration-emulator:1.0.0-preview` container. `WithDataVolume()` persists the emulator's state across restarts. On first start, an `OnResourceReady` callback seeds the emulator with values from AppHost's own `appsettings.json` using **set-if-not-exists semantics** — developer customisations are never overwritten.
- In **publish (azd/Bicep) mode**, the resource maps to a real Azure App Configuration store provisioned automatically by the Aspire Azure provisioner.
- **Both `apiservice` and `webfrontend`** receive the store's connection string via `WithReference(appConfig)` and call `builder.Configuration.AddAzureAppConfiguration(...)` at the top of their `Program.cs`. This makes the store the highest-priority configuration source, falling back gracefully to the service's own `appsettings.json` if the connection string is absent or the store is unavailable.
- The Aspire-generated Ollama connection names (`LLM:ChatConnectionName`, `LLM:EmbeddingConnectionName`) remain as `WithEnvironment` calls because their values are produced at runtime by the resource graph, not configuration.

### Consequences

**Easier:**
- AppHost no longer fans out config to services via environment variables. Adding a new setting requires touching only `appsettings.json` and the seeder dictionary in `AppHost.cs`.
- Deployed environments get a clean, centralised config story: populate the Azure App Configuration store via the Azure portal, CLI, or CI/CD pipeline, and all services pick it up automatically.
- Services fall back to their own `appsettings.json` if the Azure App Config store is unavailable or empty — no hard dependency on the store for startup.

**More complex:**
- Local dev now requires the Azure App Configuration emulator container to start before services can read centralised config (mitigated by `WaitFor(appConfig)` and the fallback to service-level `appsettings.json`).
- There is a first-start race between the `OnResourceReady` seeder and dependent service startup. Services that start concurrently with seeding will read from their local `appsettings.json` until the store is populated. On the next restart, the emulator's data volume means seeded values are already present. This is acceptable for a local dev workflow.
- The additional `Azure.Data.AppConfiguration` SDK package is pulled into the AppHost project.

### Alternatives Considered

1. **Keep environment-variable fan-out, refactor into extension methods.** Cleaner code but doesn't solve the underlying problem — AppHost still owns application config. Rejected.

2. **Service-level `appsettings.json` only.** Simple but requires developers to maintain separate config files per service, and deployed environments still need per-container configuration. Rejected.

3. **Azure Key Vault for secrets, keep env vars for non-secrets.** Solves the secrets problem but not the general config fan-out. Considered as a complementary future step.
