# 046 — Switch to Azure App Configuration Service

**Status:** Done
**Sequence:** 46
**Depends on:** None

---

## Problem

`Orchestration/MattGPT.AppHost/AppHost.cs` carries an absurd amount of configuration responsibility. It reads every application-level setting (LLM provider, model IDs, API keys, endpoints, auth config, vector store config, etc.) from its own `appsettings.json`, then fans all of those values out to child services via repeated `.WithEnvironment()` calls. This is not what an orchestration host should be doing:

- AppHost becomes a "config dispatcher" rather than an infrastructure orchestrator.
- Adding or removing a setting requires changes in three places: AppHost's `appsettings.json`, `AppHost.cs` (to read it), and `AppHost.cs` again (to pass it).
- Deployed environments have no clean way to centralise config — each service would have to be individually configured with the same values.

## Solution

Introduce **Azure App Configuration** as the single source of truth for application-level settings. Aspire 13 ships `Aspire.Hosting.Azure.AppConfiguration`, which:

- Provisions a real Azure App Configuration store for deployed scenarios.
- Runs the Azure App Configuration emulator (container: `mcr.microsoft.com/azure-app-configuration/app-configuration-emulator`) for local development — no Azure subscription needed for local dev.

### Architecture

```
AppHost.cs
  ├── Provisions infrastructure: MongoDB, Postgres, Qdrant, Ollama, Keycloak …
  ├── Provisions Azure App Configuration (emulator in run mode)
  ├── Seeds the emulator with values from AppHost appsettings.json on first run
  └── Passes WithReference(appConfig) to apiservice + webfrontend

apiservice / webfrontend
  ├── Receive an Azure App Configuration resource named "appconfig" from Aspire
  └── Call builder.AddAzureAppConfiguration("appconfig") early in Program.cs
      → reads LLM, auth, DB, vector store, RAG settings from the config store
      → falls back to own appsettings.json if store is empty or unreachable
```

### Config ownership split

| Config category | Lives in | Read by |
|---|---|---|
| Infrastructure decisions (which DB/VS/auth to provision) | AppHost `appsettings.json` | AppHost.cs only |
| Application config (LLM, auth flags, RAG, VS endpoints…) | Azure App Configuration | apiservice + webfrontend (via SDK) |
| Aspire resource connection names (`LLM:ChatConnectionName`) | Aspire `WithEnvironment` (runtime-generated) | apiservice |

---

## Acceptance Criteria

- [x] `Aspire.Hosting.Azure.AppConfiguration` added to AppHost; configuration seeding is handled by the `MattGPT.ConfigSeeder` project (no `Azure.Data.AppConfiguration`-based seeding in AppHost).
- [x] `Microsoft.Extensions.Configuration.AzureAppConfiguration` added to `MattGPT.ApiService` and `MattGPT.Web`.
- [x] `AppHost.cs` provisions Azure App Configuration with `RunAsEmulator()` + `WithDataVolume()` in run mode.
- [x] The configuration store is seeded with all application-level config values by `MattGPT.ConfigSeeder` over HTTP, using set-if-not-exists semantics (developer customisations are preserved across restarts).
- [x] `AppHost.cs` wires services to the store via `WithReference(appConfig)` rather than individual `WithEnvironment` calls for app-level settings.
- [x] Both `Program.cs` files add `AddAzureAppConfiguration` as the first configuration step so that Azure App Config values override local `appsettings.json`.
- [x] All tests pass (`dotnet test MattGPT.slnx`).
- [x] `docs/UserGuides/configuration.md` updated.
- [x] ADR created (`docs/Decisions/011-azure-app-configuration.md`).
