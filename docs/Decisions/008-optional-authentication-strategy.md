# ADR-008: Optional Authentication Strategy

**Date:** 2026-03-03
**Status:** Accepted
**Related Issues:** [040-optional-authentication.md](../Backlog/TODO/040-optional-authentication.md)

### Context

MattGPT currently has no authentication or user isolation. All imported conversations, chat sessions, and vector embeddings are globally accessible. This is acceptable for a single-user local deployment but unacceptable for shared or cloud scenarios where multiple users would see each other's ChatGPT history.

We need auth to be **optional** — the primary use case remains a single-user local Aspire deployment where requiring login would be friction for no benefit. But when deployed to the cloud or shared infrastructure, auth must gate every endpoint and scope all data to the authenticated user.

### Decision

1. **ASP.NET Core Identity** with cookie authentication, controlled by an `Auth:Enabled` config flag (default: `false`).

2. **Identity storage reuses the existing document DB** — no new infrastructure:
   - When `DocumentDb:Provider = Postgres`: use Identity's built-in EF Core stores against the existing Postgres database.
   - When `DocumentDb:Provider = MongoDB`: use a community MongoDB Identity provider.

3. **User-scoped data model**: add `string? UserId` to `StoredConversation`, `ChatSession`, and vector store payloads. All query interfaces filter on userId.

4. **Filtering semantics are symmetric**:
   - Auth enabled + logged in → return only data where `UserId == currentUser`.
   - Auth disabled (or no user context) → return only data where `UserId == null` (untagged).
   
   This means existing untagged data is only visible when auth is off. Enabling auth doesn't leak existing data — users re-import while logged in.

5. **`ICurrentUserService`** abstraction: a scoped service that resolves the current userId from `HttpContext.User` (or returns `null` when auth is disabled). All repositories and services depend on this rather than accessing `HttpContext` directly.

6. **Import pipeline threading**: userId is captured at HTTP upload time and stored on `ImportJobRequest`. The background `ImportProcessingService` reads it from the job, not from `HttpContext`, since it runs outside the request scope.

### Consequences

**Easier:**
- Multi-user deployment becomes possible without any data-model migration.
- Single-user deployments are unaffected (flag is off by default).
- The `ICurrentUserService` abstraction keeps auth concerns out of business logic.
- Untagged data (imported before auth was enabled) is safely invisible to logged-in users.

**Harder:**
- Every `IVectorStore` implementation (5 currently) needs a `user_id` filter field — this is the largest mechanical change.
- All repository query methods gain a `userId` parameter, increasing surface area.
- Identity storage requires provider-specific setup (EF Core for Postgres, community package for MongoDB).
- Users who enable auth after importing must re-import their conversations. There is no migration path for retroactively tagging existing data (by design — we can't know which user it belongs to).

### Alternatives Considered

1. **External identity provider only (e.g. Azure AD / Entra ID, Auth0)**
   - Pro: No local user management, SSO out of the box.
   - Con: Requires external service configuration, overkill for the local/small-team use case, not self-contained.
   - Verdict: Could be added later as an additional provider. ASP.NET Core Identity serves as the baseline.

2. **API key authentication (no user identity)**
   - Pro: Simpler — just a shared secret in config.
   - Con: No per-user data isolation. Doesn't solve the multi-user problem.
   - Verdict: Rejected. The whole point is user-scoped data.

3. **Separate Identity database**
   - Pro: Clean separation of concerns.
   - Con: Adds another infrastructure resource to manage. Against the Aspire ethos of minimal moving parts.
   - Verdict: Rejected in favour of reusing the existing document DB.

4. **Auth disabled = see everything (including tagged data)**
   - Pro: Simpler query logic.
   - Con: Security risk — disabling auth would expose data imported by authenticated users.
   - Verdict: Rejected. The safe default is that untagged data is the only data visible without auth.
