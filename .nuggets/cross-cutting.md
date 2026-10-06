# Cross-cutting

## 2026-10-06 — Repo SQL/BSON can be checked against real backends with podman, no Aspire needed

**Context:** Verifying new Postgres/Mongo repository methods for 049/050. Docker isn't running on the dev box, but `podman` is.
**Observation:** `podman run -d --rm -p 55432:5432 -e POSTGRES_PASSWORD=pw docker.io/library/postgres:17` (or `mongo:8` on 57017), plus a scratch console app with a `ProjectReference` to the module, constructs the repositories directly (`new PostgresConversationRepository(NpgsqlDataSource.Create(...), NullLogger...)`). Seed legacy-shaped rows with raw SQL/BSON first to test the migrations. For Mongo, register the same `BsonClassMap`s as `MongoDBModule.Module`. Seeding gotcha: AutoMap serialises a nested `Id` property (e.g. `StoredMessage.Id`) as `_id`, so hand-written BSON must use `_id`.
**Tags:** testing, postgres, mongo, tooling

## 2026-10-07 — AppHost never reads the App Configuration emulator; config flows one way

**Context:** Diagnosing "AppHost extensions don't honour the App Configuration emulator" (`Orchestration/MattGPT.AppHost/AppHostConfiguration.cs`).
**Observation:** Flow is AppHost `builder.Configuration` → `Seed__Json` env (read as `Seed:Json`, so the prefix is fine) → ConfigSeeder → emulator → ApiService/Web. AppHost extensions decide resources at model-build time, before the emulator exists, so emulator values can't affect resource provisioning. This split is intentional: the emulator only controls runtime settings. Gotcha: seeding uses `If-None-Match: *` and the emulator has a data volume, so after changing a resource-selecting key (e.g. `DocumentDb:Provider`) in AppHost config, the emulator keeps the old value and the services can disagree with what the AppHost provisioned. Clear the volume or edit the key in the emulator. Also: `appsettings.json` has `""` placeholders, so AppHost reads must use `GetValueOrDefault`, not `?? default` (fixed in `AppHostApiService.cs`).
**Tags:** aspire, apphost, app-configuration, config
