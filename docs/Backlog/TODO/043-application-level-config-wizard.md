# 043 — Application-Level First-Run Configuration Wizard

**Status:** TODO
**Sequence:** 43
**Dependencies:** 040 (optional authentication), 029 (system prompt and profile UI)

---

## Summary

Implement an application-level configuration wizard in the Blazor web UI, replacing the previously planned Aspire-level approach (issue 014, now superseded). The wizard is shown on first run when configuration is not already present in the environment or database, and guides the user through LLM provider/model selection, vector store selection, and authentication setup.

## Background

Issue 014 explored an Aspire-level configuration wizard but this approach is fundamentally blocked: Aspire's `BeforeStartEvent` / `IInteractionService` deadlocks because the dashboard URL is not emitted until after startup completes, so users can never reach the prompt. Additionally, Aspire cannot dynamically register container resources after `Build()` is called, and the intent to publish a Docker image (issue 042) means the application needs to be self-configuring at the app level anyway.

**Issue 014 is superseded by this issue.** See the [notes on 014](#notes-on-superseded-issue-014) below.

## Config Resolution Order

The application resolves configuration using the following priority chain at startup:

1. **Environment variables** — if all required config is present (injected by Aspire, a compose file, or manually), skip the wizard entirely and start normally.
2. **Database config** — if DB connection config is present in env vars but LLM/vector store settings are absent, attempt to load them from the database. If DB config itself is not in env vars, throw a clear exception at startup with instructions.
3. **First-run wizard** — if DB is reachable but no application config is found there, show the first-run wizard on the next HTTP request.

This means:
- The document DB connection string is always required in the environment (it cannot be stored in itself).
- Everything else (LLM, vector store, auth) can be configured via either env vars or the wizard.
- Users running via the provided `docker-compose` files get DB config pre-wired; they only need to complete the in-browser wizard for LLM and auth settings.

## Requirements

### Config Resolution Service

1. Create a `ConfigurationStateService` (or equivalent) that evaluates config completeness at startup:
   - Reads known config keys from `IConfiguration`.
   - Checks the database for stored config if env-var config is incomplete.
   - Exposes a `bool IsConfigurationComplete { get; }` property and a `bool FirstRunWizardRequired { get; }` property.

2. If DB connection string is absent from the environment, throw an `InvalidOperationException` with a clear, human-readable message explaining what to set.

3. If config is loaded from the database, merge it into the runtime `IConfiguration` (or equivalent options objects) so the rest of the application is unaware of the source difference.

### First-Run Wizard UI

4. Add a `/setup` Blazor page (or a redirect-intercepting layout component) that is only shown when `FirstRunWizardRequired == true`.

5. Any request to a non-setup page while first-run is required should redirect to `/setup`.

6. The wizard should be a multi-step flow with the following steps:

   **Step 1 — LLM Provider**
   - Let the user choose from the supported providers (Foundry Local, Ollama, OpenAI, Azure OpenAI, Anthropic, Gemini).
   - Collect the relevant connection details (endpoint URL, API key, model name) for the chosen provider.
   - Provide a "Test connection" button that calls the API to validate the settings before saving.

   **Step 2 — Vector Store**
   - Let the user choose from the supported vector store providers (Qdrant, Postgres/pgvector, Azure AI Search, Pinecone, Weaviate).
   - Collect the relevant connection details.
   - Provide a "Test connection" button.

   **Step 3 — Authentication**
   - Toggle: enable or disable authentication.
   - If enabling auth: prompt to create the first admin user (username + password).
   - If disabling auth: show a prominent warning that all uploaded data will be publicly accessible to anyone who can reach the URL, and that data uploaded in the unauthenticated context will not be accessible to named users if auth is enabled later.
   - The user must explicitly acknowledge the warning (checkbox) before proceeding with auth disabled.

   **Step 4 — Summary and Finish**
   - Show a summary of chosen settings (mask secrets).
   - "Finish" saves all settings to the database and clears the `FirstRunWizardRequired` flag.
   - Redirect to the home page.

### Settings Persistence

7. Store wizard-collected settings in the database using a new `AppSettings` collection/table.
8. Settings must be keyed so they can be loaded and merged into `IConfiguration` at startup.
9. API secrets must be stored encrypted at rest (use ASP.NET Core Data Protection or equivalent).

### Existing Env-Var Config Path

10. If all required config is provided via environment variables, the wizard is never shown. The `/setup` page redirects to home.
11. Environment variables always take precedence over database-stored config.

## Design Notes

- The `/setup` wizard is separate from the existing settings page (issue 029). The settings page is for day-to-day changes; the wizard is for initial setup only.
- Consider whether the existing `/settings` page should surface a link to re-run the wizard (e.g. to change provider) or whether that's out of scope for now.
- The "Test connection" API endpoints can be new minimal API endpoints that instantiate the relevant client, make a trivial request (e.g. list models), and return success/failure.

## Acceptance Criteria

- [ ] If all required config is in environment variables, the application starts normally with no wizard shown.
- [ ] If DB connection is missing from environment variables, startup throws a clear exception with actionable instructions.
- [ ] If DB is present but no app config is stored, the first-run wizard is shown and all non-setup pages redirect to `/setup`.
- [ ] The wizard collects LLM provider, vector store, and auth settings across four steps.
- [ ] A "Test connection" button validates LLM and vector store settings before saving.
- [ ] The auth step shows an explicit acknowledged warning when auth is disabled.
- [ ] Settings saved via the wizard are stored encrypted in the database and survive a container restart.
- [ ] After completing the wizard, the application behaves identically to one configured via environment variables.
- [ ] `dotnet test` passes with zero failures.

## Notes on Superseded Issue 014

Issue 014 (`014-runtime-llm-configuration-wizard.md`) has been superseded by this issue. Key differences:

- **014** explored an Aspire-level wizard (CLI, `IInteractionService`, parameters). All of these approaches are now known to be unworkable or insufficient for the Docker deployment scenario.
- **This issue** moves configuration entirely into the running application, making it deployment-agnostic.
- Issue 014 should remain in `TODO/` with a note that it is superseded, for historical context.
