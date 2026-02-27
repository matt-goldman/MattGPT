# 026 — Capture Conversation-Level Metadata

**Status:** TODO  
**Sequence:** 26  
**Dependencies:** 4 (ChatGPT JSON parser), 7 (store conversations in MongoDB), 17 (export content analysis)

## Summary

Extend the `Conversation` and `StoredConversation` models to capture conversation-level metadata fields that are currently discarded: `gizmo_id`, `conversation_template_id`, `is_do_not_remember`, `memory_scope`, and `is_archived`.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §2, §5) found several conversation-level fields with filtering/faceting value:
- `gizmo_id` (758 conversations) — identifies which custom GPT was used
- `conversation_template_id` (758 conversations) — project/template association
- `is_do_not_remember` (1,026 conversations) — user opted out of memory
- `memory_scope` (2,913) — `global_enabled` or `project_enabled`
- `is_archived` (2,913) — archived status

## Requirements

1. **Extend `Conversation` model** (`ChatGptExport.cs`):
   - `GizmoId` (string?)
   - `ConversationTemplateId` (string?)
   - `IsDoNotRemember` (bool?)
   - `MemoryScope` (string?)
   - `IsArchived` (bool?)

2. **Extend `StoredConversation` model**:
   - Same fields as above.
   - Update `StoredConversation.From()` to map them.

3. **Respect `is_do_not_remember`**: Conversations with this flag set should be flagged/annotated during import. Consider whether they should be excluded from RAG indexing entirely or just marked.

4. **Qdrant metadata**: Add `gizmo_id` and `is_archived` to the Qdrant point payload so they can be used as filters in vector search.

5. **Unit tests** for new field mapping.

## Acceptance Criteria

- [ ] `gizmo_id`, `conversation_template_id`, `is_do_not_remember`, `memory_scope`, `is_archived` are captured and stored.
- [ ] `is_do_not_remember` conversations are handled appropriately (at minimum, flagged).
- [ ] New fields are included in Qdrant payload for filtering.
- [ ] Existing tests pass; new field-mapping tests added.

## Notes

- `gizmo_type` can be captured alongside `gizmo_id` but has low independent value (values: null, "snorlax", "gpt").
- The `is_do_not_remember` handling decision may warrant an ADR if the team decides to exclude these from RAG entirely.
