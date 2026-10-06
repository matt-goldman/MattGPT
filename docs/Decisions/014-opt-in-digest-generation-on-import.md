## ADR-014: Opt-in digest generation on import

**Date:** 2026-10-06
**Status:** Accepted. Supersedes the part of [ADR-003](003-rag-pipeline-v2-embed-from-content.md) (decision 1) that took summarisation out of the import pipeline.
**Related Issues:** [048 — Export conversation summary and in-app notifications](../Backlog/Done/048-export-summary-and-notifications.md)

### Context

ADR-003 removed LLM summarisation from import because Ollama was running on CPU: about a minute per conversation, so roughly 37 hours for a 2,213-conversation export. That was the right call under that constraint.

The constraint has changed. Inference now runs on a GPU, at a few seconds per conversation or less. Leaving summarisation out of import also had costs that weren't visible at the time:

- A freshly imported conversation never had a digest, so the summary part of the embedding text never applied on the import path.
- Digests generated later by the bulk endpoint were never embedded. Embedding read the summary once, at embed time, and nothing re-triggered it. Embedding coverage across the corpus therefore depended on the order jobs happened to run in.

### Decision

1. **Import can generate digests, opt-in.** The Upload page has a **Generate summaries** checkbox, unchecked by default, with a visible warning that it slows the import. The cost scales with corpus size and with whichever provider and model are configured, and neither is known in advance, so it stays a choice rather than a default.
2. **Digests come before embedding** in the import pipeline (`ImportProcessingService`: import → digests → embed), so the embedding includes them.
3. **A digest generated after embedding is embedded straight away.** `SummarisationService.SummariseConversationAsync(..., embedAfter: true)` re-embeds that one conversation. Every vector store keeps one vector per conversation, built from title + digest + messages, so a targeted re-embed of that conversation is the smallest unit available. There is no separate digest vector to upsert.
4. **Digest state is tracked separately from embedding state** (`StoredConversation.SummaryStatus`: `None` / `Generated` / `Skipped` / `Failed`). A digest failure no longer sets `ProcessingStatus = SummaryError`, which used to leave the conversation never embedded. `Summarised` now means "digest newer than embedding", and `GET /conversations/diagnostics` reports how many conversations are in that state (`digestsAwaitingEmbedding`). An embed run brings it back to zero.
5. **One implementation.** The bulk endpoint, import, on-demand jobs and chat sessions all go through the single-conversation methods on `SummarisationService`.

### Consequences

- Import with the box unchecked behaves exactly as before.
- With the box checked, import time is dominated by LLM latency. Progress shows as a separate "Summarising" phase.
- The bulk `POST /conversations/summarise` now runs in the background (via `SummaryJobQueue`) and notifies on completion, rather than holding an HTTP request open for hours.
- Legacy `SummaryError` conversations are now embeddable, so the next embed run picks them up.

### Alternatives Considered

- **Always summarise on import.** Rejected: the cost depends on the deployment's provider and model, which the code can't know.
- **Keep import summarisation out and just fix the re-embed ordering.** That fixes coverage but leaves new imports without digests until someone runs the bulk job. The checkbox costs nothing when unchecked.
