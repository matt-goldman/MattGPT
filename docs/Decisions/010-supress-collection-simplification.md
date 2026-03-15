# ADR-010: Suppress IDE0028 — Simplify Collection Initialization

**Date:** 2026-03-14
**Status:** Accepted

### Context

C# 12 introduced *collection expressions* (e.g. `[a, b, ..c]`), and the Roslyn analysers now emit **IDE0028** ("Simplify collection initialization") suggesting that traditional collection initialisers be rewritten to use the new syntax, including the spread operator (`..`).

For example, the analyser wants to turn:

```csharp
var messages = new List<ChatMessage>(chatHistory) { systemMessage };
```

into:

```csharp
List<ChatMessage> messages = [systemMessage, ..chatHistory];
```

The spread syntax obscures intent. In the original form it is immediately obvious that a new list is being created from an existing collection and an additional item is appended. The collection-expression form requires the reader to mentally parse the spread operator and reason about ordering. This is worse in every dimension that matters in a codebase optimised for readability: scannability, familiarity, and explicitness.

**Clarity > brevity.** Terse code serves no one.

### Decision

Suppress **IDE0028**  globally via a single `.editorconfig` in the repository root:

```ini
# Collection-expression "simplification" harms readability — see ADR-010
dotnet_diagnostic.IDE0028.severity = none
```

This prevents the warning from appearing in the IDE or CI builds across every project in the solution, with zero per-project maintenance.

### Consequences

**Easier:**
- Developers can use explicit, well-understood collection initialisers and constructors without being nagged by the analyser.
- Code reviews don't waste cycles debating an auto-suggested stylistic change that reduces clarity.
- One file to maintain — no per-project `GlobalSuppressions.cs` entries needed as new code is added.

**More difficult:**
- Developers who *prefer* collection expressions can still use them, but the analyser won't nudge towards them — consistency relies on convention rather than tooling enforcement.
- If a future C# version makes the spread syntax more idiomatic or if team preference shifts, this rule will need to be revisited.

### Alternatives Considered

| Alternative | Reason rejected |
|-------------|----------------|
| Accept the suggestion and adopt collection expressions everywhere | Reduces readability — the spread operator is unfamiliar to many C# developers and adds cognitive load for no functional benefit |
| Suppress inline with `#pragma warning disable` | Scatters suppression noise throughout business logic |
| Per-project `GlobalSuppressions.cs` with `[assembly: SuppressMessage]` | Works, but requires a new entry for every occurrence in every project — high maintenance for a blanket stylistic preference |
| Lower severity to `suggestion` instead of suppressing | Still produces IDE clutter and risks accidental acceptance via quick-fix |


**Addendum:** The following analyser codes have been identified as raising the same warning and added to the `.editorconfig` file.

| Code | Added on |
|------|----------|
| IDE0300 | 2026-03-14 |

**Note:** While ADRs are generally immutable, an exception is explicitly granted in this case to track these codes as they are revealed over time.