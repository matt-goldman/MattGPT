# 003 — Config-Driven LLM Endpoint Selection

**Status:** TODO
**Sequence:** 3
**Dependencies:** 001 (MongoDB must be available for later integration, but LLM config itself is independent)

## Summary

Implement configuration-driven selection of the LLM endpoint. The system must support switching between Foundry Local, Ollama, and Azure OpenAI via configuration, without code changes.

## Requirements

1. Define a configuration schema (in `appsettings.json` or Aspire parameters) to select the LLM provider and model.
2. Create an abstraction (interface/service) in the API service that wraps LLM interactions (chat completion, embeddings) so the rest of the codebase is provider-agnostic.
3. Implement provider-specific clients for:
   - **Foundry Local** (local model serving)
   - **Ollama** (local model serving)
   - **Azure OpenAI** (cloud)
4. Register the appropriate client at startup based on configuration.
5. Add a health/status endpoint that reports which LLM provider is active and whether it is reachable.
6. If running locally with Ollama, consider adding Ollama as an Aspire container resource.

## Acceptance Criteria

- [ ] Configuration in `appsettings.json` (or Aspire parameters) allows selecting between Foundry Local, Ollama, and Azure OpenAI.
- [ ] The API service resolves the correct LLM client based on config.
- [ ] A simple test endpoint (e.g. `GET /llm/status`) confirms the active provider and reachability.
- [ ] Switching providers requires only a config change and app restart, no code changes.

## Notes

- Microsoft.Extensions.AI provides a unified abstraction that may simplify multi-provider support — evaluate this.
- Consider creating an ADR to document the chosen abstraction approach.
- For local-only development, Ollama is the easiest starting point.
