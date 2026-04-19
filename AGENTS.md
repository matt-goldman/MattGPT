# AGENTS.md

Instructions for AI coding agents working on this repository.

## Project Overview

**MattGPT** is a .NET Aspire application that imports ChatGPT conversation history and makes it available as RAG memory for any LLM. See [README.md](README.md) for goals and [conversation-restore-outline.md](docs/TechnicalReference/conversation-restore-outline.md) for the technical approach.

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker (for Aspire container resources)
cd src/MattGPT.AppHost
dotnet run
```

The Aspire dashboard will be available at the URL printed on startup. All services, databases, and container resources are orchestrated automatically.

## Backlog & Issue Tracking

**The system of record is [`docs/Backlog/index.md`](docs/Backlog/index.md).** Read it before starting any work.

### Workflow Summary

1. Open [`docs/Backlog/index.md`](docs/Backlog/index.md) and find the next issue with status `TODO` (lowest sequence number).
2. Read the full issue file in `docs/Backlog/TODO/`.
3. Update the index table: set the issue status to `In Progress`.
4. Implement the issue. Commit early and often.
5. When complete:
   - Move the issue file from `docs/Backlog/TODO/` to `docs/Backlog/Done/`.
   - **Update the issue file itself:** set `**Status:**` to `Done` and tick all acceptance criteria checkboxes (`- [ ]` → `- [x]`).
   - Update the index table: set the status to `Done` and the location to `Done/`.
6. If you make a significant architectural decision, create an ADR in `docs/Decisions/` using the template there (`000-template.md`).

### Key Rules

- **Sequencing is by the index table**, not by filenames or metadata in issue files.
- **One `In Progress` issue per agent** at a time.
- **Do not skip sequence numbers** — dependencies are implicit in the ordering. If blocked, note the blocker and move to the next unblocked issue.
- **Issue files must be self-contained** — any agent should be able to pick one up cold.
- **Update the issue file when done** — set `**Status:** Done` and tick all acceptance criteria checkboxes before closing the issue.
- **All tests must pass** before requesting review on a PR. Run `dotnet test MattGPT.slnx` and confirm there are no failures. This is enforced by the CI workflow on every pull request.

## Additional Agent Instructions

Aspire-specific guidance (running, debugging, MCP tools, integrations) is in [`src/AGENTS.md`](src/AGENTS.md). Read it when working on any code under `src/`.

## Repository Structure

```
MattGPT/
├── AGENTS.md                               ← You are here (global agent instructions)
├── README.md                               ← Project goals and vision
├── MattGPT.slnx                            ← Solution file
├── docs/
│   ├── index.md                            ← Documentation home (navigation hub)
│   ├── Backlog/                            ← Project tracking (system of record)
│   │   ├── index.md                        ← Backlog & issue tracking index
│   │   ├── TODO/                           ← Issue files awaiting implementation
│   │   └── Done/                           ← Completed issue files
│   ├── Decisions/                          ← Architecture Decision Records (ADRs)
│   ├── UserGuides/                         ← End-user documentation
│   │   ├── getting-started.md
│   │   ├── usage.md
│   │   ├── configuration.md
│   │   ├── integrations.md
│   │   └── troubleshooting.md
│   └── TechnicalReference/                 ← Technical docs (schema, pipeline, analysis)
│       ├── conversation-restore-outline.md ← Technical approach for RAG pipeline
│       └── conversations.schema.json       ← Full JSON schema for ChatGPT export
└── src/
    ├── AGENTS.md                           ← Aspire-specific agent instructions
    ├── MattGPT.AppHost/                    ← Aspire orchestration (entry point)
    ├── MattGPT.ApiService/                 ← API service (business logic)
    ├── MattGPT.Web/                        ← Blazor web frontend
    └── MattGPT.ServiceDefaults/            ← Shared Aspire service configuration
```

## Tech Stack

- **.NET 10** / C#
- **Aspire 13.1** — local orchestration, service discovery, health checks, observability
- **Blazor Server** — interactive web UI
- **ASP.NET Core Minimal APIs** — API service
- **MongoDB** — document storage for full conversations and metadata
- **Qdrant** — vector database for embeddings and similarity search
- **LLM** — configurable: Foundry Local, Ollama, or Azure OpenAI

## Development Guidelines

### Aspire

- The AppHost project (`src/MattGPT.AppHost/`) orchestrates everything. Add new resources (databases, containers, projects) there.
- Use Aspire's service discovery for inter-service communication (e.g. `https+http://apiservice`).
- Use the Aspire MCP tools (`list integrations`, `get integration docs`) to find correct NuGet packages and follow current integration guidance.

### Code Style

- Follow existing patterns in the codebase.
- Use `System.Text.Json` for JSON handling (not Newtonsoft).
- Prefer streaming APIs for large data (the conversation export is ~148 MB).
- Use dependency injection consistently — register services in `Program.cs`.

### Testing

- Validate changes by running the Aspire AppHost and checking the dashboard.
- Unit tests should be added for pure logic (parsers, transformers) but are not required for integration/UI code in the MVP phase.
- **All tests must pass** before requesting review. Run `dotnet test MattGPT.slnx` to verify.

### Commits

- Commit early and often with clear, descriptive messages.
- One logical change per commit where practical.

## Nuggets

This repo uses a lightweight convention called "nuggets" to capture incidental
observations during agent runs — things that would otherwise be discarded when
a run ends or a conversation compacts.

**Before starting a non-trivial task:**
Read `.nuggets/README.md` for an overview, and scan the relevant thematic files
under `.nuggets/themes/` for observations related to the task. When about to
explore a specific file or area, search `.nuggets/` for mentions of it.

**During a run, write a nugget when you observe something that:**
- is non-obvious (not immediately visible from the code itself),
- would be useful to a future agent working in this area,
- but does not warrant an ADR, skill, or AGENTS.md update.

Particularly worth capturing: approaches you tried and ruled out, gotchas,
non-local side effects, and anything you'd be annoyed to re-derive. If you
notice something mid-run that you'd regret losing to compaction, write a nugget.

**Especially engage with nuggets when there's a signal this task has been
touched before:**
- The user references prior work ("last time," "we already," "is X still,"
  "has Y been reverted") — explicitly or implicitly.
- The task is investigative or diagnostic (checking whether something holds,
  why something is happening).
- The task touches files or subsystems with existing nugget entries.

In these cases:

- **Check the relevant nuggets first.** If prior runs have left observations
  about this area, they are most likely to be useful exactly here.
- **If no relevant nuggets exist, that is itself a signal.** This area is being
  revisited but has no trail. Capture relevant nuggets during or after this
  run so the next revisit is not starting from zero. Early in a repo's
  adoption of nuggets, or in a genuinely new area, absence of entries is
  expected — it means there's groundwork to lay, not that the system has
  failed.

**Before writing a nugget, ask:**
- Is this decision-grade? → ADR
- Is this a reusable procedure? → Skill
- Is this a standing instruction? → AGENTS.md
- Otherwise → Nugget

**When you encounter a stale nugget** (code has changed, pointer is wrong,
observation no longer holds), update or remove it.

**Format and conventions:** see `.nuggets/README.md`.
