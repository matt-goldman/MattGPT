# 027 — Filter Hidden and System Messages from Embedding

**Status:** Done  
**Sequence:** 27  
**Dependencies:** 17 (export content analysis), 23 (handle non-text content types)

## Summary

Use message `weight` and `metadata.is_visually_hidden_from_conversation` to filter or de-prioritise hidden/system messages during summarisation and embedding. Currently, all linearised messages are treated equally, causing internal scaffolding and duplicated custom instructions to dilute RAG quality.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §5) found:
- **7,884 messages** with `weight: 0.0` — these are system messages, custom instructions, and internal scaffolding.
- **17,107 messages** with `is_visually_hidden_from_conversation: true`.
- **1,170 `user_editable_context`** messages — identical custom instructions injected into most conversations.

Including these in embedding text wastes the limited embedding context window (8,000 chars) on non-conversational content that adds noise rather than signal.

## Requirements

1. **Add `Weight` field to `Message` model** (`ChatGptExport.cs`) and `StoredMessage`.

2. **Add `IsHidden` field to `StoredMessage`** — derived from `metadata.is_visually_hidden_from_conversation`.

3. **Update `EmbeddingService.BuildEmbeddingText()`**: Skip messages where `Weight == 0.0` or `IsHidden == true`.

4. **Update `SummarisationService.BuildPrompt()`**: Skip hidden messages, but optionally annotate that custom instructions were present.

5. **Keep hidden messages in MongoDB** — they should still be stored for completeness and conversation display, just excluded from embedding/summary input.

6. **Unit tests** for filtering behaviour.

## Acceptance Criteria

- [x] `Weight` and `IsHidden` fields are captured on `StoredMessage`.
- [x] `BuildEmbeddingText()` excludes weight-0 and hidden messages.
- [x] `BuildPrompt()` excludes hidden messages.
- [x] Hidden messages are still stored in MongoDB for display purposes.
- [x] Existing tests pass; new tests for filtering logic.

## Notes

- This change should improve embedding quality by dedicating the 8,000-char window to actual conversational content instead of scaffolding.
- Consider whether `user_editable_context` content type alone is a sufficient filter signal, or whether `weight` + `is_hidden` is more robust. The analysis suggests `weight == 0.0` is the most reliable indicator.
