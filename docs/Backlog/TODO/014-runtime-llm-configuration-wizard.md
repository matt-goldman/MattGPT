# 014 — Runtime LLM Configuration Wizard

**Status:** Superseded by [043 — Application-Level First-Run Configuration Wizard](043-application-level-config-wizard.md)
**Sequence:** 14
**Dependencies:** 3 (config-driven LLM endpoint selection)

> **Note:** This issue has been superseded by issue 043. The Aspire-level approaches explored here (CLI wizard, `IInteractionService`, Aspire parameters) are either blocked by the Aspire dashboard deadlock problem or insufficient for the Docker deployment scenario. The new approach moves configuration entirely into the running application. This file is retained for historical context.

## Summary

Allow users to configure the LLM provider, model, and connection details at runtime without editing configuration files. The first step is deciding the implementation approach, since Aspire's built-in interaction service (`IInteractionService` / `BeforeStartEvent`) is not viable — it blocks startup before the dashboard URL is available, creating a deadlock.

## Background

The current setup requires the LLM provider and model to be configured in `appsettings.json` (or via environment variables / user secrets) before starting the application. This works but is not user-friendly for first-time users or for quickly switching between providers during development.

An attempt was made to use Aspire's `IInteractionService` with `PromptInputsAsync` in a `BeforeStartEvent` handler. This approach fails because:

- The dashboard URL (including access token) is only printed to the console **after** startup completes.
- `BeforeStartEvent` blocks startup waiting for the user to respond in the dashboard.
- The user can't reach the dashboard because the URL hasn't been emitted yet — a deadlock.

Additionally, Aspire cannot dynamically register resources after `Build()` is called. The app model (containers, project references, dependencies) is fixed at build time. This means the Ollama container resource decision must be made before `Build()`, regardless of how configuration is gathered.

## Possible Approaches

The first task is to evaluate and select an approach. Options identified so far:

1. **Startup CLI wizard** — a small console app or `dotnet tool` that prompts the user interactively in the terminal, writes the configuration (to `appsettings.json`, user secrets, or a shared config file), and then launches `aspire run`. Simple and self-contained.

2. **In-app settings page** — add an LLM configuration page to the Blazor web frontend. The page writes settings to a shared store (e.g. MongoDB or a local config file). The API service reads from this store at startup or watches for changes. May require a service restart to pick up new provider/resource changes.

3. **Aspire parameters** — use `builder.AddParameter()` for LLM settings. These prompt in the Aspire dashboard during Azure deployment scenarios but may not solve the local-dev UX problem.

4. **Hybrid** — use config files as the primary mechanism but provide a simple setup script (`setup.ps1` / `setup.sh`) that walks the user through configuration interactively before first run.

## Requirements

1. Evaluate the approaches above (and any others discovered) and select one. Document the decision in an ADR.
2. Implement the chosen approach so that a user with no prior configuration can start MattGPT and be guided through LLM setup.
3. Existing config-file-based setup must continue to work — the wizard is an alternative, not a replacement.
4. The solution should work on both Windows and macOS.

## Acceptance Criteria

- [ ] An ADR documents the chosen approach and the reasons for rejecting alternatives.
- [ ] A first-time user can start MattGPT without manually editing config files and be guided through LLM provider/model selection.
- [ ] Users who have already configured `appsettings.json` or environment variables experience no change in behaviour — the app starts immediately.
- [ ] The solution works on Windows and macOS.

## Notes

- Keep scope minimal — this is a developer tool, not a polished installer. A terminal-based wizard is perfectly acceptable.
- Consider whether the wizard should also handle pulling Ollama models (`ollama pull <model>`) as part of setup.
