# 039 — Add Postgres as Document DB and Vector Store Provider

**Status:** Done
**Depends on:** 38 (Add Cloud LLM and Vector Store Provider Integrations)

---

## Summary

Add PostgreSQL as a provider for both the document database and the vector store, using the [pgvector](https://github.com/pgvector/pgvector) extension for similarity search. When both roles are assigned to Postgres, a single Aspire-managed container serves both, avoiding duplicate infrastructure.

## Problem

MattGPT currently requires two infrastructure services — MongoDB (document DB) and Qdrant (vector store). Many teams and cloud environments prefer a single Postgres instance for simplicity, cost, and operational consistency. Postgres with pgvector can fulfil both roles.

Document DB was also previously hardcoded to MongoDB; this issue introduces the `DocumentDb:Provider` configuration option to make it swappable.

## Requirements

1. Add `DocumentDb:Provider` configuration option (default: `MongoDB`).
2. Add Postgres implementations for all repository interfaces (`IConversationRepository`, `IChatSessionRepository`, `IUserProfileRepository`, `ISystemConfigRepository`, `IProjectNameRepository`).
3. Add `PostgresVectorStore` implementing `IVectorStore` using pgvector's cosine distance operator.
4. When `DocumentDb:Provider = Postgres` and `VectorStore:Provider = Postgres`, register the shared Npgsql data source once and configure both in a single conditional block (`vectorStoreConfigured` flag prevents duplicate registration).
5. Update AppHost to conditionally provision a Postgres resource (instead of MongoDB) when Postgres is selected for either role.
6. Update documentation (`configuration.md`, `integrations.md`).
7. All existing tests must continue to pass.

## Design

### Document DB (Postgres)

Conversations are stored as JSONB documents in a `conversations` table, with key scalar fields promoted to indexed columns (`processing_status`, `update_time`, `create_time`, `gizmo_type`, `conversation_template_id`) for efficient querying.

Other repositories (user profile, system config) use a shared `key_value_docs` table with `id TEXT PK` and `data JSONB` columns.

Chat sessions are stored in a `chat_sessions` table with JSONB `data` column and indexed `session_id`, `updated_at`, and `status` columns.

Project names are stored in a simple `project_names` table.

### Vector Store (Postgres/pgvector)

The `PostgresVectorStore` creates a `conversation_vectors` table with a `vector(N)` column and an HNSW cosine index. Vectors are passed as text in pgvector notation (e.g. `[1.0,2.0,3.0]`) and cast to `vector` in SQL — no additional .NET type-mapping library required.

Schemas are created lazily on first use (idempotent `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS`).

### Shared Resource (AppHost)

If `DocumentDb:Provider = Postgres` OR `VectorStore:Provider = Postgres`, AppHost provisions a single `postgres` resource (with `mattgptdb` database). MongoDB is only provisioned if `DocumentDb:Provider` is not `Postgres`.

### `vectorStoreConfigured` flag (Program.cs)

When the document DB block detects that `VectorStore:Provider` is also Postgres, it registers `PostgresVectorStore` and sets `vectorStoreConfigured = true`. The later vector store switch is guarded by `if (!vectorStoreConfigured)` and skips when already done.

## Acceptance Criteria

- [x] `DocumentDbOptions` class with `Provider` option (default: `MongoDB`) is added.
- [x] All five repository interfaces have Postgres implementations.
- [x] `PostgresVectorStore` implements `IVectorStore` using pgvector cosine distance.
- [x] When both are Postgres, a single `AddNpgsqlDataSource` call registers the data source and `vectorStoreConfigured` prevents double-registration.
- [x] AppHost provisions a Postgres container (not MongoDB) when document DB is Postgres.
- [x] AppHost provisions Postgres for vector store when only the vector store is Postgres (MongoDB still provisioned for document DB).
- [x] When both use Postgres, only one Postgres container is provisioned.
- [x] All 175 existing tests pass.
- [x] `configuration.md` and `integrations.md` updated with Postgres documentation.
