# 007 — Store Parsed Conversations in MongoDB

**Status:** TODO
**Sequence:** 7
**Dependencies:** 001 (MongoDB in AppHost), 004 (parser), 006 (background processing)

## Summary

Extend the background processing pipeline to store each parsed conversation as a document in MongoDB. This preserves the complete original content for later retrieval and full-context RAG.

## Requirements

1. Define a MongoDB document model for stored conversations. At minimum:
   - `ConversationId` (from the export's `id` field — use as the document `_id` or a unique index)
   - `Title`
   - `CreateTime`, `UpdateTime`
   - `DefaultModelSlug`
   - `LinearisedMessages` (the flat list of active-thread messages)
   - `RawMapping` (optional — store the original mapping for future use)
   - `ImportTimestamp`
   - `ProcessingStatus` (e.g. `Imported`, `Summarised`, `Embedded`)
2. Use the MongoDB .NET driver (configured via Aspire DI from issue 001) to insert documents.
3. Handle upserts — re-importing the same file should update existing conversations, not create duplicates.
4. Update the background processing service to store each parsed conversation as it is processed.
5. Add a simple API endpoint to query stored conversations (e.g. `GET /conversations?page=1&pageSize=20`).

## Acceptance Criteria

- [ ] Parsed conversations are stored in MongoDB during import processing.
- [ ] Re-importing the same file does not create duplicates (upsert by conversation ID).
- [ ] The `GET /conversations` endpoint returns paginated conversation metadata.
- [ ] `ProcessingStatus` is set to `Imported` on initial storage.
- [ ] MongoDB data is visible/queryable via the Aspire dashboard or a simple API call.

## Notes

- Keep the document model lean for now. Additional fields can be added as needed by later issues.
- Consider creating indexes on `ConversationId`, `CreateTime`, and `ProcessingStatus`.
