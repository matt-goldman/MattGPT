# 008 — Generate Conversation Summaries Using LLM

**Status:** Done
**Sequence:** 8
**Dependencies:** 003 (LLM config), 007 (conversations in MongoDB)

## Summary

For each stored conversation, use the configured LLM to generate a summary that captures the topic, key decisions, conclusions, and notable context. These summaries will be used as the basis for embedding generation.

## Requirements

1. Create a summarisation service that:
   - Reads conversations with `ProcessingStatus = Imported` from MongoDB.
   - Constructs an LLM prompt from the linearised messages (truncating or chunking if the conversation exceeds the model's context window).
   - Calls the LLM (via the abstraction from issue 003) to generate a summary.
   - Stores the summary back on the MongoDB document and updates `ProcessingStatus` to `Summarised`.
2. The summary prompt should instruct the LLM to capture:
   - What the conversation was about (topic, project, problem).
   - Key decisions, conclusions, or outputs.
   - Notable context (model used, code execution, files attached, images generated).
3. Implement rate limiting / batching to avoid overwhelming the LLM endpoint.
4. Handle failures gracefully — mark failed conversations with a `SummaryError` status and continue.
5. Expose a trigger endpoint (e.g. `POST /conversations/summarise`) and/or integrate into the background processing pipeline.

## Acceptance Criteria

- [x] Conversations with status `Imported` are summarised and updated to `Summarised`.
- [x] Summaries are stored in MongoDB alongside the full conversation.
- [x] The summarisation process handles LLM errors without aborting the batch.
- [x] Progress is trackable (number summarised, errors).
- [x] The summary content is meaningful and captures the key elements described above.

## Notes

- For very long conversations, consider summarising in chunks and then summarising the summaries, or truncating to the most recent N messages.
- The prompt template should be configurable or at least easy to iterate on.

## Post-Completion Changes

**2026-02-26 — ADR-003:** Summarisation is no longer a prerequisite for embedding or RAG retrieval. LLM summarisation at ~1 min/conversation via Ollama on CPU was prohibitively slow for 2,213+ conversations. The embedding pipeline was redesigned to work directly from conversation content, making summarisation an optional background enrichment step. If a summary exists it's included in the embedding text for potentially better quality, but it's not required. See [ADR-003](../Decisions/003-rag-pipeline-v2-embed-from-content.md).
