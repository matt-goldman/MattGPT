# AGENTS.md

Instructions for AI coding agents working on this repository.

## Project Overview

**MattGPT** is a .NET Aspire application that imports ChatGPT conversation history and makes it available as RAG memory for any LLM. See [README.md](README.md) for goals and [conversation-restore-outline.md](conversation-restore-outline.md) for the technical approach.

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker (for Aspire container resources)
cd src/MattGPT.AppHost
dotnet run
```

The Aspire dashboard will be available at the URL printed on startup. All services, databases, and container resources are orchestrated automatically.

## Backlog & Issue Tracking

**The system of record is [`docs/index.md`](docs/index.md).** Read it before starting any work.

### Workflow Summary

1. Open [`docs/index.md`](docs/index.md) and find the next issue with status `TODO` (lowest sequence number).
2. Read the full issue file in `docs/TODO/`.
3. Update the index table: set the issue status to `In Progress`.
4. Implement the issue. Commit early and often.
5. When complete:
   - Move the issue file from `docs/TODO/` to `docs/Done/`.
   - Update the index table: set the status to `Done` and the location to `Done/`.
6. If you make a significant architectural decision, create an ADR in `docs/Decisions/` using the template there (`000-template.md`).

### Key Rules

- **Sequencing is by the index table**, not by filenames or metadata in issue files.
- **One `In Progress` issue per agent** at a time.
- **Do not skip sequence numbers** — dependencies are implicit in the ordering. If blocked, note the blocker and move to the next unblocked issue.
- **Issue files must be self-contained** — any agent should be able to pick one up cold.

## Additional Agent Instructions

Aspire-specific guidance (running, debugging, MCP tools, integrations) is in [`src/AGENTS.md`](src/AGENTS.md). Read it when working on any code under `src/`.

## Repository Structure

```
MattGPT/
├── AGENTS.md                          ← You are here (global agent instructions)
├── README.md                          ← Project goals and vision
├── conversation-restore-outline.md    ← Technical approach for RAG pipeline
├── conversations.schema.json          ← Full JSON schema for ChatGPT export
├── MattGPT.slnx                       ← Solution file
├── docs/
│   ├── index.md                       ← Backlog & project tracking (system of record)
│   ├── TODO/                          ← Issue files awaiting implementation
│   ├── Done/                          ← Completed issue files
│   └── Decisions/                     ← Architecture Decision Records (ADRs)
└── src/
    ├── AGENTS.md                      ← Aspire-specific agent instructions
    ├── MattGPT.AppHost/               ← Aspire orchestration (entry point)
    ├── MattGPT.ApiService/            ← API service (business logic)
    ├── MattGPT.Web/                   ← Blazor web frontend
    └── MattGPT.ServiceDefaults/       ← Shared Aspire service configuration
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

### Commits

- Commit early and often with clear, descriptive messages.
- One logical change per commit where practical.
