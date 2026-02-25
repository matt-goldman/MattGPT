# 002 — Add Qdrant Vector DB Integration to AppHost

**Status:** TODO
**Sequence:** 2
**Dependencies:** None

## Summary

Add Qdrant as an Aspire resource in the AppHost for vector storage and similarity search of conversation embeddings.

## Requirements

1. Add the Qdrant Aspire hosting integration NuGet package to `MattGPT.AppHost`.
2. Add the Qdrant Aspire client integration NuGet package to `MattGPT.ApiService`.
3. Register a Qdrant resource in `AppHost.cs` and wire it as a reference to the API service.
4. Configure the API service to consume the Qdrant connection via Aspire's dependency injection.
5. Verify that Qdrant starts and is healthy when running `aspire run`.

## Acceptance Criteria

- [ ] `aspire run` starts Qdrant as a container resource and it shows as healthy in the Aspire dashboard.
- [ ] The API service can resolve the Qdrant endpoint/connection via DI.
- [ ] No persistent volume is configured (ephemeral for now).

## Notes

- Use the Aspire MCP `list integrations` tool to find the correct package version.
- Use `get integration docs` to follow the latest integration guidance.
- If Qdrant is not available as a first-party Aspire integration, consider using a container resource directly and document the decision in an ADR.
