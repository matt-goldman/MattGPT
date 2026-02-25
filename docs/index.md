# MattGPT — Project Tracking Index

This document is the **system of record** for project planning and issue tracking. All agents and contributors must use this file to determine what to work on next and to record progress.

---

## Agent Workflow Instructions

### Picking Up Work

1. **Read this index.** The backlog below is the authoritative ordering. Issues are sequenced by the numeral in the "Seq" column — **not** by filename or any metadata inside the issue file.
2. **Select the next `TODO` issue** (lowest sequence number with status `TODO`).
3. Read the issue file in `docs/TODO/` for full requirements and acceptance criteria.
4. Move the issue status in the table below to `In Progress`.
5. Do the work. Commit early and often. Validate changes by running the Aspire AppHost and using Aspire MCP tooling as described in [`src/AGENTS.md`](../src/AGENTS.md).
6. When the issue is **complete and verified**:
   - Move the issue file from `docs/TODO/` to `docs/Done/`.
   - Update the table below: change the status to `Done` and update the "Location" column to `Done/`.
7. If the work involves a **significant architectural decision**, create an ADR in `docs/Decisions/` following the template in that folder (see [ADR instructions](#architecture-decision-records) below).

### Rules

- Only **one issue may be `In Progress` per agent** at a time.
- Do not skip sequence numbers. Dependencies are implicit in the ordering.
- If an issue is blocked, note the blocker in the issue file and move on to the next unblocked issue, leaving a comment in this index under the "Notes" column.
- Keep issue files self-contained: each must include enough context for any agent to pick it up cold.
- **All tests must pass** before requesting review on a PR. Run `dotnet test MattGPT.slnx` and confirm there are no failures.

---

## Backlog

| Seq | Issue File | Title | Status | Location | Notes |
|-----|-----------|-------|--------|----------|-------|
| 1 | 001-add-mongodb-to-apphost.md | Add MongoDB integration to AppHost | Done | Done/ | |
| 2 | 002-add-qdrant-to-apphost.md | Add Qdrant vector DB integration to AppHost | Done | Done/ | |
| 3 | 003-configure-llm-endpoint.md | Config-driven LLM endpoint selection | Done | Done/ | Depends on 1 |
| 4 | 004-chatgpt-json-parser.md | Build ChatGPT JSON parser & conversation lineariser | Done | Done/ | |
| 5 | 005-file-upload-ui.md | Create file upload UI page with progress tracking | Done | Done/ | |
| 6 | 006-background-processing-service.md | Implement background processing service for import | Done | Done/ | Depends on 4, 5 |
| 7 | 007-store-conversations-in-mongodb.md | Store parsed conversations in MongoDB | Done | Done/ | Depends on 1, 4, 6 |
| 8 | 008-generate-conversation-summaries.md | Generate conversation summaries using LLM | TODO | TODO/ | Depends on 3, 7 |
| 9 | 009-generate-embeddings.md | Generate embeddings from summaries | TODO | TODO/ | Depends on 8 |
| 10 | 010-store-embeddings-in-qdrant.md | Store embeddings in Qdrant with metadata | TODO | TODO/ | Depends on 2, 9 |
| 11 | 011-rag-retrieval-pipeline.md | Build RAG retrieval pipeline | TODO | TODO/ | Depends on 10 |
| 12 | 012-chat-ui-with-rag.md | Create chat UI page for LLM interaction with RAG memory | TODO | TODO/ | Depends on 11 |
| 13 | 013-end-to-end-testing-and-docs.md | End-to-end testing and documentation | TODO | TODO/ | Depends on all |

---

## Architecture Decision Records

Significant architectural decisions must be recorded as ADRs in `docs/Decisions/`.

### When to create an ADR

- Adding or replacing an infrastructure component (database, message broker, LLM provider, etc.)
- Changing the data model or storage strategy in a non-trivial way
- Choosing between materially different implementation approaches
- Any decision that a future contributor would need context on to understand "why"

### How to create an ADR

1. Copy `docs/Decisions/000-template.md` to a new file with the next available sequence number and a short slug, e.g. `001-use-qdrant-for-vectors.md`.
2. Fill in all sections of the template.
3. Reference the ADR from the relevant issue file(s) and note it in this index if appropriate.

---

## Project Overview

**MattGPT** imports an entire ChatGPT conversation history (~148 MB, ~2,213 conversations) and makes it available as RAG memory for any LLM. See [README.md](../README.md) and [conversation-restore-outline.md](../conversation-restore-outline.md) for full context.

### Key Components (target architecture)

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Orchestrator | .NET Aspire | Local dev orchestration, service discovery, observability |
| Web Frontend | Blazor Server | Upload UI, chat UI, progress monitoring |
| API Service | ASP.NET Core Minimal API | Business logic, parsing, RAG pipeline |
| Document DB | MongoDB | Full conversation storage, metadata |
| Vector DB | Qdrant | Embedding storage and similarity search |
| LLM | Foundry Local / Ollama / Azure OpenAI (config-driven) | Summary generation, chat, embeddings |
