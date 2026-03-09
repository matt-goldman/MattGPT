# MattGPT — Project Tracking Index

This document is the **system of record** for project planning and issue tracking. All agents and contributors must use this file to determine what to work on next and to record progress.

---

## Agent Workflow Instructions

### Picking Up Work

1. **Read this index.** The backlog below is the authoritative ordering. Issues are sequenced by the numeral in the "Seq" column — **not** by filename or any metadata inside the issue file.
2. **Select the next `TODO` issue** (lowest sequence number with status `TODO`).
3. Read the issue file in `TODO/` for full requirements and acceptance criteria.
4. Move the issue status in the table below to `In Progress`.
5. Do the work. Commit early and often. Validate changes by running the Aspire AppHost and using Aspire MCP tooling as described in [`src/AGENTS.md`](../../src/AGENTS.md).
6. When the issue is **complete and verified**:
   - Move the issue file from `TODO/` to `Done/`.
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
| 8 | 008-generate-conversation-summaries.md | Generate conversation summaries using LLM | Done | Done/ | Depends on 3, 7 |
| 9 | 009-generate-embeddings.md | Generate embeddings from summaries | Done | Done/ | Depends on 8 |
| 10 | 010-store-embeddings-in-qdrant.md | Store embeddings in Qdrant with metadata | Done | Done/ | Depends on 2, 9 |
| 11 | 011-rag-retrieval-pipeline.md | Build RAG retrieval pipeline | Done | Done/ | Depends on 10 |
| 12 | 012-chat-ui-with-rag.md | Create chat UI page for LLM interaction with RAG memory | Done | Done/ | Depends on 11; OpenWebUI evaluated and deferred — see ADR-002 |
| 13 | 013-end-to-end-testing-and-docs.md | End-to-end testing and documentation | Done | Done/ | Depends on all |
| 14 | 016-multi-file-import.md | Support importing multiple files | Done | Done/ | Depends on 5, 6 |
| 15 | 018-multi-turn-chat-with-rolling-summaries.md | Multi-turn chat with rolling summaries | Done | Done/ | Depends on 11, 12; see ADR-004. Includes MongoDB session persistence (scope pulled forward from 019). |
| 16 | 019-persist-and-embed-chat-conversations.md | Persist and embed chat conversations | Done | Done/ | Depends on 15, 7, 9, 10. Persistence, session lifecycle, auto-titles, and chat history sidebar all delivered (sidebar in 022). |
| 17 | 020-tool-calling-rag-retrieval.md | Tool-calling RAG retrieval | Done | Done/ | Depends on 15, 11; see ADR-005 |
| 18 | 015-clickable-citation-links.md | Clickable citation links in chat UI | Done | Done/ | Depends on 12 |
| 19 | 017-export-content-analysis.md | Fully analyse export content for missing import detail | Done | Done/ | Depends on 4, 7. Analysed 2,913 conversations / 79,910 messages; created issues 023–028. |
| 20 | 023-handle-non-text-content-types.md | Handle non-text content types in parser | Done | Done/ | Depends on 4, 7, 17. Highest-impact parser fix — 11K messages with lost content. |
| 21 | 027-filter-hidden-messages-from-embedding.md | Filter hidden/system messages from embedding | Done | Done/ | Depends on 19, 20. Improve RAG quality by excluding scaffolding. |
| 22 | 022-chat-history-sidebar.md | Chat history sidebar with imported conversations | Done | Done/ | Part of 019 scope. Depends on 18, 7, 12. Shows both native chat sessions and imported ChatGPT conversations in a collapsible sidebar. |
| 23 | 026-capture-conversation-metadata.md | Capture conversation-level metadata | Done | Done/ | Depends on 4, 7, 17. gizmo_id, memory opt-out, archived status. |
| 24 | 025-capture-citations-and-content-references.md | Capture citations and content references | Done | Done/ | Depends on 4, 7, 17. 8,651 citations and 13,106 content references lost. |
| 25 | 028-extract-user-profile.md | Extract user profile from custom instructions | Done | Done/ | Depends on 17, 20. Store custom instructions as reusable system context. |
| 26 | 021-abstract-vector-store-interface.md | Abstract vector store behind provider-agnostic interface | Done | Done/ | Depends on 2, 10. Low urgency — code quality / extensibility. Existing TODO in QdrantService.cs. |
| 27 | 024-capture-attachments-and-tool-metadata.md | Capture file attachments and author/tool metadata | Deferred | TODO/ | Depends on 4, 7, 17. Deferred per ADR-006 — marginal value without whole-zip import. |
| 28 | 014-runtime-llm-configuration-wizard.md | Runtime LLM configuration wizard | Superseded | TODO/ | Superseded by 043. Aspire-level approach blocked; replaced by in-app config wizard. |
| 29 | 029-system-prompt-and-profile-ui.md | System prompt and user profile UI | Done | Done/ | Depends on 28, 12. Settings page with GET/PUT /user-profile and /system-prompt endpoints. |
| 30 | 030-show-message-timestamps.md | Show message and conversation timestamps | Done | Done/ | Depends on 12, 22. Per-message timestamps and date separators. |
| 31 | 031-conversation-search.md | Conversation history search | Done | Done/ | Depends on 7, 10, 22. /search page using existing semantic search endpoint. |
| 32 | 032-tool-calling-status-indicators.md | Tool-calling status indicators in chat UI | Done | Done/ | Depends on 20, 12. SSE tool_start/tool_end events; "Searching memories..." indicator. |
| 33 | 033-spa-chat-navigation.md | SPA-style chat navigation | Done | Done/ | Depends on 22, 12. JS history.replaceState for URL updates without Blazor navigation. |
| 34 | 034-new-chat-with-conversation-context.md | Start new chat with conversation as context | Done | Done/ | Depends on 12, 22, 25. "Continue this conversation" button on imported read-only chats. |
| 35 | 035-auto-scroll-chat.md | Auto-scroll chat to bottom | Done | Done/ | Depends on 12. JS interop scrollToBottom with near-bottom detection. |
| 36 | 036-investigate-tool-search-results-ignored.md | Investigate LLM ignoring tool search results | Done | Done/ | Depends on 20, 32. Improved system prompt and tool description to be more directive. |
| 37 | 037-sidebar-overlay-layout.md | Sidebar should overlay chat area, not push it | Done | Done/ | Depends on 22, 12. Fixed overlay with backdrop and transform animation. |
| 38 | 038-add-cloud-provider-integrations.md | Add cloud LLM and vector store provider integrations | Done | Done/ | Depends on 3, 21. Anthropic, OpenAI direct, Gemini; Azure AI Search, Pinecone, Weaviate. See ADR-007. |
| 39 | 039-add-postgres-provider.md | Add Postgres as document DB and vector store provider | Done | Done/ | Depends on 38. Postgres via pgvector for both document DB and vector store, with shared Aspire resource. |
| 40 | 040-optional-authentication.md | Optional authentication with user-scoped data | Done | Done/ | Depends on 39. ASP.NET Core Identity; user-scoped conversations, chat sessions, and vector search. See ADR-008. |
| 41 | 041-rename-project.md | Rename the project | TODO | TODO/ | No dependencies. Prerequisite for Docker publish (042). |
| 42 | 042-publish-docker-image.md | Publish Docker image to public registry | TODO | TODO/ | Depends on 41. CI/CD to ghcr.io; Dockerfile; docker-compose variant templates. |
| 43 | 043-application-level-config-wizard.md | Application-level first-run configuration wizard | TODO | TODO/ | Depends on 040, 029. Supersedes 014. Env-var → DB → wizard config chain; LLM, vector store, auth setup in Blazor UI. |
| 44 | 044-plugin-extensibility-system.md | Plugin system for tool-calling extensibility | TODO | TODO/ | Depends on 020. Drop-in DLL plugins via Plugins/ directory; reflection-based loader; ToolPlugin contract in separate abstractions package. |
| 45 | 045-separate-auth-from-conversation-db.md | Separate auth backing store from document DB provider | TODO | TODO/ | Depends on 040. Independent auth DB config; UseDocumentDbForAuth option; Keycloak external provider via Aspire. |

