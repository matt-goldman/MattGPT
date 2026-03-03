# 040 — Optional Authentication with User-Scoped Data

**Status:** TODO
**Depends on:** 39 (Add Postgres as Document DB and Vector Store Provider)

---

## Summary

Add optional authentication to MattGPT using ASP.NET Core Identity. When enabled via configuration, all API endpoints and UI pages require login, and all data (imported conversations, chat sessions, vector embeddings) is scoped to the authenticated user. When disabled, the app works as today but only surfaces untagged (no userId) data.

## Problem

MattGPT currently has no concept of users. All imported conversations and chat sessions are globally visible. In a shared or cloud deployment this is unacceptable — one user's ChatGPT history would be visible to everyone. Even in a single-user deployment, adding auth provides a basic security layer.

## Requirements

### Configuration

1. Add `Auth:Enabled` configuration option (default: `false`).
2. When `Auth:Enabled = false`, the app behaves as today but queries filter to `UserId == null` (untagged data only). No login UI is shown.
3. When `Auth:Enabled = true`, all API endpoints require authentication and the Blazor UI redirects unauthenticated users to a login page.

### Identity Provider

4. Use ASP.NET Core Identity with cookie authentication.
5. Use the existing document DB for Identity storage:
   - If `DocumentDb:Provider = Postgres`, use Identity's EF Core stores against the existing Postgres database.
   - If `DocumentDb:Provider = MongoDB`, use a MongoDB Identity provider (e.g. `AspNetCore.Identity.MongoDbCore` or similar).
6. Provide basic register/login/logout UI pages (minimal — not a full account management suite).

### Data Model Changes

7. Add `string? UserId` property to `StoredConversation`.
8. Add `string? UserId` property to `ChatSession`.
9. Add `user_id` to vector store point payloads (all `IVectorStore` implementations).

### Query Filtering

10. All `IConversationRepository` query methods must accept an optional `string? userId` parameter and filter results accordingly:
    - If `userId` is non-null, return only documents where `UserId == userId`.
    - If `userId` is null, return only documents where `UserId == null`.
11. Same filtering logic for `IChatSessionRepository`.
12. `IVectorStore.SearchAsync` must accept an optional `string? userId` parameter and apply the same filter.
13. `IVectorStore.UpsertAsync` must store the userId in the payload.

### Import Pipeline

14. Thread `userId` through the import pipeline: capture from `HttpContext` at upload time → store in `ImportJobRequest` → pass to `ConversationRepository.UpsertAsync` and `IVectorStore.UpsertAsync`.
15. The `ImportProcessingService` (background service) must use the userId from the job request, not from `HttpContext`.

### RAG & Chat Pipeline

16. `RagService` must pass the current userId to `IVectorStore.SearchAsync` so retrieval is user-scoped.
17. `SearchMemoriesTool` must also be user-scoped.
18. `ChatSessionService` must filter sessions by userId.

### UI

19. When auth is enabled, add a login/register page and a logout button in the header.
20. Protect all Blazor pages with `[Authorize]` (or equivalent).
21. When auth is disabled, hide all auth-related UI.

### Vector Store Implementations

22. Update all five `IVectorStore` implementations (Qdrant, Postgres, Azure AI Search, Pinecone, Weaviate) to store and filter on `user_id`.

## Design

### Auth Middleware (Program.cs)

When `Auth:Enabled = true`:
- Register ASP.NET Core Identity services and the appropriate store provider.
- Add authentication and authorization middleware.
- Apply a global authorization policy to all endpoints.

When `Auth:Enabled = false`:
- Skip Identity registration entirely.
- No auth middleware. Endpoints are anonymous.
- A `ICurrentUserService` returns `null` for userId, which the repositories use to filter to untagged data.

### CurrentUserService

A scoped `ICurrentUserService` interface with a single `string? UserId` property. Implementation reads from `HttpContext.User` when auth is enabled, returns `null` when disabled. This avoids scattering `HttpContext` access throughout the codebase.

### Vector Store Filtering

Each provider handles filtering differently:
- **Qdrant**: `must` filter condition on `user_id` payload field.
- **Postgres/pgvector**: `WHERE user_id = $1` (or `IS NULL`) clause.
- **Azure AI Search**: OData `$filter` on `user_id` field.
- **Pinecone**: metadata filter on `user_id`.
- **Weaviate**: `where` filter on `user_id` property.

### Migration of Existing Data

Existing data (imported without auth) will have `UserId = null`. This is by design:
- When auth is enabled, this data is invisible to all logged-in users.
- When auth is disabled, this data is the only data visible.
- No migration is required. Users who enable auth will re-import their conversations while logged in.

## Acceptance Criteria

- [ ] `Auth:Enabled` configuration option controls whether authentication is active.
- [ ] When `Auth:Enabled = false`, the app works without any login requirement and only surfaces untagged data.
- [ ] When `Auth:Enabled = true`, all API endpoints and UI pages require authentication.
- [ ] ASP.NET Core Identity is configured with the existing document DB (Postgres or MongoDB).
- [ ] `StoredConversation` and `ChatSession` have a `UserId` property.
- [ ] All `IConversationRepository` query methods filter by userId.
- [ ] All `IChatSessionRepository` query methods filter by userId.
- [ ] All `IVectorStore` implementations store and filter on userId.
- [ ] Import pipeline threads userId from upload through to storage and embedding.
- [ ] `RagService` and `SearchMemoriesTool` pass userId for user-scoped retrieval.
- [ ] Basic register/login/logout UI is functional.
- [ ] All existing tests pass (with auth disabled by default).
- [ ] New tests cover the user-scoping filter logic.

## Related

- ADR-008: Optional authentication strategy
