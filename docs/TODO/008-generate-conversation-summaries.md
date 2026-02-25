# 008 — Generate Conversation Summaries Using LLM

**Status:** TODO
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

- [ ] Conversations with status `Imported` are summarised and updated to `Summarised`.
- [ ] Summaries are stored in MongoDB alongside the full conversation.
- [ ] The summarisation process handles LLM errors without aborting the batch.
- [ ] Progress is trackable (number summarised, errors).
- [ ] The summary content is meaningful and captures the key elements described above.

## Notes

- For very long conversations, consider summarising in chunks and then summarising the summaries, or truncating to the most recent N messages.
- The prompt template should be configurable or at least easy to iterate on.