---

## Architecture Decision Records

Significant architectural decisions must be recorded as ADRs in `docs/Decisions/`.

### When to create an ADR

- Adding or replacing an infrastructure component (database, message broker, LLM provider, etc.)
- Changing the data model or storage strategy in a non-trivial way
- Choosing between materially different implementation approaches
- Any decision that a future contributor would need context on to understand "why"

### How to create an ADR

1. Copy `Decisions/000-template.md` to a new file with the next available sequence number and a short slug, e.g. `001-use-qdrant-for-vectors.md`.
2. Fill in all sections of the template.
3. Reference the ADR from the relevant issue file(s) and note it in this index if appropriate.

---

## Project Overview

**MattGPT** imports an entire ChatGPT conversation history (~148 MB, ~2,213 conversations) and makes it available as RAG memory for any LLM. See [README.md](../../README.md) and [conversation-restore-outline.md](../TechnicalReference/conversation-restore-outline.md) for full context.

### Key Components (target architecture)

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Orchestrator | .NET Aspire | Local dev orchestration, service discovery, observability |
| Web Frontend | Blazor Server | Upload UI, chat UI, progress monitoring |
| API Service | ASP.NET Core Minimal API | Business logic, parsing, RAG pipeline |
| Document DB | MongoDB | Full conversation storage, metadata |
| Vector DB | Qdrant / Postgres (pgvector) / Azure AI Search / Pinecone / Weaviate (config-driven) | Embedding storage and similarity search |
| LLM | Ollama / Foundry Local / Azure OpenAI / OpenAI / Anthropic / Gemini (config-driven) | Summary generation, chat, embeddings |

---

← [Documentation home](../index.md)
