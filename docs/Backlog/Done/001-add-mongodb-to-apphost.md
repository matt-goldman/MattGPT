# 001 — Add MongoDB Integration to AppHost

**Status:** Done
**Sequence:** 1
**Dependencies:** None

## Summary

Add MongoDB as an Aspire resource in the AppHost so it is available as the document database for storing full conversation data and metadata.

## Requirements

1. Add the MongoDB Aspire hosting integration NuGet package to `MattGPT.AppHost`.
2. Add the MongoDB Aspire client integration NuGet package to `MattGPT.ApiService`.
3. Register a MongoDB resource in `AppHost.cs` and wire it as a reference to the API service.
4. Configure the API service to consume the MongoDB connection via Aspire service defaults.
5. Verify that MongoDB starts and is healthy when running `aspire run`.

## Acceptance Criteria

- [ ] `aspire run` starts MongoDB as a container resource and it shows as healthy in the Aspire dashboard.
- [ ] The API service can resolve the MongoDB connection string via Aspire's dependency injection.
- [ ] No persistent volume is configured (ephemeral for now — see AGENTS.md guidance on persistent containers).

## Notes

- Use the Aspire MCP `list integrations` tool to find the correct package version aligned with `Aspire.AppHost.Sdk/13.1.0`.
- Use `get integration docs` to follow the latest integration guidance.
- Consider creating an ADR if the MongoDB configuration deviates from defaults.
