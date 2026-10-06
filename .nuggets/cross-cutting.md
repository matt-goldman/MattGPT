# Cross-cutting

## 2026-10-06 — Repo SQL/BSON can be checked against real backends with podman, no Aspire needed

**Context:** Verifying new Postgres/Mongo repository methods for 049/050. Docker isn't running on the dev box, but `podman` is.
**Observation:** `podman run -d --rm -p 55432:5432 -e POSTGRES_PASSWORD=pw docker.io/library/postgres:17` (or `mongo:8` on 57017), plus a scratch console app with a `ProjectReference` to the module, constructs the repositories directly (`new PostgresConversationRepository(NpgsqlDataSource.Create(...), NullLogger...)`). Seed legacy-shaped rows with raw SQL/BSON first to test the migrations. For Mongo, register the same `BsonClassMap`s as `MongoDBModule.Module`. Seeding gotcha: AutoMap serialises a nested `Id` property (e.g. `StoredMessage.Id`) as `_id`, so hand-written BSON must use `_id`.
**Tags:** testing, postgres, mongo, tooling
