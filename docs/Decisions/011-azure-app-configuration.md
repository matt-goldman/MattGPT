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
- In **run (local dev) mode**, `RunAsEmulator()` starts the `mcr.microsoft.com/azure-app-configuration/app-configuration-emulator:1.0.0-preview` container. `WithDataVolume()` persists the emulator's state across restarts. Configuration is seeded by the dedicated **`MattGPT.ConfigSeeder`** project, which reads from AppHost's `appsettings.json` and writes into the emulator using **set-if-not-exists semantics** — developer customisations in the emulator are never overwritten on subsequent runs.
- In **publish (azd/Bicep) mode**, the resource maps to a real Azure App Configuration store provisioned automatically by the Aspire Azure provisioner. The same seeding mechanism (via `MattGPT.ConfigSeeder` or CI/CD) can be used to populate non-production stores.
- **Both `apiservice` and `webfrontend`** receive the `"appconfig"` resource via Aspire and call `builder.AddAzureAppConfiguration("appconfig")` at the top of their `Program.cs`. This uses Aspire's client integration to make the store the highest-priority configuration source, falling back gracefully to the service's own `appsettings.json` if the connection string is absent or the store is unavailable.
- The Aspire-generated Ollama connection names (`LLM:ChatConnectionName`, `LLM:EmbeddingConnectionName`) remain as `WithEnvironment` calls because their values are produced at runtime by the resource graph, not configuration.

### Consequences

**Easier:**
- AppHost no longer fans out config to services via environment variables. Adding a new setting requires touching only `appsettings.json` and the `SeededKeys` (or equivalent) list in the `MattGPT.ConfigSeeder` project.
- Deployed environments get a clean, centralised config story: populate the Azure App Configuration store via the Azure portal, CLI, CI/CD pipeline, or `MattGPT.ConfigSeeder`, and all services pick it up automatically.
- Services fall back to their own `appsettings.json` if the Azure App Config store is unavailable or empty — no hard dependency on the store for startup.

**More complex:**
- Local dev now requires the Azure App Configuration emulator container to start and the `MattGPT.ConfigSeeder` to complete seeding before services can read centralised config (mitigated by `WaitFor(appConfig)` on the emulator and the fallback to service-level `appsettings.json`).
- If services start before the seeder has populated the store, they will read from their local `appsettings.json` until the store is populated. On the next restart, the emulator's data volume means seeded values are already present. This is acceptable for a local dev workflow.
- The additional `Azure.Data.AppConfiguration` SDK package is pulled into the `MattGPT.ConfigSeeder` project (rather than AppHost) as the component responsible for seeding.

### Alternatives Considered

1. **Keep environment-variable fan-out, refactor into extension methods.** Cleaner code but doesn't solve the underlying problem — AppHost still owns application config. Rejected.

2. **Service-level `appsettings.json` only.** Simple but requires developers to maintain separate config files per service, and deployed environments still need per-container configuration. Rejected.

3. **Azure Key Vault for secrets (API keys, vector store credentials).** Assessed and explicitly deferred. The reasoning:

   - `AppHost.cs` and the `MattGPT.ConfigSeeder` project are used only for **local development**. No production deployment story has been built yet.
   - If a production deployment story is needed in future, Azure App Configuration natively supports **Key Vault references** — the store entry simply points to a Key Vault secret rather than containing the value directly. This is the correct production pattern and requires no changes to the consumer services.
   - A community Key Vault emulator exists (`azure-keyvault-emulator`) but requires `DisableChallengeResourceVerification = true` in `SecretClientOptions` on the consuming side. This leaks deployment concerns into service code, which is undesirable. The preferred pattern is that services treat the config store as an opaque source and are never aware of whether values come from Key Vault references or plain values.
   - For local dev, secrets (API keys, credentials) in the App Config emulator are acceptable: the emulator data volume is local-only, not shared, and not subject to the same exposure risks as a real secrets store.
   - **Decision:** Seed secrets into the App Config emulator for local dev only. When a production deployment story is built, secrets should be stored in Azure Key Vault with Key Vault references in App Configuration, configured via CI/CD pipeline — the `MattGPT.ConfigSeeder` is not involved in that path.
