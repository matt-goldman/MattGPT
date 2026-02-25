# ADR-002: Chat UI Approach — Custom Blazor Page vs. OpenWebUI

**Date:** 2026-02-25
**Status:** Accepted
**Related Issues:** [012-chat-ui-with-rag.md](../TODO/012-chat-ui-with-rag.md)

### Context

Issue 012 calls for a chat interface that lets users interact with an LLM augmented by the RAG pipeline built in issues 010–011. Two candidate approaches were evaluated before implementation began:

1. **Custom Blazor page** — a new `/chat` page inside the existing `MattGPT.Web` Blazor Server project, following the same pattern as the existing file-upload UI.
2. **OpenWebUI** — an open-source, feature-rich chat UI distributed as a container image that can be added as an Aspire resource.

The central constraint is that *everything must be coordinated through Aspire*: all resources must be declared in the AppHost so that service discovery, health checks, and observability work consistently.

### Decision

Build a **custom Blazor chat page** (as described in issue 012) for v1.

### Consequences

**Easier:**
- The chat UI works with all three supported LLM providers (Foundry Local, Ollama, Azure OpenAI) without any additional plumbing — the same `IChatClient` abstraction used by the API service is available to any Blazor component.
- No new container image or external service to coordinate; the Blazor page is part of the existing `MattGPT.Web` project and is already orchestrated by Aspire.
- The UI can natively surface RAG-specific context (retrieved conversation titles, relevance scores) in a way that is tailored to MattGPT's domain, which would require custom work in OpenWebUI.
- Simpler local dev story — no additional container startup time or memory overhead.

**More difficult / trade-offs:**
- The Blazor UI will be more minimal than OpenWebUI out of the box (no conversation history persistence across sessions, no model switching UI, etc.) unless those features are explicitly built.
- Any UX improvements beyond MVP must be implemented rather than configured.

### Alternatives Considered

#### OpenWebUI

OpenWebUI (`ghcr.io/open-webui/open-webui`) is a polished, actively maintained chat front-end with streaming responses, conversation management, and model selection built in.

**Why rejected for v1:**

The Aspire Community Toolkit provides `CommunityToolkit.Aspire.Hosting.OpenWebUI`, but at the time of this decision it only integrates with Ollama (the resource is added via `.WithOpenWebUI()` on an Ollama resource). MattGPT is designed to be provider-agnostic — the active LLM provider is selected at startup via `LLM:Provider` configuration and could be Foundry Local or Azure OpenAI, not necessarily Ollama.

Adopting OpenWebUI for v1 would either:
- Constrain the application to Ollama as the sole LLM provider, breaking the multi-provider design; or
- Require running OpenWebUI alongside the existing API service and duplicating the RAG pipeline logic so that requests flow through OpenWebUI's plugin/tool system rather than the MattGPT API — a significant scope increase for MVP.

**Future consideration:** If Aspire's OpenWebUI integration is extended to support arbitrary OpenAI-compatible endpoints (not just Ollama), or if MattGPT standardises on Ollama as the primary local provider, replacing the Blazor chat page with OpenWebUI should be re-evaluated. The change would be primarily in the AppHost and could retire the Blazor `/chat` page entirely.
