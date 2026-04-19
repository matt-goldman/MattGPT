# Nuggets

This directory contains "nuggets" — small, scoped captures of incidental
observations made during agent coding runs. A nugget is something that was
learned, ruled out, or noticed during exploration that would otherwise be
lost when a run ends or a conversation compacts.

Nuggets are find-signals, not skip-signals. Treat them as hints to verify,
not facts to act on.

## Layout

- `README.md` — this file
- `themes/` — thematic append-only files (e.g. `pipelines.md`, `auth.md`).
  Most nuggets live here. Theme names are repo-local and emergent; create a
  new file when no existing theme fits.
- `cross-cutting.md` — observations that don't belong to any single theme.
- `files/` (optional) — file-scoped sidecars mirroring the repo structure
  (e.g. `files/src/Pipeline/FooProcessor.cs.md`). Use when a single file has
  accumulated enough related nuggets that a dedicated landing page is
  warranted. Sidecars primarily contain pointers into the thematic files
  rather than duplicate content. Do not create pre-emptively; let density
  drive the decision.

## When to write a nugget

Write a nugget when you observe something that is:

- non-obvious (not immediately visible from the code itself),
- useful to a future agent working in this area,
- but does not warrant an ADR, skill, or AGENTS.md update.

Particularly worth capturing: approaches tried and ruled out, gotchas,
non-local side effects, anything you'd be annoyed to re-derive, anything
you'd regret losing to compaction.

## Extended reasoning

Reasoning that spans multiple files, layers, or sources — and produces or
deduces something not visible from any single place — is particularly
worth capturing. The most common case is code paths: how a request flows,
how a pipeline behaves under specific conditions, why a particular outcome
occurs. Also worth capturing: reasoning that produced a finding tangential
to the current task — you traced to B while looking for A, and the trace
to B is correct and complete, just not what this run was for.

The test: will a future agent (or you, later) benefit from not having to
retrace this ground? If retracing would be expensive, capture the path.

Capture as navigation aids, not full traces. A terse sequence of steps
with pointers at each stage is enough. The nugget is the map, not the
terrain. Line-level pointers matter especially here — a code-path nugget
whose steps just name files sends the reader back into cold exploration;
one whose steps give line ranges lets the reader verify each hop in
seconds.

## Classification

Before writing, check that this is actually a nugget:

- Is it decision-grade? → ADR
- Is it a reusable procedure? → Skill
- Is it a standing instruction? → AGENTS.md
- Otherwise → Nugget

## Format

No strict schema. Keep each nugget under ~100 words (extended-reasoning
nuggets may run up to ~200 words). These elements tend to be useful and
should be included when they apply:

- Date
- Short title
- Context (what you were doing when you noticed this)
- The observation itself
- Pointer (file, symbol, or area), if applicable
- Tags (informal, repo-local; e.g. `gotcha`, `tried-didnt-work`,
  `ruled-out`, `code-path`, `tangential`)

### Pointers

Pointers should be as specific as the observation warrants:

- **Area-level** — `src/Pipeline/` — for nuggets about a subsystem or
  pattern across multiple files.
- **File-or-symbol-level** — `src/Pipeline/FooProcessor.cs` or
  `FooProcessor.Bar` — for nuggets that apply to a file or method as a
  whole.
- **Line-level** — `src/Pipeline/FooProcessor.cs:54-79` — for nuggets
  tied to a specific region, especially within a large file.

For large files, include line ranges. The token cost is trivial and the
work saved for future readers is significant.

### Examples

```
## 2026-04-18 — Log mutation via FooProcessor.Bar side effect

**Context:** Investigating why reassembled logs occasionally missed entries.
**Observation:** FooProcessor.Bar mutates the log stream as a side effect
when EnableCompaction is true. Not mentioned in the processor's docstring.
**Pointer:** src/Pipeline/FooProcessor.cs:54-79
**Tags:** gotcha, pipeline
```

Ruled-out approach:

```
## 2026-04-18 — Tried batching log reads via MemoryMappedFile

**Context:** Reducing IO overhead during log reassembly.
**Observation:** MemoryMappedFile approach failed because the device writes
logs with exclusive locks during rotation. Pursued streaming reads instead.
**Tags:** tried-didnt-work, pipeline
```

Code path:

```
## 2026-04-18 — Log reassembly path, fragmented-input case

**Context:** Tracing why fragmented logs occasionally reassemble with gaps.
**Path:**
1. Input arrives at `LogIngestor.Receive` (src/Ingestion/LogIngestor.cs:42-58)
   — validates headers only.
2. Fragments routed via `FragmentDispatcher.Route`
   (src/Pipeline/FragmentDispatcher.cs:89-117) based on device ID.
3. `ReassemblyBuffer.Accept` (src/Pipeline/ReassemblyBuffer.cs:140-192)
   holds fragments until sequence completes OR timeout (default 30s, set
   in `appsettings.json:Reassembly:Timeout`).
4. On completion, `FooProcessor.Bar` (src/Pipeline/FooProcessor.cs:54-79)
   emits — see separate nugget re EnableCompaction side effect at this stage.
5. Output written via `LogSink.Write` (src/Output/LogSink.cs:201-223).

**Gaps correlate with step 3 timeout, not fragment loss.**
**Tags:** code-path, pipeline
```

## Staleness

If you encounter a nugget whose pointer no longer matches the code, or
whose observation no longer holds, update it or remove it. Stale nuggets
are expected occasionally; a stale nugget is unhelpful but not dangerous,
because nuggets are hints to verify, not facts to act on.

## More

For the motivation, experimental framing, and evaluation criteria, see
the nuggets spec: https://github.com/matt-goldman/nuggets